using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlNoticeTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlNoticeTests(IdentitySqlFixture fixture) => this.fixture = fixture;
    [Fact]
    public async Task Notice_jobs_retry_once_and_customer_read_checks_live_scope()
    {
        // Seed a pending customer request with a valid lease/grant fixture; notification does not post inventory.
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today, today.AddDays(3));
        Guid lease;
        await using (var db = fixture.Db()) { lease = await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.LeaseId).SingleAsync();
            db.StockRequests.Add(new WarehouseRent.Infrastructure.Stock.StockRequest { Id = Guid.NewGuid(), OrganizationId = DemoIdentitySeed.OrganizationA, FacilityId = DemoIdentitySeed.FacilityA,
                LeaseId = lease, LeaseUnitId = grant, Type = "Receipt", State = "Submitted", CreatedBy = "admin.a@example.test", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        var enqueue = $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/notice-jobs/enqueue"; var dispatch = $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/notice-jobs/dispatch";
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, enqueue, new { page = 1 }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, enqueue, new { page = 1 }, key: Guid.NewGuid())).StatusCode);
        var commands = await Task.WhenAll(manager.Send(HttpMethod.Post, dispatch, new { page = 1 }, key: Guid.NewGuid()), manager.Send(HttpMethod.Post, dispatch, new { page = 1 }, key: Guid.NewGuid()));
        Assert.All(commands, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var path = $"/api/v1/notices?organizationId={DemoIdentitySeed.OrganizationA}&facilityId={DemoIdentitySeed.FacilityA}";
        var read = await customer.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync()); var rows = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length); Assert.Equal(2, rows.Select(x => x.GetProperty("id").GetGuid()).Distinct().Count());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, enqueue, new { page = 1 }, key: Guid.NewGuid())).StatusCode);
    }
}
