using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Facilities;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlOperationsTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Calendar_rejects_local_midnight_outside_representable_utc_range()
    {
        using var manager = await fixture.Login("manager@example.test");
        var result = await manager.Send(HttpMethod.Get, $"/api/v1/operations/calendar?facilityId={DemoIdentitySeed.FacilityA}&startDate=0001-01-01&endDate=0001-01-02");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.StatusCode);
    }
    [Fact]
    public async Task Facility_creation_is_visible_in_its_facility_audit()
    {
        using var op = await fixture.Login("operator@example.test");
        var created = await op.Send(HttpMethod.Post, "/api/v1/facilities", new { code = "AUDIT-NEW", name = "Synthetic", address = "Synthetic address", permittedGoods = "Dry", published = false, reason = "Create synthetic facility" }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var id = body.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid();
        var audit = await op.Send(HttpMethod.Get, $"/api/v1/audit?facilityId={id}"); Assert.Equal(HttpStatusCode.OK, audit.StatusCode); Assert.Contains("FacilityCreated", await audit.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Self_demoted_active_admin_can_read_only_own_membership_receipt()
    {
        using var admin = await fixture.Login(); using var other = await fixture.Login("admin2.a@example.test"); var key = Guid.NewGuid();
        await using var db = fixture.Db(); var member = await db.Memberships.SingleAsync(x => x.UserId == "admin.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA);
        try {
            Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{member.Id}", new { organizationId = DemoIdentitySeed.OrganizationA, role = "Member", active = true, reason = "Synthetic self demotion" }, Convert.ToBase64String(member.Version), key)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
            await db.Entry(member).ReloadAsync(); member.Active = false; await db.SaveChangesAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        } finally { await db.Entry(member).ReloadAsync(); member.Role = "CustomerAdmin"; member.Active = true; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Customer_rental_lists_are_scoped_and_staff_calendar_separates_schedule_from_physical_custody()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today, today.AddDays(5));
        await using var db = fixture.Db(); var unit = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var lease = await db.Leases.SingleAsync(x => x.Id == unit.LeaseId);
        db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = grant, OrganizationId = lease.OrganizationId, ScheduledEnd = today.AddDays(5), HandedOverAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var manager = await fixture.Login("manager@example.test");
        var own = await customer.Send(HttpMethod.Get, $"/api/v1/bookings?facilityId={DemoIdentitySeed.FacilityA}&organizationId={DemoIdentitySeed.OrganizationA}"); Assert.Equal(HttpStatusCode.OK, own.StatusCode); Assert.Contains(lease.BookingId.ToString(), await own.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/bookings?facilityId={DemoIdentitySeed.FacilityA}&organizationId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Get, $"/api/v1/operations/calendar?facilityId={DemoIdentitySeed.FacilityA}&startDate={today:yyyy-MM-dd}&endDate={today.AddDays(7):yyyy-MM-dd}")).StatusCode);
        var calendar = await manager.Send(HttpMethod.Get, $"/api/v1/operations/calendar?facilityId={DemoIdentitySeed.FacilityA}&startDate={today:yyyy-MM-dd}&endDate={today.AddDays(7):yyyy-MM-dd}"); Assert.Equal(HttpStatusCode.OK, calendar.StatusCode);
        var text = await calendar.Content.ReadAsStringAsync(); Assert.Contains("Confirmed", text); Assert.Contains("PhysicallyOccupied", text); Assert.Contains(DemoIdentitySeed.OrganizationA.ToString(), text);
        var leases = await customer.Send(HttpMethod.Get, $"/api/v1/leases?facilityId={DemoIdentitySeed.FacilityA}&organizationId={DemoIdentitySeed.OrganizationA}"); Assert.Equal(HttpStatusCode.OK, leases.StatusCode); Assert.Contains(lease.Id.ToString(), await leases.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Catalog_has_real_pagination_instead_of_silently_bounding_management_to_first_hundred()
    {
        await using var db = fixture.Db(); var zone = await db.Zones.FirstAsync(x => x.FacilityId == DemoIdentitySeed.FacilityA);
        db.RentableUnits.AddRange(Enumerable.Range(0, 101).Select(i => new RentableUnit { Id = Guid.NewGuid(), FacilityId = DemoIdentitySeed.FacilityA, ZoneId = zone.Id, Code = $"PAGING-{i:000}", AreaM2 = 10, Published = false })); await db.SaveChangesAsync();
        using var manager = await fixture.Login("manager@example.test");
        using var result = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog?unitsPage=2&zonesPage=1")).Content.ReadAsStringAsync());
        Assert.Equal(2, result.RootElement.GetProperty("unitsPage").GetInt32()); Assert.Equal(50, result.RootElement.GetProperty("units").GetArrayLength()); Assert.True(result.RootElement.GetProperty("unitTotal").GetInt32() >= 103);
    }
    [Fact]
    public async Task Required_audit_failure_after_save_rolls_back_business_audit_and_command_receipt()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(SqlConfiguration.Options(fixture.Connection)).AddInterceptors(new RejectRequiredAudit()).Options)); }));
        using var customer = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await customer.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode); await customer.RefreshCsrf(); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.InternalServerError, (await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "ROLLBACK-AUDIT", description = "Must not persist", baseUom = "box", reason = "Required audit fault fixture" }, key: key)).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.Items.AnyAsync(x => x.Sku == "ROLLBACK-AUDIT")); Assert.False(await db.SecurityAudits.AnyAsync(x => x.Reason == "Required audit fault fixture")); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
    }

    [Fact]
    public async Task Facility_audit_history_has_actual_scope_reason_and_correlation_without_cross_scope_read()
    {
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        var made = await manager.Send(HttpMethod.Post, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/zones", new { code = "AUDIT-TEST", reason = "Documented local management action" }, key: Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, made.StatusCode);
        var history = await manager.Send(HttpMethod.Get, $"/api/v1/audit?facilityId={DemoIdentitySeed.FacilityA}"); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var text = await history.Content.ReadAsStringAsync(); Assert.Contains("ZoneCreated", text); Assert.Contains("Documented local management action", text); Assert.Contains("correlationId", text); Assert.Contains(DemoIdentitySeed.FacilityA.ToString(), text);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Get, $"/api/v1/audit?facilityId={DemoIdentitySeed.FacilityA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Send(HttpMethod.Get, $"/api/v1/audit?facilityId={DemoIdentitySeed.FacilityB}")).StatusCode);
    }

    [Fact]
    public async Task Membership_result_readback_stays_with_initiator_current_admin_and_organization()
    {
        using var admin = await fixture.Login(); using var other = await fixture.Login("admin2.a@example.test"); var key = Guid.NewGuid();
        await using var db = fixture.Db(); var member = await db.Memberships.SingleAsync(x => x.UserId == "member.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA);
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{member.Id}", new { organizationId = DemoIdentitySeed.OrganizationA, role = "Member", active = true, reason = "Explicit membership readback test" }, Convert.ToBase64String(member.Version), key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
    }
    private sealed class RejectRequiredAudit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Reason == "Required audit fault fixture")) throw new InvalidOperationException("Synthetic required audit failure before transaction commit.");
            return ValueTask.FromResult(result);
        }
    }
}
