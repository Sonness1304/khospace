using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Stock;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlLifecycleTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
    private async Task<(Guid Id, Guid Zone, string Version)> Unit()
    {
        await using var db = fixture.Db(); var source = await db.RentableUnits.SingleAsync(x => x.Id == SqlFacilitiesTests.UnitA);
        var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = source.FacilityId, ZoneId = source.ZoneId, Code = "L-" + Guid.NewGuid().ToString("N"), AreaM2 = 10, Published = true };
        db.RentableUnits.Add(unit); db.RateVersions.Add(new RateVersion { Id = Guid.NewGuid(), UnitId = unit.Id, SalesValidFrom = Today.AddDays(-1), DailyRate = 100 }); await db.SaveChangesAsync();
        return (unit.Id, unit.ZoneId, Convert.ToBase64String(unit.Version));
    }
    private async Task<(Guid Unit, Guid Grant, Guid Lease, string Version)> Active()
    {
        var u = await Unit(); var grant = await fixture.SeedOccupancyOwner(u.Id, Today.AddDays(-2), Today.AddDays(20));
        await using var db = fixture.Db(); var lu = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var lease = await db.Leases.SingleAsync(x => x.Id == lu.LeaseId);
        db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = u.Id, LeaseUnitId = grant, OrganizationId = lease.OrganizationId, ScheduledEnd = lease.EndDate, HandedOverAt = DateTimeOffset.UtcNow.AddDays(-2) }); await db.SaveChangesAsync();
        return (u.Id, grant, lease.Id, Convert.ToBase64String(lease.Version));
    }
    private static object TerminateBody => new { reason = "Customer ends use; retain physical custody", terminationConfirmed = true };
    private async Task<(Guid Id, string Version, object Post)> ApprovedRequest((Guid Unit, Guid Grant, Guid Lease, string Version) s, Guid item, string type, string quantity)
    {
        using var customer = await fixture.Login(); using var clerk = await fixture.Login("clerk@example.test");
        var draft = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = s.Lease, leaseUnitId = s.Grant, type, lines = new[] { new { itemId = item, quantity } }, reason = "Customer instruction" }, key: Guid.NewGuid()))).GetProperty("result");
        var id = draft.GetProperty("resourceId").GetGuid(); var path = $"/api/v1/stock-requests/{id}";
        var submitted = (await Ok(await customer.Send(HttpMethod.Post, path + "/submit", new { reason = "Customer submits" }, draft.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result");
        var approved = (await Ok(await clerk.Send(HttpMethod.Post, path + "/approve", new { reason = "Review within term" }, submitted.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result");
        var read = await Ok(await customer.Send(HttpMethod.Get, path));
        return (id, approved.GetProperty("version").GetString()!, new { reason = "Observed full quantity", lines = new[] { new { requestLineId = read.GetProperty("lines")[0].GetProperty("id").GetGuid(), quantity } } });
    }
    [Fact]
    public async Task Termination_blocks_inbound_and_requires_new_manager_release_before_outbound_then_clearance()
    {
        var s = await Active(); using var manager = await fixture.Login("manager@example.test"); using var clerk = await fixture.Login("clerk@example.test"); using var customer = await fixture.Login();
        var item = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = Guid.NewGuid().ToString(), description = "Termination goods", baseUom = "box", reason = "Synthetic test" }, key: Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        var receipt = await ApprovedRequest(s, item, "Receipt", "10"); await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{receipt.Id}/post", receipt.Post, receipt.Version, Guid.NewGuid()));
        var issue = await ApprovedRequest(s, item, "Issue", "10"); var inbound = await ApprovedRequest(s, item, "Receipt", "2");
        var terminated = (await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/terminate", TerminateBody, s.Version, Guid.NewGuid()))).GetProperty("result");
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{inbound.Id}/post", inbound.Post, inbound.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/post", issue.Post, issue.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/approve", new { reason = "Clerk lacks release authority" }, issue.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = s.Lease, leaseUnitId = s.Grant, type = "Receipt", lines = new[] { new { itemId = item, quantity = "1" } }, reason = "Blocked new inbound" }, key: Guid.NewGuid())).StatusCode);
        var clear = new { reason = "Inspected", inspectionConfirmed = true }; var version = terminated.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", clear, version, Guid.NewGuid())).StatusCode);
        var authorized = (await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/approve", new { reason = "Authorize release after termination" }, issue.Version, Guid.NewGuid()))).GetProperty("result");
        await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/post", issue.Post, authorized.GetProperty("version").GetString(), Guid.NewGuid()));
        // Zero stock alone is insufficient while an inbound request remains pending.
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", clear, version, Guid.NewGuid())).StatusCode);
        await Ok(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{inbound.Id}/cancel", new { reason = "Cancel unusable inbound" }, inbound.Version, Guid.NewGuid()));
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", clear, version, Guid.NewGuid()));
        await using var db = fixture.Db(); Assert.Equal(0, (await db.StockBalances.SingleAsync(x => x.LeaseUnitId == s.Grant)).Quantity); Assert.NotNull((await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Grant)).ClearedAt);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lifecycle_audit_failure_rolls_back_mutation_and_command_receipt(bool retirement)
    {
        var s = await Active(); var u = await Unit(); var key = Guid.NewGuid();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectLifecycleAudit()).Options)); }));
        using var manager = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await manager.RefreshCsrf();
        await Ok(await manager.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "manager@example.test", password = DemoIdentitySeed.Password })); await manager.RefreshCsrf();
        var result = retirement ? await manager.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/retire", new { reason = "Faulted retirement", retirementConfirmed = true }, u.Version, key)
            : await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/terminate", TerminateBody, s.Version, key);
        Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.Null((await db.RentableUnits.SingleAsync(x => x.Id == u.Id)).RetiredAt); Assert.True((await db.RentableUnits.SingleAsync(x => x.Id == u.Id)).Published);
        Assert.Null((await db.Leases.SingleAsync(x => x.Id == s.Lease)).TerminatedAt); Assert.Equal(Today.AddDays(20), (await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Grant)).ScheduledEnd);
        var allocationId = await db.LeaseUnits.Where(x => x.Id == s.Grant).Select(x => x.InitialAllocationId).SingleAsync(); Assert.Equal(Today.AddDays(20), (await db.SpaceAllocations.SingleAsync(x => x.Id == allocationId)).EndDate);
    }
    private sealed class RejectLifecycleAudit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action is "LeaseTerminated" or "UnitRetired")) throw new InvalidOperationException("Synthetic lifecycle audit fault"); return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Termination_preserves_history_deposit_and_other_bookings_until_inspected_clearance()
    {
        var s = await Active(); using var manager = await fixture.Login("manager@example.test");
        var futureLine = await fixture.SeedAllocationOwner(s.Unit, Today.AddDays(20), Today.AddDays(22));
        await using (var db = fixture.Db()) { db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = s.Unit, BookingLineId = futureLine, State = "Confirmed", StartDate = Today.AddDays(20), EndDate = Today.AddDays(22) }); await db.SaveChangesAsync(); }
        var key = Guid.NewGuid(); var path = $"/api/v1/leases/{s.Lease}/terminate";
        var first = await Ok(await manager.Send(HttpMethod.Post, path, TerminateBody, s.Version, key));
        Assert.Equal("ClearancePending", first.GetProperty("result").GetProperty("state").GetString());
        Assert.Equal(first.GetRawText(), (await Ok(await manager.Send(HttpMethod.Post, path, TerminateBody, s.Version, key))).GetRawText());
        await using (var db = fixture.Db()) {
            var lease = await db.Leases.SingleAsync(x => x.Id == s.Lease); Assert.Equal(Today.AddDays(20), lease.EndDate);
            Assert.Equal(20m, (await db.RentalAccounts.SingleAsync(x => x.Id == lease.RentalAccountId)).RequiredDeposit);
            Assert.Equal(Today.AddDays(20), (await db.Bookings.SingleAsync(x => x.Id == lease.BookingId)).EndDate);
            var occupancy = await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Grant); Assert.Null(occupancy.ClearedAt); Assert.Equal(Today, occupancy.ScheduledEnd);
            Assert.Equal(Today, (await db.SpaceAllocations.SingleAsync(x => x.Id == (db.LeaseUnits.Single(y => y.Id == s.Grant).InitialAllocationId))).EndDate);
            Assert.Equal("Confirmed", (await db.SpaceAllocations.SingleAsync(x => x.BookingLineId == futureLine)).State);
            Assert.Single(await db.SecurityAudits.Where(x => x.Action == "LeaseTerminated" && x.AggregateId == s.Lease).ToListAsync());
        }
        var read = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/leases/{s.Lease}")); Assert.NotEqual(JsonValueKind.Null, read.GetProperty("terminatedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", new { reason = "Inspected empty warehouse", inspectionConfirmed = true }, read.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
        await using var check = fixture.Db(); Assert.Equal("SettlementPending", (await check.Leases.SingleAsync(x => x.Id == s.Lease)).State);
    }
    [Fact]
    public async Task Termination_enforces_scope_confirmation_versions_and_active_state()
    {
        var s = await Active(); using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var manager = await fixture.Login("manager@example.test"); var path = $"/api/v1/leases/{s.Lease}/terminate";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, path, TerminateBody, s.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, path, TerminateBody, s.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.Send(HttpMethod.Post, path, new { reason = "Not confirmed", terminationConfirmed = false }, s.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await manager.Send(HttpMethod.Post, path, TerminateBody, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await manager.Send(HttpMethod.Post, path, TerminateBody, "stale", Guid.NewGuid())).StatusCode);
        await using (var db = fixture.Db()) { var lease = await db.Leases.SingleAsync(x => x.Id == s.Lease); lease.State = "Scheduled"; await db.SaveChangesAsync(); s.Version = Convert.ToBase64String(lease.Version); }
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, path, TerminateBody, s.Version, Guid.NewGuid())).StatusCode);
    }
    [Theory]
    [InlineData("Allocation")]
    [InlineData("Maintenance")]
    [InlineData("Hold")]
    [InlineData("Occupancy")]
    [InlineData("Stock")]
    [InlineData("Request")]
    public async Task Retirement_rejects_each_independent_operational_commitment(string kind)
    {
        var u = await Unit(); var grant = await fixture.SeedOccupancyOwner(u.Id, Today.AddDays(-5), Today.AddDays(-1));
        await using (var db = fixture.Db()) {
            var lu = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var allocation = await db.SpaceAllocations.SingleAsync(x => x.Id == lu.InitialAllocationId);
            if (kind is "Allocation" or "Maintenance" or "Hold") { allocation.EndDate = Today.AddDays(4); allocation.State = kind == "Hold" ? "Held" : kind == "Maintenance" ? "Maintenance" : "Confirmed"; allocation.HoldExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5); }
            if (kind == "Occupancy") db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = u.Id, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = Today.AddDays(-1), HandedOverAt = DateTimeOffset.UtcNow });
            if (kind == "Stock") { var item = new CustomerItem { Id = Guid.NewGuid(), OrganizationId = DemoIdentitySeed.OrganizationA, Sku = Guid.NewGuid().ToString(), Description = "Synthetic stranded stock", BaseUom = "box" }; db.Items.Add(item); db.StockBalances.Add(new StockBalance { OrganizationId = item.OrganizationId, ItemId = item.Id, LeaseUnitId = grant, Quantity = 1 }); }
            if (kind == "Request") db.StockRequests.Add(new StockRequest { Id = Guid.NewGuid(), OrganizationId = DemoIdentitySeed.OrganizationA, FacilityId = DemoIdentitySeed.FacilityA, LeaseId = lu.LeaseId, LeaseUnitId = grant, Type = "Issue", CreatedBy = "synthetic", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        using var manager = await fixture.Login("manager@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/retire", new { reason = "Cannot discard commitments", retirementConfirmed = true }, u.Version, Guid.NewGuid())).StatusCode);
        await using var check = fixture.Db(); Assert.True((await check.RentableUnits.SingleAsync(x => x.Id == u.Id)).Published);
    }
    [Fact]
    public async Task Retirement_is_versioned_permanent_scoped_and_replayed_without_reactivation()
    {
        var u = await Unit(); using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login(); var path = $"/api/v1/units/{u.Id}/retire"; var body = new { reason = "Permanently withdraw empty unit", retirementConfirmed = true };
        Assert.Equal(HttpStatusCode.NotFound, (await customer.Send(HttpMethod.Post, path, body, u.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await manager.Send(HttpMethod.Post, path, body, "stale", Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.Send(HttpMethod.Post, path, new { reason = "No confirmation" }, u.Version, Guid.NewGuid())).StatusCode);
        var key = Guid.NewGuid(); var saved = await Ok(await manager.Send(HttpMethod.Post, path, body, u.Version, key));
        Assert.Equal(saved.GetRawText(), (await Ok(await manager.Send(HttpMethod.Post, path, body, u.Version, key))).GetRawText());
        var version = saved.GetProperty("result").GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Patch, $"/api/v1/units/{u.Id}", new { zoneId = u.Zone, code = "Reactivated", areaM2 = "10", published = true, reason = "Forbidden reuse" }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/rates", new { dailyRate = "200", salesValidFrom = Today, reason = "Forbidden new rate" }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", new { facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { u.Id }, startDate = Today, endDate = Today.AddDays(1), reason = "Forbidden maintenance" }, key: Guid.NewGuid())).StatusCode);
        var catalog = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog?search=L-")); Assert.Contains(catalog.GetProperty("units").EnumerateArray(), x => x.GetProperty("id").GetGuid() == u.Id && x.GetProperty("retiredAt").ValueKind != JsonValueKind.Null);
        await using var db = fixture.Db(); Assert.False((await db.RentableUnits.SingleAsync(x => x.Id == u.Id)).Published); Assert.Single(await db.SecurityAudits.Where(x => x.Action == "UnitRetired" && x.AggregateId == u.Id).ToListAsync());
    }
    [Fact]
    public async Task Retirement_and_hold_serialize_and_cannot_both_commit()
    {
        var u = await Unit(); using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        var q = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = DemoIdentitySeed.OrganizationA, facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { u.Id }, startDate = Today.AddDays(2), endDate = Today.AddDays(3) }, key: Guid.NewGuid()))).GetProperty("result");
        var outcomes = await Task.WhenAll(manager.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/retire", new { reason = "Concurrent retirement", retirementConfirmed = true }, u.Version, Guid.NewGuid()), customer.Send(HttpMethod.Post, $"/api/v1/quotes/{q.GetProperty("resourceId").GetGuid()}/holds", new { expectedQuoteVersion = q.GetProperty("version").GetString() }, key: Guid.NewGuid()));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Retirement_and_maintenance_cannot_both_commit()
    {
        var u = await Unit(); using var one = await fixture.Login("manager@example.test"); using var two = await fixture.Login("manager@example.test");
        var outcomes = await Task.WhenAll(one.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/retire", new { reason = "Withdraw unit", retirementConfirmed = true }, u.Version, Guid.NewGuid()),
            two.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", new { facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { u.Id }, startDate = Today, endDate = Today.AddDays(2), reason = "Concurrent maintenance" }, key: Guid.NewGuid()));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Expired_hold_and_past_allocations_do_not_prevent_retirement_but_quotes_cannot_reuse_it()
    {
        var u = await Unit(); var grant = await fixture.SeedOccupancyOwner(u.Id, Today.AddDays(-5), Today.AddDays(-1));
        var line = await fixture.SeedAllocationOwner(u.Id, Today.AddDays(2), Today.AddDays(3));
        await using (var db = fixture.Db()) { db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = u.Id, BookingLineId = line, StartDate = Today.AddDays(2), EndDate = Today.AddDays(3), State = "Held", HoldExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }); await db.SaveChangesAsync(); }
        using var manager = await fixture.Login("manager@example.test"); using var customer = await fixture.Login();
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/units/{u.Id}/retire", new { reason = "Only historical commitments remain", retirementConfirmed = true }, u.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = DemoIdentitySeed.OrganizationA, facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { u.Id }, startDate = Today.AddDays(3), endDate = Today.AddDays(4) }, key: Guid.NewGuid())).StatusCode);
        var publicUnits = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/public/facilities/{DemoIdentitySeed.FacilityA}/units?pageSize=100")); Assert.DoesNotContain(publicUnits.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == u.Id);
        await using var check = fixture.Db(); Assert.True(await check.LeaseUnits.AnyAsync(x => x.Id == grant)); Assert.True(await check.SpaceAllocations.AnyAsync(x => x.BookingLineId == line));
    }
}
