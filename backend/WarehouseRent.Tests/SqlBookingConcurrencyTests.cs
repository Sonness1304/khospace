using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication.Cookies;
using WarehouseRent.Api.Features.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlBookingConcurrencyTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private async Task<(Guid Id, string Version)> Quote(BrowserSession browser, Guid[] units, int offset)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(offset);
        var response = await browser.Send(HttpMethod.Post, "/api/v1/quotes", new { facilityId = DemoIdentitySeed.FacilityA, organizationId = DemoIdentitySeed.OrganizationA,
            unitIds = units, startDate = start, endDate = start.AddDays(2) }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (result.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid(), result.RootElement.GetProperty("result").GetProperty("version").GetString()!);
    }
    private static async Task<(Guid Id, Guid Account, string Version)> HoldIdentity(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var data = json.RootElement.GetProperty("result");
        return (data.GetProperty("resourceId").GetGuid(), data.GetProperty("rentalAccountId").GetGuid(), data.GetProperty("version").GetString()!);
    }
    [Fact]
    public async Task Opposite_order_multiunit_holds_have_one_complete_winner_and_stable_account()
    {
        using var first = await fixture.Login(); using var second = await fixture.Login("admin2.a@example.test");
        var one = await Quote(first, [SqlFacilitiesTests.UnitA, SqlFacilitiesTests.UnitB], 30);
        var two = await Quote(second, [SqlFacilitiesTests.UnitB, SqlFacilitiesTests.UnitA], 30);
        var responses = await Task.WhenAll(first.Send(HttpMethod.Post, $"/api/v1/quotes/{one.Id}/holds", new { expectedQuoteVersion = one.Version }, key: Guid.NewGuid()),
            second.Send(HttpMethod.Post, $"/api/v1/quotes/{two.Id}/holds", new { expectedQuoteVersion = two.Version }, key: Guid.NewGuid()));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(30);
        await using var db = fixture.Db(); Assert.Equal(2, await db.SpaceAllocations.CountAsync(x => x.State == "Held" && x.StartDate == start));
        var winner = await HoldIdentity(responses.Single(x => x.StatusCode == HttpStatusCode.OK));
        using var operatorUser = await fixture.Login("operator@example.test");
        var status = await operatorUser.Send(HttpMethod.Get, $"/api/v1/bookings/{winner.Id}");
        Assert.Contains(winner.Account.ToString(), await status.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Same_key_returns_saved_hold_changed_payload_conflicts_and_customer_cannot_confirm()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitA], 60); var key = Guid.NewGuid();
        var body = new { expectedQuoteVersion = quote.Version };
        var response = await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", body, key: key); var hold = await HoldIdentity(response);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", body, key: key)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = "altered" }, key: key)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "Forged confirmation" }, hold.Version, Guid.NewGuid())).StatusCode);
        using var sales = await fixture.Login("sales@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "No verified deposit" }, hold.Version, Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Cancel_releases_capacity_but_keeps_rental_account_and_foreign_customer_is_denied()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitB], 90);
        var hold = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/cancel", new { reason = "Synthetic cancellation" }, hold.Version, Guid.NewGuid())).StatusCode);
        var read = await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}"); var text = await read.Content.ReadAsStringAsync();
        Assert.Contains("Cancelled", text); Assert.Contains(hold.Account.ToString(), text);
        await using var db = fixture.Db(); Assert.False(await db.SpaceAllocations.AnyAsync(x => x.UnitId == SqlFacilitiesTests.UnitB && x.State == "Held"));
    }
    [Fact]
    public async Task Logical_expiry_needs_no_cleanup_and_late_verified_seam_cannot_revive_hold()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitA], 150);
        var held = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        await using var db = fixture.Db(); var item = await db.Bookings.SingleAsync(x => x.Id == held.Id);
        item.HeldAt = DateTimeOffset.UtcNow.AddMinutes(-20); item.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        foreach (var allocation in await db.SpaceAllocations.Where(x => db.BookingLines.Any(l => l.Id == x.BookingLineId && l.BookingId == held.Id)).ToListAsync()) allocation.HoldExpiresAt = item.ExpiresAt;
        await db.SaveChangesAsync();
        Assert.Contains("Expired", await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{held.Id}")).Content.ReadAsStringAsync());
        await using var seam = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>();
        }));
        using var sales = new BrowserSession(seam.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await sales.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await sales.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "sales@example.test", password = DemoIdentitySeed.Password })).StatusCode); await sales.RefreshCsrf();
        var confirmation = await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{held.Id}/confirm", new { reason = "Synthetic late funding" }, Convert.ToBase64String(item.Version), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, confirmation.StatusCode); Assert.Contains("HOLD_NOT_LIVE", await confirmation.Content.ReadAsStringAsync());
        var replacement = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        Assert.NotEqual(held.Id, replacement.Id); Assert.NotEqual(held.Account, replacement.Account);
        Assert.True(await db.RentalAccounts.AnyAsync(x => x.Id == held.Account));
    }

    [Fact]
    public async Task Explicit_test_only_verified_seam_allows_one_confirmation_and_funded_cancellation_keeps_account()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitB], 180);
        var held = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        await using var seam = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<IDepositVerification>(); services.AddScoped<IDepositVerification, SyntheticVerifiedDeposit>();
        }));
        using var sales = new BrowserSession(seam.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await sales.RefreshCsrf(); await sales.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "sales@example.test", password = DemoIdentitySeed.Password }); await sales.RefreshCsrf();
        var first = await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{held.Id}/confirm", new { reason = "TEST ONLY verified seam" }, held.Version, Guid.NewGuid());
        var confirmed = await HoldIdentity(first);
        var duplicate = await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{held.Id}/confirm", new { reason = "TEST ONLY repeated confirmation" }, held.Version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Equal(held.Id, (await HoldIdentity(duplicate)).Id);
        Assert.Equal(HttpStatusCode.OK, (await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{held.Id}/cancel", new { reason = "TEST ONLY funded cancellation" }, confirmed.Version, Guid.NewGuid())).StatusCode);
        await using var db = fixture.Db(); var account = await db.RentalAccounts.SingleAsync(x => x.Id == held.Account);
        Assert.True(account.RefundNeeded); Assert.Equal("Cancelled", account.State);
        Assert.Equal(1, await db.SecurityAudits.CountAsync(x => x.AggregateId == held.Id && x.Action == "BookingConfirmed"));
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(-10, true)]
    public async Task Booking_status_uses_SQL_expiry_time_even_when_API_host_clock_is_skewed(int skewDays, bool expired)
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitA], expired ? 285 : 280);
        var hold = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        if (expired)
        {
            await using var db = fixture.Db(); var item = await db.Bookings.SingleAsync(x => x.Id == hold.Id);
            item.HeldAt = DateTimeOffset.UtcNow.AddMinutes(-20); item.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5); await db.SaveChangesAsync();
        }
        await using var skewed = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new BookingSkewClock(skewDays));
            services.PostConfigure<CookieAuthenticationOptions>(IdentityRegistration.Scheme, options => options.TimeProvider = TimeProvider.System);
        }));
        using var browser = new BrowserSession(skewed.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        foreach (var cookie in customer.Cookies) browser.Cookies[cookie.Key] = cookie.Value;
        var response = await browser.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(expired ? "Expired" : "Held", status.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Future_sales_revision_does_not_invalidate_a_quote_using_current_sellable_rate()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitA], 210);
        using var manager = await fixture.Login("operator@example.test");
        using var catalog = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog")).Content.ReadAsStringAsync());
        var version = catalog.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == SqlFacilitiesTests.UnitA).GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{SqlFacilitiesTests.UnitA}/rates", new { dailyRate = "250", salesValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(5), salesValidTo = (DateOnly?)null, reason = "Synthetic future only price" }, version, Guid.NewGuid())).StatusCode);
        var hold = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        using var status = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).Content.ReadAsStringAsync());
        Assert.Equal(TimeSpan.FromMinutes(15), status.RootElement.GetProperty("expiresAt").GetDateTimeOffset() - status.RootElement.GetProperty("heldAt").GetDateTimeOffset());
        Assert.Equal("424", status.RootElement.GetProperty("requiredDeposit").GetString());
    }

    [Fact]
    public async Task Cancel_receipt_save_fault_rolls_back_booking_account_and_allocations()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitB], 250);
        var hold = await HoldIdentity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<WarehouseRent.Infrastructure.Persistence.AppDbContext>();
            services.AddScoped(_ => new WarehouseRent.Infrastructure.Persistence.AppDbContext(new DbContextOptionsBuilder<WarehouseRent.Infrastructure.Persistence.AppDbContext>(fixture.Options())
                .AddInterceptors(new RejectCatalogReceipt()).Options));
        }));
        using var fault = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        foreach(var cookie in customer.Cookies) fault.Cookies[cookie.Key] = cookie.Value;
        await fault.RefreshCsrf(); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.InternalServerError, (await fault.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/cancel", new { reason = "Synthetic cancel receipt failure" }, hold.Version, key)).StatusCode);
        await using var db = fixture.Db();
        Assert.Equal("Held", (await db.Bookings.SingleAsync(x => x.Id == hold.Id)).State);
        Assert.Equal("Open", (await db.RentalAccounts.SingleAsync(x => x.Id == hold.Account)).State);
        Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.False(await db.SecurityAudits.AnyAsync(x => x.AggregateId == hold.Id && x.Action == "RentalCancelled"));
        Assert.Equal(1, await db.SpaceAllocations.CountAsync(x => db.BookingLines.Any(l => l.Id == x.BookingLineId && l.BookingId == hold.Id) && x.State == "Held"));
    }

    [Fact]
    public async Task Rate_drift_requires_explicit_requote_but_future_sales_revision_does_not_reprice_current_tariff()
    {
        using var customer = await fixture.Login(); var quote = await Quote(customer, [SqlFacilitiesTests.UnitB], 120);
        using var manager = await fixture.Login("operator@example.test");
        using var catalog = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog")).Content.ReadAsStringAsync());
        var version = catalog.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == SqlFacilitiesTests.UnitB).GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{SqlFacilitiesTests.UnitB}/rates", new { dailyRate = "6000", salesValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), salesValidTo = (DateOnly?)null, reason = "Synthetic drift" }, version, Guid.NewGuid())).StatusCode);
        var conflict = await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); Assert.Contains("REQUOTE_REQUIRED", await conflict.Content.ReadAsStringAsync());
    }
}

// Explicit test fixture only; never registered by Program, no HTTP route fabricates payment.
public sealed class SyntheticVerifiedDeposit : IDepositVerification
{
    public Task<DepositEvidence> ReadAsync(WarehouseRent.Infrastructure.Rentals.RentalAccount account, CancellationToken ct)
        => Task.FromResult(new DepositEvidence(true, account.RequiredDeposit));
}
public sealed class BookingSkewClock(int days) : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(days); }
