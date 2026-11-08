using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class CatalogAuditTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Command_receipt_failure_rolls_back_maintenance_allocations_and_audit()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(fixture.Connection).AddInterceptors(new RejectCatalogReceipt()).Options));
        }));
        using var browser = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "operator@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        await browser.RefreshCsrf();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(42);
        var key = Guid.NewGuid();
        var response = await browser.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", new { facilityId = DemoIdentitySeed.FacilityA,
            unitIds = new[] { SqlFacilitiesTests.UnitA, SqlFacilitiesTests.UnitB }, startDate = start, endDate = start.AddDays(2), reason = "Synthetic catalog receipt fault" }, key: key);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = fixture.Db();
        Assert.False(await db.SpaceAllocations.AnyAsync()); Assert.False(await db.MaintenanceBlocks.AnyAsync());
        Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.False(await db.SecurityAudits.AnyAsync(x => x.Action == "MaintenanceCreated"));
    }
}
public sealed class RejectCatalogReceipt : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (data.Context!.ChangeTracker.Entries<OperationalCommand>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Synthetic command receipt fault");
        return ValueTask.FromResult(result);
    }
}
