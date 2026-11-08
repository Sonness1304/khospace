using System.Net;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Gates;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
public sealed partial class SqlGateTests
{
    private async Task<Guid> SeedVisit(Guid lane, Guid leaseId, DateTimeOffset start, string state = "Scheduled")
    {
        await using var db = fixture.Db(); var lease = await db.Leases.SingleAsync(x => x.Id == leaseId); var now = DateTimeOffset.UtcNow;
        var visit = new GateVisit { Id = Guid.NewGuid(), LaneId = lane, FacilityId = lease.FacilityId, OrganizationId = lease.OrganizationId, LeaseId = lease.Id, Direction = "Outbound", Vehicle = "SYNTHETIC-123",
            StartUtc = start, EndUtc = start.AddMinutes(30), State = state, CreatedBy = "synthetic-test-fixture", CreatedAt = now, UpdatedAt = now,
            ArrivedAt = state == "CheckedIn" ? start : null, ArrivedBy = state == "CheckedIn" ? "clerk@example.test" : null };
        db.GateVisits.Add(visit); await db.SaveChangesAsync(); return visit.Id;
    }
    [Fact]
    public async Task Simultaneous_empty_lane_reservations_commit_exactly_one_visit()
    {
        using var manager = await fixture.Login("manager@example.test"); using var a = await fixture.Login(); using var b = await fixture.Login("admin2.a@example.test");
        var lane = Resource(await Json(await Lane(manager))); var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddHours(3);
        var results = await Task.WhenAll(Schedule(a, lane, lease, start, start.AddMinutes(30)), Schedule(b, lane, lease, start, start.AddMinutes(30)));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.Db(); Assert.Single(await db.GateVisits.Where(x => x.LaneId == lane).ToListAsync());
    }
    [Theory]
    [InlineData(-60, 30)]
    [InlineData(60, 4)]
    [InlineData(60, 241)]
    [InlineData(131041, 30)]
    public async Task Invalid_past_short_long_or_distant_windows_never_create_visits(int minutes, int duration)
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        var lane = Resource(await Json(await Lane(manager))); var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddMinutes(minutes);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Schedule(customer, lane, lease, start, start.AddMinutes(duration))).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.GateVisits.AnyAsync(x => x.LaneId == lane));
    }
    [Fact]
    public async Task Lane_status_requires_version_and_cancellation_frees_window_without_stock_change()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login(); using var clerk = await fixture.Login("clerk@example.test");
        var laneResponse = await Json(await Lane(manager)); var lane = Resource(laneResponse); var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddHours(4);
        var made = await Json(await Schedule(customer, lane, lease, start, start.AddMinutes(30))); var id = Resource(made); var version = made.GetProperty("result").GetProperty("version").GetString();
        var body = new { active = false, reason = "Close synthetic lane" };
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await manager.Send(HttpMethod.Post, $"/api/v1/gate-lanes/{lane}/status", body, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/gate-lanes/{lane}/status", body, key: Guid.NewGuid(), version: laneResponse.GetProperty("result").GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-lanes/{lane}/status", body, key: Guid.NewGuid(), version: laneResponse.GetProperty("result").GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await customer.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/cancel", new { reason = "Cancel visit" }, key: Guid.NewGuid(), version: "stale")).StatusCode);
        var key = Guid.NewGuid();
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/cancel", new { reason = "Cancel visit" }, key: key, version: version)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Schedule(customer, lane, lease, start, start.AddMinutes(30))).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.StockPostings.AnyAsync(x => x.OrganizationId == DemoIdentitySeed.OrganizationA));
        Assert.Single(await db.SecurityAudits.Where(x => x.AggregateId == id && x.Action == "GateVisit-cancel").ToListAsync());
    }
}
