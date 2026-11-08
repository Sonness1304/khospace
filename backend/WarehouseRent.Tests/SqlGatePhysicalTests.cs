using System.Net;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
public sealed partial class SqlGateTests
{
    [Fact]
    public async Task Overstay_blocks_arrival_is_listed_outside_range_and_departure_survives_termination()
    {
        using var manager = await fixture.Login("manager@example.test"); using var clerk = await fixture.Login("clerk@example.test"); using var customer = await fixture.Login();
        var lane = Resource(await Json(await Lane(manager))); var lease = await Lease();
        var overstaying = await SeedVisit(lane, lease, DateTimeOffset.UtcNow.AddHours(-2), "CheckedIn"); var current = await SeedVisit(lane, lease, DateTimeOffset.UtcNow.AddMinutes(-5));
        var visit = await Json(await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits/{current}")); var action = new { reason = "Physical arrival" };
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{current}/arrive", action, key: Guid.NewGuid(), version: visit.GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.Send(HttpMethod.Post, $"/api/v1/gate-visits/{current}/arrive", action, key: Guid.NewGuid(), version: visit.GetProperty("version").GetString())).StatusCode);
        var start = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ")); var end = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ssZ"));
        var list = await Json(await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits?facilityId={DemoIdentitySeed.FacilityA}&startUtc={start}&endUtc={end}"));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == overstaying);
        var old = await Json(await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits/{overstaying}"));
        await using (var db = fixture.Db()) { var row = await db.Leases.SingleAsync(x => x.Id == lease); row.State = "ClearancePending"; row.TerminatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{overstaying}/depart", action, key: Guid.NewGuid(), version: old.GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{current}/arrive", action, key: Guid.NewGuid(), version: visit.GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/gate-visits/{current}/cancel", action, key: Guid.NewGuid(), version: visit.GetProperty("version").GetString())).StatusCode);
    }
    [Fact]
    public async Task Arrival_departure_preserves_observed_events_and_forbids_cancel_after_arrival()
    {
        using var manager = await fixture.Login("manager@example.test"); using var clerk = await fixture.Login("clerk@example.test");
        var lane = Resource(await Json(await Lane(manager))); var lease = await Lease(); var id = await SeedVisit(lane, lease, DateTimeOffset.UtcNow.AddMinutes(-1));
        var before = await Json(await clerk.Send(HttpMethod.Get, $"/api/v1/gate-visits/{id}")); var body = new { reason = "Observed vehicle" }; var key = Guid.NewGuid();
        var arrived = await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/arrive", body, key: key, version: before.GetProperty("version").GetString()); Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/arrive", body, key: key, version: before.GetProperty("version").GetString())).StatusCode);
        var current = (await Json(arrived)).GetProperty("result"); Assert.Equal("CheckedIn", current.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/cancel", body, key: Guid.NewGuid(), version: current.GetProperty("version").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Post, $"/api/v1/gate-visits/{id}/depart", body, key: Guid.NewGuid(), version: current.GetProperty("version").GetString())).StatusCode);
        await using var db = fixture.Db(); var saved = await db.GateVisits.SingleAsync(x => x.Id == id); Assert.Equal("Completed", saved.State); Assert.True(saved.DepartedAt >= saved.ArrivedAt);
        saved.Vehicle = "REWRITE"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        saved = await db.GateVisits.SingleAsync(x => x.Id == id); saved.ArrivedAt = saved.ArrivedAt!.Value.AddSeconds(1); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        db.GateVisits.Remove(await db.GateVisits.SingleAsync(x => x.Id == id)); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
