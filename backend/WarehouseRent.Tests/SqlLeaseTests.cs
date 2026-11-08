using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Leases;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlLeaseTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private async Task<BrowserSession> Sales(WebApplicationFactory<Program> host)
    {
        var browser = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await browser.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "operator@example.test", password = DemoIdentitySeed.Password })).StatusCode); await browser.RefreshCsrf(); return browser;
    }
    private async Task<(Guid Booking, string Version, Guid Account)> Confirmed(BrowserSession browser, int offset)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(offset);
        var quoteResponse = await browser.Send(HttpMethod.Post, "/api/v1/quotes", new { facilityId = DemoIdentitySeed.FacilityA, organizationId = DemoIdentitySeed.OrganizationA,
            unitIds = new[] { SqlFacilitiesTests.UnitA, SqlFacilitiesTests.UnitB }, startDate = start, endDate = start.AddDays(2) }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode); using var quote = JsonDocument.Parse(await quoteResponse.Content.ReadAsStringAsync());
        var q = quote.RootElement.GetProperty("result");
        var holdResponse = await browser.Send(HttpMethod.Post, $"/api/v1/quotes/{q.GetProperty("resourceId").GetGuid()}/holds", new { expectedQuoteVersion = q.GetProperty("version").GetString() }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, holdResponse.StatusCode); using var hold = JsonDocument.Parse(await holdResponse.Content.ReadAsStringAsync()); var h = hold.RootElement.GetProperty("result");
        var id = h.GetProperty("resourceId").GetGuid();
        var confirmedResponse = await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{id}/confirm", new { reason = "TEST ONLY deposit seam" }, h.GetProperty("version").GetString(), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode); using var confirmed = JsonDocument.Parse(await confirmedResponse.Content.ReadAsStringAsync());
        return (id, confirmed.RootElement.GetProperty("result").GetProperty("version").GetString()!, h.GetProperty("rentalAccountId").GetGuid());
    }
    [Fact]
    public async Task Conversion_once_preserves_account_monthly_snapshots_and_cancellation_is_shared()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>(); }));
        using var browser = await Sales(host);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var monthEnd = nextMonth.AddMonths(1).AddDays(-1);
        var booking = await Confirmed(browser, monthEnd.DayNumber - today.DayNumber);
        var converted = await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Synthetic lease conversion" }, booking.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, converted.StatusCode); using var first = JsonDocument.Parse(await converted.Content.ReadAsStringAsync()); var leaseId = first.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid();
        var repeated = await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Repeated conversion" }, booking.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); using var repeat = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync()); Assert.Equal(leaseId, repeat.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid());
        var read = await browser.Send(HttpMethod.Get, $"/api/v1/leases/{leaseId}"); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var data = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(booking.Account, data.RootElement.GetProperty("rentalAccountId").GetGuid()); Assert.Equal("Scheduled", data.RootElement.GetProperty("state").GetString());
        await using (var db = fixture.Db())
        {
            var quoteId = await db.Bookings.Where(x => x.Id == booking.Booking).Select(x => x.QuoteId).SingleAsync();
            var originals = await db.QuoteLines.AsNoTracking().Where(x => x.QuoteId == quoteId).ToListAsync();
            var snapshots = data.RootElement.GetProperty("lines").EnumerateArray().ToArray();
            Assert.Equal(4, originals.Count); // Two units, each spanning two calendar months.
            Assert.Equal(originals.Count, snapshots.Length);
            foreach (var original in originals)
            {
                var copy = Assert.Single(snapshots, x => x.GetProperty("sourceQuoteLineId").GetGuid() == original.Id);
                Assert.Equal(original.StartDate.ToString("yyyy-MM-dd"), copy.GetProperty("startDate").GetString());
                Assert.Equal(original.EndDate.ToString("yyyy-MM-dd"), copy.GetProperty("endDate").GetString());
                Assert.Equal(original.Days, copy.GetProperty("days").GetInt32());
                Assert.Equal(original.AreaM2, decimal.Parse(copy.GetProperty("areaM2").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(original.DailyRate, decimal.Parse(copy.GetProperty("dailyRate").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
                Assert.Equal(original.Amount, decimal.Parse(copy.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            }
            Assert.Equal(await db.Quotes.Where(x => x.Id == quoteId).Select(x => x.RentalSubtotal).SingleAsync(),
                snapshots.Sum(x => decimal.Parse(x.GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        }
        using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/leases/{leaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Foreign customer conversion" }, booking.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, $"/api/v1/leases/{leaseId}/handover", new { reason = "Foreign customer handover", inspectionConfirmed = true }, data.RootElement.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
        var cancellation = await browser.Send(HttpMethod.Post, $"/api/v1/leases/{leaseId}/cancel", new { reason = "Synthetic scheduled cancellation" }, booking.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, cancellation.StatusCode);
        Assert.Contains("Cancelled", await (await browser.Send(HttpMethod.Get, $"/api/v1/leases/{leaseId}")).Content.ReadAsStringAsync());
        Assert.Contains("Cancelled", await (await browser.Send(HttpMethod.Get, $"/api/v1/bookings/{booking.Booking}")).Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Unresolved_occupancy_blocks_all_handover_then_manager_clearance_with_inventory_evidence_allows_reuse()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>(); services.RemoveAll<IInventoryClearance>(); services.AddScoped<IInventoryClearance, UnavailableInventoryClearance>(); }));
        using var browser = await Sales(host); var booking = await Confirmed(browser, 0);
        var response = await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Synthetic current lease" }, booking.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var converted = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var item = converted.RootElement.GetProperty("result");
        var id = item.GetProperty("resourceId").GetGuid(); var version = item.GetProperty("version").GetString()!;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var previousUnit = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today.AddDays(-10), today.AddDays(-5));
        await using var db = fixture.Db(); var prior = await db.LeaseUnits.SingleAsync(x => x.Id == previousUnit);
        db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = SqlFacilitiesTests.UnitA, LeaseUnitId = previousUnit,
            OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = today.AddDays(-5), HandedOverAt = DateTimeOffset.UtcNow.AddDays(-10) }); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(HttpMethod.Post, $"/api/v1/leases/{id}/handover", new { reason = "Blocked handover", inspectionConfirmed = true }, version, Guid.NewGuid())).StatusCode);
        Assert.False(await db.UnitOccupancies.AnyAsync(x => db.LeaseUnits.Any(l => l.Id == x.LeaseUnitId && l.LeaseId == id)));
        var conflicts = await (await browser.Send(HttpMethod.Get, $"/api/v1/operations/conflicts?facilityId={DemoIdentitySeed.FacilityA}")).Content.ReadAsStringAsync();
        Assert.Contains(SqlFacilitiesTests.UnitA.ToString(), conflicts); Assert.Contains(booking.Booking.ToString(), conflicts);
        var previousLease = await db.Leases.SingleAsync(x => x.Id == prior.LeaseId);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(HttpMethod.Post, $"/api/v1/leases/{prior.LeaseId}/physical-clearance", new { reason = "No inventory module evidence", inspectionConfirmed = true }, Convert.ToBase64String(previousLease.Version), Guid.NewGuid())).StatusCode);
        await using var inventory = host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IInventoryClearance>(); services.AddScoped<IInventoryClearance, SyntheticZeroInventory>(); }));
        using var manager = await Sales(inventory);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{prior.LeaseId}/physical-clearance", new { reason = "Inspection still required", inspectionConfirmed = false }, Convert.ToBase64String(previousLease.Version), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{prior.LeaseId}/physical-clearance", new { reason = "TEST ONLY zero stock, no pending requests, inspection", inspectionConfirmed = true }, Convert.ToBase64String(previousLease.Version), Guid.NewGuid())).StatusCode);
        var handed = await browser.Send(HttpMethod.Post, $"/api/v1/leases/{id}/handover", new { reason = "Synthetic verified handover", inspectionConfirmed = true }, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, handed.StatusCode);
        Assert.Equal(2, await db.UnitOccupancies.CountAsync(x => db.LeaseUnits.Any(l => l.Id == x.LeaseUnitId && l.LeaseId == id) && x.ClearedAt == null));
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/cancel", new { reason = "Cannot cancel activated lease" }, booking.Version, Guid.NewGuid())).StatusCode);
        Assert.Contains("Active", await (await browser.Send(HttpMethod.Get, $"/api/v1/leases/{id}")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Concurrent_conversion_returns_one_lease_to_both_commands_under_booking_locks()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>(); }));
        using var first = await Sales(host); using var second = await Sales(host); var booking = await Confirmed(first, 100);
        var results = await Task.WhenAll(first.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Synthetic concurrent conversion one" }, booking.Version, Guid.NewGuid()),
            second.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Synthetic concurrent conversion two" }, booking.Version, Guid.NewGuid()));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        using var one = JsonDocument.Parse(await results[0].Content.ReadAsStringAsync()); using var two = JsonDocument.Parse(await results[1].Content.ReadAsStringAsync());
        Assert.Equal(one.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid(), two.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid());
        await using var db = fixture.Db(); Assert.Equal(1, await db.Leases.CountAsync(x => x.BookingId == booking.Booking));
        Assert.Equal(1, await db.RentalAccounts.CountAsync(x => x.BookingId == booking.Booking));
    }

    [Fact]
    public async Task Future_lease_cannot_be_handed_over_before_start_date()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>(); }));
        using var browser = await Sales(host); var booking = await Confirmed(browser, 50);
        var response = await browser.Send(HttpMethod.Post, $"/api/v1/bookings/{booking.Booking}/lease", new { reason = "Synthetic future lease" }, booking.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var result = created.RootElement.GetProperty("result");
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(HttpMethod.Post, $"/api/v1/leases/{result.GetProperty("resourceId").GetGuid()}/handover", new { reason = "Too early", inspectionConfirmed = true }, result.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
    }
}
// Test-only evidence seam; actual locked balances/pending requests integrate in WP08.
public sealed class SyntheticZeroInventory : IInventoryClearance
{
    public Task<ClearanceEvidence> ReadAsync(Guid leaseId, Guid[] leaseUnitIds, CancellationToken ct) => Task.FromResult(new ClearanceEvidence(true, true, true));
}
