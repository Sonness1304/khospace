using System.Net;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlFinanceTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<(Guid Id, string Version, JsonElement Data)> Identity(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var result = data.RootElement.GetProperty("result");
        return (result.GetProperty("resourceId").GetGuid(), result.TryGetProperty("version", out var version) ? version.GetString()! : "", result.Clone());
    }
    private async Task<(Guid Id, Guid Account, string Version, string Deposit, DateOnly Start, DateOnly End)> Hold(BrowserSession customer, int offset, DateOnly? ending = null, Guid? unit = null)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(offset); var end = ending ?? start.AddDays(2);
        var quote = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = DemoIdentitySeed.OrganizationA, facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { unit ?? SqlFacilitiesTests.UnitA }, startDate = start, endDate = end }, key: Guid.NewGuid()));
        var hold = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        using var read = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).Content.ReadAsStringAsync());
        return (hold.Id, hold.Data.GetProperty("rentalAccountId").GetGuid(), hold.Version, read.RootElement.GetProperty("requiredDeposit").GetString()!, start, end);
    }
    private async Task<(Guid Id, string Version, JsonElement Data)> Receipt(BrowserSession finance, Guid account, string amount)
        => await Identity(await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = account, amount, reference = "SYNTHETIC-" + Guid.NewGuid().ToString("N"), method = "BankTransfer", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "TEST ONLY manual ledger match", reason = "Record synthetic received funds" }, key: Guid.NewGuid()));
    [Fact]
    public async Task Real_verified_deposit_confirms_and_monthly_statements_reuse_quote_lines_without_deposit_revenue()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); using var sales = await fixture.Login("sales@example.test"); using var manager = await fixture.Login("manager@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var end = new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(1); var hold = await Hold(customer, 0, end);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "No verified allocation yet" }, hold.Version, Guid.NewGuid())).StatusCode);
        var receipt = await Receipt(finance, hold.Account, hold.Deposit);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = hold.Account, amount = hold.Deposit, reason = "Unverified money cannot allocate" }, receipt.Version, Guid.NewGuid())).StatusCode);
        var verified = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Finance explicitly checked synthetic evidence" }, receipt.Version, Guid.NewGuid()));
        await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = hold.Account, amount = hold.Deposit, reason = "Refundable security allocation" }, verified.Version, Guid.NewGuid()));
        var confirmed = await Identity(await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "Deposit verified while hold live" }, hold.Version, Guid.NewGuid()));
        var converted = await Identity(await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/lease", new { reason = "One rental account" }, confirmed.Version, Guid.NewGuid()));
        var period = new DateOnly(today.Year, today.Month, 1);
        object Statement(DateOnly month) => new { periodStart = month, dueDate = month, reason = "Issue agreed scheduled month" };
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/statements", Statement(period), key: Guid.NewGuid())).StatusCode);
        await Identity(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/handover", new { reason = "Synthetic inspected handover", inspectionConfirmed = true }, converted.Version, Guid.NewGuid()));
        var first = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/statements", Statement(period), key: Guid.NewGuid()));
        var repeated = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/statements", Statement(period), key: Guid.NewGuid())); Assert.Equal(first.Id, repeated.Id);
        var second = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/statements", Statement(period.AddMonths(1)), key: Guid.NewGuid()));
        using var a = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/statements/{first.Id}")).Content.ReadAsStringAsync()); using var b = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/statements/{second.Id}")).Content.ReadAsStringAsync());
        await using var db = fixture.Db(); var quoteId = await db.Bookings.Where(x => x.Id == hold.Id).Select(x => x.QuoteId).SingleAsync(); var expected = await db.Quotes.Where(x => x.Id == quoteId).Select(x => x.RentalSubtotal).SingleAsync();
        decimal Amount(JsonElement x, string name) => decimal.Parse(x.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
        Assert.Equal(expected, Amount(a.RootElement, "total") + Amount(b.RootElement, "total")); Assert.Equal(Amount(a.RootElement, "total"), Amount(a.RootElement, "outstanding")); Assert.Equal("0", a.RootElement.GetProperty("paid").GetString());
        using var foreign = await fixture.Login("admin.b@example.test"); Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/statements/{first.Id}")).StatusCode);
        var partial = await Receipt(finance, hold.Account, "1"); var checkedFunds = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{partial.Id}/verify", new { reason = "Separate rent funds" }, partial.Version, Guid.NewGuid()));
        await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{partial.Id}/allocations", new { targetType = "Charge", targetId = first.Id, amount = "1", reason = "Partial charge payment" }, checkedFunds.Version, Guid.NewGuid()));
        using var paid = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/statements/{first.Id}")).Content.ReadAsStringAsync()); Assert.Equal("PartPaid", paid.RootElement.GetProperty("paymentState").GetString()); Assert.Equal(Amount(a.RootElement, "total") - 1, Amount(paid.RootElement, "outstanding"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, $"/api/v1/leases/{converted.Id}/statements", new { periodStart = "9999-12-01", dueDate = "9999-12-31", reason = "Invalid out-of-range billing period" }, key: Guid.NewGuid())).StatusCode);
        var issued = await db.ChargeStatements.SingleAsync(x => x.Id == first.Id); issued.Total += 1; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Concurrent_deposit_allocations_cannot_exceed_receipt_or_target()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); using var second = await fixture.Login("operator@example.test"); var hold = await Hold(customer, 100);
        var receipt = await Receipt(finance, hold.Account, hold.Deposit); var verified = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Synthetic verified funds" }, receipt.Version, Guid.NewGuid()));
        var amount = decimal.Parse(hold.Deposit, CultureInfo.InvariantCulture); var allocation = decimal.Ceiling(amount * 0.7m).ToString(CultureInfo.InvariantCulture);
        object Allocate() => new { targetType = "Deposit", targetId = hold.Account, amount = allocation, reason = "Concurrent split allocation" };
        var results = await Task.WhenAll(finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", Allocate(), verified.Version, Guid.NewGuid()), second.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", Allocate(), verified.Version, Guid.NewGuid()));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict);
        using var read = JsonDocument.Parse(await (await finance.Send(HttpMethod.Get, $"/api/v1/receipts/{receipt.Id}")).Content.ReadAsStringAsync()); Assert.Equal(allocation, read.RootElement.GetProperty("allocated").GetString());
    }
    [Fact]
    public async Task Cancellation_and_receipt_verification_race_retains_money_and_never_revives_capacity()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); var hold = await Hold(customer, 110); var receipt = await Receipt(finance, hold.Account, hold.Deposit);
        var results = await Task.WhenAll(customer.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/cancel", new { reason = "Customer cancels before handover" }, hold.Version, Guid.NewGuid()), finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Matched late transfer" }, receipt.Version, Guid.NewGuid()));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        using var read = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).Content.ReadAsStringAsync()); Assert.Equal("Cancelled", read.RootElement.GetProperty("state").GetString()); Assert.True(read.RootElement.GetProperty("refundNeeded").GetBoolean());
        using var funds = JsonDocument.Parse(await (await finance.Send(HttpMethod.Get, $"/api/v1/receipts/{receipt.Id}")).Content.ReadAsStringAsync()); Assert.Equal("Verified", funds.RootElement.GetProperty("state").GetString()); Assert.Equal(hold.Deposit, funds.RootElement.GetProperty("unapplied").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = hold.Account, amount = hold.Deposit, reason = "Cannot revive cancelled capacity" }, funds.RootElement.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Expired_hold_funds_remain_visible_for_review_and_finance_scope_cannot_be_forged(bool verifyBeforeExpiry, bool allocatedBeforeExpiry)
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); using var clerk = await fixture.Login("clerk@example.test"); var hold = await Hold(customer, allocatedBeforeExpiry ? 128 : verifyBeforeExpiry ? 124 : 120); var receipt = await Receipt(finance, hold.Account, hold.Deposit);
        if (verifyBeforeExpiry) receipt = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Verified before later expiry" }, receipt.Version, Guid.NewGuid()));
        if (allocatedBeforeExpiry) receipt = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = hold.Account, amount = hold.Deposit, reason = "Deposit allocated before later expiry" }, receipt.Version, Guid.NewGuid()));
        await using (var db = fixture.Db()) { var booking = await db.Bookings.SingleAsync(x => x.Id == hold.Id); booking.HeldAt = DateTimeOffset.UtcNow.AddMinutes(-20); booking.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Clerk is not finance" }, receipt.Version, Guid.NewGuid())).StatusCode);
        var verified = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Late payment retained for review" }, receipt.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = hold.Account, amount = hold.Deposit, reason = "Expired target cannot reserve again" }, verified.Version, Guid.NewGuid())).StatusCode);
        using var read = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).Content.ReadAsStringAsync()); Assert.Equal("Expired", read.RootElement.GetProperty("state").GetString()); Assert.True(read.RootElement.GetProperty("refundNeeded").GetBoolean());
        using var account = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{hold.Account}")).Content.ReadAsStringAsync()); Assert.True(account.RootElement.GetProperty("refundNeeded").GetBoolean());
    }
    [Fact]
    public async Task Separate_verified_receipts_race_for_one_deposit_target_and_cannot_cross_accounts()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); using var secondFinance = await fixture.Login("operator@example.test"); var hold = await Hold(customer, 130); var other = await Hold(customer, 134);
        var one = await Receipt(finance, hold.Account, hold.Deposit); var two = await Receipt(secondFinance, hold.Account, hold.Deposit);
        var a = await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{one.Id}/verify", new { reason = "First verified receipt" }, one.Version, Guid.NewGuid())); var b = await Identity(await secondFinance.Send(HttpMethod.Post, $"/api/v1/receipts/{two.Id}/verify", new { reason = "Second verified receipt" }, two.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{one.Id}/allocations", new { targetType = "Deposit", targetId = other.Account, amount = "1", reason = "Cannot move money to another rental" }, a.Version, Guid.NewGuid())).StatusCode);
        var portion = decimal.Ceiling(decimal.Parse(hold.Deposit, CultureInfo.InvariantCulture) * .7m).ToString(CultureInfo.InvariantCulture);
        object Allocate() => new { targetType = "Deposit", targetId = hold.Account, amount = portion, reason = "Independent receipt targets same deposit" };
        var outcomes = await Task.WhenAll(finance.Send(HttpMethod.Post, $"/api/v1/receipts/{one.Id}/allocations", Allocate(), a.Version, Guid.NewGuid()), secondFinance.Send(HttpMethod.Post, $"/api/v1/receipts/{two.Id}/allocations", Allocate(), b.Version, Guid.NewGuid()));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        using var account = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{hold.Account}")).Content.ReadAsStringAsync()); Assert.Equal(portion, account.RootElement.GetProperty("depositHeld").GetString());
    }
    [Fact]
    public async Task Zero_rounded_deposit_needs_explicit_finance_verification_without_fake_payment()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("operator@example.test"); using var sales = await fixture.Login("sales@example.test");
        var tinyUnit=Guid.NewGuid();
        await using(var db=fixture.Db()) {
            var source=await db.RentableUnits.AsNoTracking().SingleAsync(x=>x.Id==SqlFacilitiesTests.UnitA);
            db.RentableUnits.Add(new WarehouseRent.Infrastructure.Facilities.RentableUnit {Id=tinyUnit,FacilityId=source.FacilityId,ZoneId=source.ZoneId,Code="ZERO-"+tinyUnit.ToString("N")[..12],AreaM2=source.AreaM2,Published=true});
            db.RateVersions.Add(new WarehouseRent.Infrastructure.Facilities.RateVersion {Id=Guid.NewGuid(),UnitId=tinyUnit,SalesValidFrom=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),DailyRate=.0001m});await db.SaveChangesAsync();
        }
        var hold=await Hold(customer,140,unit:tinyUnit);
        Assert.Equal("0", hold.Deposit);
        Assert.Equal(HttpStatusCode.Conflict, (await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "Not yet finance verified" }, hold.Version, Guid.NewGuid())).StatusCode);
        using var account = JsonDocument.Parse(await (await finance.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{hold.Account}")).Content.ReadAsStringAsync());
        await Identity(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{hold.Account}/verify-zero-deposit", new { reason = "Explicitly verify zero under agreed rounded formula" }, account.RootElement.GetProperty("version").GetString(), Guid.NewGuid()));
        await Identity(await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "Zero deposit verified without fictitious funds" }, hold.Version, Guid.NewGuid()));
    }

    [Fact]
    public async Task Financial_summary_waits_for_account_lock_so_one_view_cannot_mix_finance_versions()
    {
        using var customer = await fixture.Login(); var hold = await Hold(customer, 160);
        Task<HttpResponseMessage> reading;
        await using (var writer = fixture.Db())
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            // Existing financial writers use this account UPDATE lock before changing receipt/target totals.
            if(writer.Database.IsNpgsql()) await writer.RentalAccounts.FromSqlInterpolated($"SELECT * FROM khospace.\"RentalAccounts\" WHERE \"Id\"={hold.Account} FOR UPDATE").SingleAsync();
            else await writer.RentalAccounts.FromSqlInterpolated($"SELECT * FROM [RentalAccounts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={hold.Account}").SingleAsync();
            reading = customer.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{hold.Account}");
            await Task.WhenAny(reading, Task.Delay(500));
            Assert.False(reading.IsCompleted, "Financial summary must observe totals only after the in-progress account transaction releases its lock.");
            await transaction.RollbackAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await reading).StatusCode);
    }

}
