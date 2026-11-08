using System.Net;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
namespace WarehouseRent.Tests;
public sealed partial class SqlGateTests
{
    [Fact]
    public async Task Disabled_lane_unique_code_and_command_readback_follow_live_staff_roles()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        var code = "UNIQUE-" + Guid.NewGuid().ToString("N").ToUpperInvariant(); var laneMade = await Json(await Lane(manager, code)); var lane = Resource(laneMade);
        Assert.Equal(HttpStatusCode.Conflict, (await Lane(manager, code)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Lane(manager, "lowercase")).StatusCode);
        var key = Guid.NewGuid(); var status = await manager.Send(HttpMethod.Post, $"/api/v1/gate-lanes/{lane}/status", new { active = false, reason = "Maintenance" }, key: key, version: laneMade.GetProperty("result").GetProperty("version").GetString()); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.FacilityA}")).StatusCode);
        var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddHours(1);
        Assert.Equal(HttpStatusCode.Conflict, (await Schedule(customer, lane, lease, start, start.AddMinutes(30))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/gate-lanes/{lane}/status", new { active = true, reason = "Ready" }, key: Guid.NewGuid(), version: (await Json(status)).GetProperty("result").GetProperty("version").GetString())).StatusCode);
        var visitKey = Guid.NewGuid(); Assert.Equal(HttpStatusCode.OK, (await Schedule(manager, lane, lease, start, start.AddMinutes(30), visitKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Get, $"/api/v1/commands/{visitKey}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
    }
    [Fact]
    public async Task Whole_window_uses_facility_local_lease_end_and_requires_explicit_utc()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login(); var lane = Resource(await Json(await Lane(manager))); var lease = await Lease();
        var localEnd = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime).AddDays(2);
        await using (var db = fixture.Db()) { var row = await db.Leases.SingleAsync(x => x.Id == lease); row.EndDate = localEnd; await db.SaveChangesAsync(); }
        var midnight = new DateTimeOffset(localEnd.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        Assert.Equal(HttpStatusCode.OK, (await Schedule(customer, lane, lease, midnight.AddMinutes(-30), midnight)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Schedule(customer, lane, lease, midnight, midnight.AddMinutes(30))).StatusCode);
        var start = DateTimeOffset.UtcNow.AddHours(1);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await customer.Send(HttpMethod.Post, "/api/v1/gate-visits", new { laneId = lane, leaseId = lease, direction = "Inbound", vehicle = "51C-123", startUtc = start.ToString("yyyy-MM-ddTHH:mm:ss"), endUtc = start.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ss"), reason = "Missing explicit timezone" }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Early_and_expired_arrivals_are_rejected_and_lists_require_bounded_utc_ranges()
    {
        using var manager = await fixture.Login("manager@example.test"); using var clerk = await fixture.Login("clerk@example.test"); var lane = Resource(await Json(await Lane(manager))); var lease = await Lease();
        foreach (var start in new[] { DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1) }) {
            var id = await SeedVisit(lane, lease, start); var visit = await Json(await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits/{id}"));
            Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/arrive", new { reason = "Wrong time" }, key: Guid.NewGuid(), version: visit.GetProperty("version").GetString())).StatusCode);
        }
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits?facilityId={DemoIdentitySeed.FacilityA}&startUtc=2026-01-01T00:00:00Z&endUtc=2026-03-01T00:00:00Z")).StatusCode);
        using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/gate-lanes?facilityId={DemoIdentitySeed.FacilityA}&organizationId={DemoIdentitySeed.OrganizationA}")).StatusCode);
    }
}
