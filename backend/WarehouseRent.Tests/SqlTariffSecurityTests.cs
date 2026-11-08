using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Tests;
public sealed partial class SqlTariffTests
{
    private async Task<BrowserSession> LoginHost(WebApplicationFactory<Program> host,string user)
    {
        var browser=new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost"),HandleCookies=false}));await browser.RefreshCsrf();await Data(await browser.Send(HttpMethod.Post,"/api/v1/auth/login",new{userName=user,password=DemoIdentitySeed.Password}));await browser.RefreshCsrf();return browser;
    }
    [Fact]
    public async Task Revocation_between_precheck_and_identity_lock_prevents_rate_and_receipt()
    {
        var unit=await Unit();var interceptor=new RevokeManagerAtWrite(fixture);
        using var host=fixture.Host.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<AppDbContext>();s.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(interceptor).Options));}));
        using var manager=await LoginHost(host,"manager@example.test");interceptor.Armed=true;var key=Guid.NewGuid();
        try {
            Assert.Equal(HttpStatusCode.NotFound,(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,key)).StatusCode);
            Assert.True(interceptor.Revoked, "The synthetic revocation must run before the identity lock.");
            await using var db=fixture.Db();Assert.False(await db.RateVersions.AnyAsync(x=>x.UnitId==unit.Id));Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==key));
        } finally {if(interceptor.Revoked){await using var restore=fixture.Db();restore.StaffFacilityScopes.Add(new StaffFacilityScope {UserId="manager@example.test",FacilityId=DemoIdentitySeed.FacilityA,StaffRole="FacilityManager"});await restore.SaveChangesAsync();}}
    }
    private sealed class RevokeManagerAtWrite(IdentitySqlFixture fixture) : DbCommandInterceptor
    {
        public bool Armed {get;set;}
        public bool Revoked {get;private set;}
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData eventData,DbDataReader result,CancellationToken cancellationToken=default)
        {
            // PG mutexes use direct ADO commands and do not raise EF NonQuery events.
            // Revoke after the server has evaluated the OUTSIDE-transaction grant precheck,
            // before CommandExecutor opens its transaction and acquires the identity mutex.
            if(Armed && !Revoked && eventData.Context is { } context && context.Database.IsNpgsql()
                && context.Database.CurrentTransaction is null && command.CommandText.Contains("\"StaffFacilityScopes\"",StringComparison.Ordinal)) {
                await using var db=fixture.Db();
                var removed=await db.StaffFacilityScopes.Where(x=>x.UserId=="manager@example.test" && x.FacilityId==DemoIdentitySeed.FacilityA && x.StaffRole=="FacilityManager").ExecuteDeleteAsync(cancellationToken);
                Revoked=removed==1;
            }
            return result;
        }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {
            if(Armed && !Revoked && command.CommandText.Contains("sp_getapplock",StringComparison.Ordinal) && command.Parameters.Cast<DbParameter>().Any(p=>p.Value is string value && value=="WarehouseRent.IdentityDirectory.v1")) {await using var db=fixture.Db();var removed=await db.StaffFacilityScopes.Where(x=>x.UserId=="manager@example.test" && x.FacilityId==DemoIdentitySeed.FacilityA && x.StaffRole=="FacilityManager").ExecuteDeleteAsync(cancellationToken);Revoked=removed==1;}
            return result;
        }
    }
    [Fact]
    public async Task Receipt_failure_rolls_back_new_rate_prior_window_and_unit_version()
    {
        var unit=await Unit();await using(var seed=fixture.Db()){seed.RateVersions.Add(new WarehouseRent.Infrastructure.Facilities.RateVersion {Id=Guid.NewGuid(),UnitId=unit.Id,DailyRate=100,SalesValidFrom=Today.AddDays(-1)});await seed.SaveChangesAsync();}
        using var host=fixture.Host.WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<AppDbContext>();s.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectCatalogReceipt()).Options));}));
        using var manager=await LoginHost(host,"operator@example.test");var key=Guid.NewGuid();Assert.Equal(HttpStatusCode.InternalServerError,(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,key)).StatusCode);
        await using var db=fixture.Db();var rate=await db.RateVersions.SingleAsync(x=>x.UnitId==unit.Id);Assert.Equal("Daily",rate.BillingBasis);Assert.Null(rate.SalesValidTo);Assert.Equal(unit.Version,Convert.ToBase64String((await db.RentableUnits.SingleAsync(x=>x.Id==unit.Id)).Version));Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==key));
    }
}
