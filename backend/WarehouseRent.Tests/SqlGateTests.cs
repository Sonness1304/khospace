using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed partial class SqlGateTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<Guid> Lease()
    {
        await using var db = fixture.Db(); var original = await db.RentableUnits.FirstAsync(x => x.FacilityId == DemoIdentitySeed.FacilityA);
        var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = original.FacilityId, ZoneId = original.ZoneId, Code = "G-" + Guid.NewGuid().ToString("N")[..12], AreaM2 = 10, Published = true };
        db.RentableUnits.Add(unit); await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var grant = await fixture.SeedOccupancyOwner(unit.Id, today, today.AddDays(100));
        return await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.LeaseId).SingleAsync();
    }
    private static Task<HttpResponseMessage> Lane(BrowserSession staff, string? code = null, Guid? key = null) => staff.Send(HttpMethod.Post, "/api/v1/gate-lanes", new { facilityId = DemoIdentitySeed.FacilityA, code = code ?? "GATE-" + Guid.NewGuid().ToString("N").ToUpperInvariant(), reason = "Synthetic gate" }, key: key ?? Guid.NewGuid());
    private static Task<HttpResponseMessage> Schedule(BrowserSession actor, Guid lane, Guid lease, DateTimeOffset start, DateTimeOffset end, Guid? key = null) => actor.Send(HttpMethod.Post, "/api/v1/gate-visits", new { laneId = lane, leaseId = lease, direction = "Inbound", vehicle = "51C-123.45", startUtc = start, endUtc = end, reason = "Synthetic visit" }, key: key ?? Guid.NewGuid());
    private static Guid Resource(JsonElement value) => value.GetProperty("result").GetProperty("resourceId").GetGuid();
    [Fact]
    public async Task Customer_schedule_replays_and_adjacent_windows_are_allowed()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        var response = await Lane(manager); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var lane = Resource(await Json(response));
        var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddHours(1); var end = start.AddMinutes(30); var key = Guid.NewGuid();
        var first = await Schedule(customer, lane, lease, start, end, key); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var replay = await Schedule(customer, lane, lease, start, end, key); Assert.Equal(Resource(await Json(first)), Resource(await Json(replay)));
        Assert.Equal(HttpStatusCode.Conflict, (await Schedule(customer, lane, lease, start.AddMinutes(1), end)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Schedule(customer, lane, lease, end, end.AddMinutes(30))).StatusCode);
    }
    [Fact]
    public async Task Customer_cannot_manage_lanes_or_read_another_customers_visits()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await Lane(customer)).StatusCode);
        var lane = Resource(await Json(await Lane(manager))); var lease = await Lease(); var start = DateTimeOffset.UtcNow.AddHours(2);
        var visit = Resource(await Json(await Schedule(customer, lane, lease, start, start.AddMinutes(20))));
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/gate-visits/{visit}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Schedule(foreign, lane, lease, start.AddHours(1), start.AddHours(1).AddMinutes(20))).StatusCode);
    }
}
