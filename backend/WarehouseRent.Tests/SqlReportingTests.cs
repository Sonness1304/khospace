using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlReportingTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlReportingTests(IdentitySqlFixture fixture) => this.fixture = fixture;
    [Fact]
    public async Task Utilization_excludes_maintenance_and_separates_current_custody_and_funds()
    {
        var start = new DateOnly(2040, 1, 1); var end = start.AddDays(10);
        Guid unitId;
        await using (var db = fixture.Db())
        {
            var source = await db.RentableUnits.SingleAsync(x => x.Id == SqlFacilitiesTests.UnitA);
            // A dedicated facility keeps the expected denominator independent of catalog seed size.
            var facility = new Facility { Id = Guid.NewGuid(), Code = "REPORT-" + Guid.NewGuid().ToString("N")[..8], Name = "Synthetic report", Address = "Demo", PermittedGoods = "Dry", Published = true };
            var zone = new Zone { Id = Guid.NewGuid(), FacilityId = facility.Id, Code = "DRY" };
            var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = facility.Id, ZoneId = zone.Id, Code = "R-1", AreaM2 = 10, Published = true };
            db.Facilities.Add(facility); db.Zones.Add(zone); db.RentableUnits.Add(unit); await db.SaveChangesAsync(); unitId = unit.Id;
            var maintenance = new MaintenanceBlock { Id = Guid.NewGuid(), FacilityId = facility.Id, StartDate = start, EndDate = start.AddDays(2), Reason = "Fixture maintenance" };
            db.MaintenanceBlocks.Add(maintenance); db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = unit.Id, StartDate = start, EndDate = start.AddDays(2), State = "Maintenance", MaintenanceId = maintenance.Id }); await db.SaveChangesAsync();
        }
        var line = await fixture.SeedAllocationOwner(unitId, start.AddDays(2), start.AddDays(7)); Guid facilityId;
        await using (var db = fixture.Db())
        { facilityId = await db.RentableUnits.Where(x => x.Id == unitId).Select(x => x.FacilityId).SingleAsync();
          db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = unitId, BookingLineId = line, State = "Confirmed", StartDate = start.AddDays(2), EndDate = start.AddDays(7) }); await db.SaveChangesAsync(); }
        using var op = await fixture.Login("operator@example.test");
        var response = await op.Send(HttpMethod.Get, $"/api/v1/reports/facilities/{facilityId}?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var result = json.RootElement;
        Assert.Equal(80, result.GetProperty("scheduled").GetProperty("rentableAreaDays").GetDecimal());
        Assert.Equal(50, result.GetProperty("scheduled").GetProperty("bookedAreaDays").GetDecimal());
        Assert.Equal(62.5m, result.GetProperty("scheduled").GetProperty("utilizationPercent").GetDecimal());
        Assert.Equal(0, result.GetProperty("physical").GetProperty("occupiedAreaM2").GetDecimal());
        Assert.Equal(0, result.GetProperty("collections").GetProperty("grossVerifiedReceiptsVnd").GetDecimal());
        using var customer = await fixture.Login(); Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Get, $"/api/v1/reports/facilities/{facilityId}?startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}")).StatusCode);
    }
    [Fact]
    public async Task Export_is_customer_scoped_formula_safe_and_audited()
    {
        using var admin = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        var path = $"/api/v1/exports/stock?organizationId={DemoIdentitySeed.OrganizationA}&facilityId={DemoIdentitySeed.FacilityA}&startDate=2026-01-01&endDate=2027-01-01";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, path)).StatusCode);
        var response = await admin.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("posting_id", await response.Content.ReadAsStringAsync());
        await using var db = fixture.Db(); Assert.True(await db.SecurityAudits.AnyAsync(x => x.ActorId == "admin.a@example.test" && x.Action == "StockExported" && x.OrganizationId == DemoIdentitySeed.OrganizationA));
    }
    [Fact]
    public async Task Financial_closure_preserves_past_scheduled_utilization()
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today.AddDays(-12), today.AddDays(-2)); Guid account;
        await using (var db = fixture.Db())
        { var leaseId = await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.LeaseId).SingleAsync(); var lease = await db.Leases.SingleAsync(x => x.Id == leaseId); lease.State = "SettlementPending"; lease.ClearedAt = DateTimeOffset.UtcNow; account = lease.RentalAccountId; await db.SaveChangesAsync(); }
        using var manager = await fixture.Login("manager@example.test"); using var finance = await fixture.Login("finance@example.test");
        var path = $"/api/v1/reports/facilities/{DemoIdentitySeed.FacilityA}?startDate={today.AddDays(-12):yyyy-MM-dd}&endDate={today.AddDays(-2):yyyy-MM-dd}";
        async Task<decimal> Numerator() { var response = await manager.Send(HttpMethod.Get, path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("scheduled").GetProperty("bookedAreaDays").GetDecimal(); }
        var before = await Numerator(); Assert.True(before > 0);
        await using (var db = fixture.Db())
        { var version = Convert.ToBase64String((await db.RentalAccounts.AsNoTracking().SingleAsync(x => x.Id == account)).Version);
          Assert.Equal(HttpStatusCode.OK, (await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/close", new { reason = "Close synthetic settled history", closureConfirmed = true }, version: version, key: Guid.NewGuid())).StatusCode); }
        Assert.Equal(before, await Numerator());
        // Simulate an allocation released by the older closure implementation; restore/report must retain its historical prefix too.
        await using (var db = fixture.Db())
        { var initial = await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.InitialAllocationId).SingleAsync(); (await db.SpaceAllocations.SingleAsync(x => x.Id == initial)).State = "Released"; await db.SaveChangesAsync(); }
        Assert.Equal(before, await Numerator());
    }

}
