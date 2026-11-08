using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;
public sealed partial class SqlGateTests
{
    [Fact]
    public async Task Visit_command_receipt_failure_rolls_back_visit_and_audit()
    {
        using var manager = await fixture.Login("manager@example.test"); var lane = Resource(await Json(await Lane(manager))); var lease = await Lease(); var key = Guid.NewGuid();
        using var host = fixture.Host.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<AppDbContext>(); s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectCatalogReceipt()).Options)); }));
        using var customer = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await customer.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode); await customer.RefreshCsrf();
        var start = DateTimeOffset.UtcNow.AddHours(5); Assert.Equal(HttpStatusCode.InternalServerError, (await Schedule(customer, lane, lease, start, start.AddMinutes(20), key)).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.GateVisits.AnyAsync(x => x.LaneId == lane)); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.False(await db.SecurityAudits.AnyAsync(x => x.Action == "GateVisitScheduled" && x.Reason == "Synthetic visit" && !db.GateVisits.Any(v => v.Id == x.AggregateId)));
    }
    [Fact]
    public async Task Populated_gate_migration_refuses_downgrade_and_retains_history()
    {
        using var manager = await fixture.Login("manager@example.test"); var lane = Resource(await Json(await Lane(manager))); await using var db = fixture.Db();
        var migrator = db.GetService<IMigrator>();
        if(db.Database.IsNpgsql())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>migrator.MigrateAsync("20261029054446_OnlinePaymentIntents"));
            Assert.True(await db.GateLanes.AnyAsync(x=>x.Id==lane));
            Assert.True(await PostgresMigrations.IsReadyAsync(db,default));
            return;
        }
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => migrator.MigrateAsync("20261029054446_OnlinePaymentIntents"));
        Assert.True(await db.GateLanes.AnyAsync(x => x.Id == lane));
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), x => x.EndsWith("GateVisitScheduling"));
    }
}
