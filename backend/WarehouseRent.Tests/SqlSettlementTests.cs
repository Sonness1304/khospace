using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlSettlementTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}"); using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
    private async Task<(Guid Lease, Guid Account, Guid Statement, Guid Grant)> Setup()
    {
        Guid unitId;
        await using (var db = fixture.Db()) { var template = await db.RentableUnits.SingleAsync(x => x.Id == SqlFacilitiesTests.UnitA); var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = template.FacilityId, ZoneId = template.ZoneId, Code = "SET-" + Guid.NewGuid().ToString("N"), AreaM2 = 10, Published = true }; db.RentableUnits.Add(unit); await db.SaveChangesAsync(); unitId = unit.Id; }
        var grant = await fixture.SeedOccupancyOwner(unitId, Today, Today.AddDays(3)); Guid leaseId; Guid accountId;
        await using (var db = fixture.Db()) {
            var lu = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var lease = await db.Leases.SingleAsync(x => x.Id == lu.LeaseId); leaseId = lease.Id; accountId = lease.RentalAccountId;
            var booking = await db.Bookings.SingleAsync(x => x.Id == lease.BookingId); var rate = await db.RateVersions.FirstAsync(x => x.UnitId == SqlFacilitiesTests.UnitA);
            var source = new QuoteLine { Id = Guid.NewGuid(), QuoteId = booking.QuoteId, UnitId = unitId, UnitCode = "Synthetic", UnitVersion = (await db.RentableUnits.AsNoTracking().SingleAsync(x=>x.Id==unitId)).Version, RateVersionId = rate.Id, StartDate = lease.StartDate, EndDate = lease.EndDate, Days = 3, AreaM2 = 10, DailyRate = 3.3333m, Amount = 100 };
            var version = new LeaseVersion { Id = Guid.NewGuid(), LeaseId = lease.Id, VersionNo = 1, PolicyVersion = "SyntheticSettlement" }; db.QuoteLines.Add(source); db.LeaseVersions.Add(version);
            db.LeaseVersionLines.Add(new LeaseVersionLine { Id = Guid.NewGuid(), LeaseVersionId = version.Id, LeaseUnitId = grant, SourceQuoteLineId = source.Id, StartDate = source.StartDate, EndDate = source.EndDate, Days = source.Days, AreaM2 = source.AreaM2, DailyRate = source.DailyRate, Amount = source.Amount });
            db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = unitId, LeaseUnitId = grant, OrganizationId = lease.OrganizationId, ScheduledEnd = lease.EndDate, HandedOverAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        }
        using var finance = await fixture.Login("finance@example.test"); var month = new DateOnly(Today.Year, Today.Month, 1);
        var statement = (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/leases/{leaseId}/statements", new { periodStart = month, dueDate = month, reason = "Issue agreed rental" }, key: Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        return (leaseId, accountId, statement, grant);
    }
    private async Task<(Guid Id, string Version)> Receipt(Guid account, string amount)
    {
        using var finance = await fixture.Login("finance@example.test"); var item = (await Ok(await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = account, amount, reference = Guid.NewGuid().ToString(), method = "BankTransfer", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "Synthetic bank match", reason = "Record observed funds" }, key: Guid.NewGuid()))).GetProperty("result"); var id = item.GetProperty("resourceId").GetGuid();
        var verified = (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{id}/verify", new { reason = "Verified actual evidence" }, item.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result"); return (id, verified.GetProperty("version").GetString()!);
    }
    private async Task<string> Allocate(Guid receipt, string version, string type, Guid target, string amount)
    {
        using var finance = await fixture.Login("finance@example.test"); return (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt}/allocations", new { targetType = type, targetId = target, amount, reason = "Apply verified funds" }, version, Guid.NewGuid()))).GetProperty("result").GetProperty("version").GetString()!;
    }
    private async Task Clear(Guid leaseId)
    {
        using var manager = await fixture.Login("manager@example.test"); var lease = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/leases/{leaseId}"));
        var terminated = (await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{leaseId}/terminate", new { reason = "End rental for settlement", terminationConfirmed = true }, lease.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result");
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{leaseId}/physical-clearance", new { reason = "Empty inspected warehouse", inspectionConfirmed = true }, terminated.GetProperty("version").GetString(), Guid.NewGuid()));
    }
    private static object Adjustment(string kind, string amount = "10") => new { kind, amount, quantity = "2.5", unitPrice = "3", evidenceReference = "Synthetic approved evidence", reason = "Correct issued rental", approved = true };
    private static object Entry(string kind, string amount, Guid? statement = null, Guid? receipt = null, string? reference = null) => new { kind, amount, chargeStatementId = statement, paymentReceiptId = receipt, reference = reference ?? Guid.NewGuid().ToString(), evidenceReference = "Synthetic manual transaction proof", performedAt = DateTimeOffset.UtcNow.AddMinutes(-1), reason = "Explicit settlement decision", confirmed = true };
    private async Task<JsonElement> Settlement(Guid account) { using var finance = await fixture.Login("finance@example.test"); return await Ok(await finance.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{account}/settlement")); }
    [Fact]
    public async Task Paid_credit_releases_allocations_to_unapplied_funds_without_editing_issued_history()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "100"); await Allocate(r.Id, r.Version, "Charge", s.Statement, "100"); using var finance = await fixture.Login("finance@example.test"); var key = Guid.NewGuid(); var body = Adjustment("Credit", "30"); var path = $"/api/v1/statements/{s.Statement}/adjustments";
        var saved = await Ok(await finance.Send(HttpMethod.Post, path, body, key: key)); Assert.Equal(saved.GetRawText(), (await Ok(await finance.Send(HttpMethod.Post, path, body, key: key))).GetRawText());
        var summary = await Settlement(s.Account); Assert.Equal("70", summary.GetProperty("netBilled").GetString()); Assert.Equal("70", summary.GetProperty("rentalPaid").GetString()); Assert.Equal("30", summary.GetProperty("unappliedVerified").GetString());
        var statement = await Ok(await finance.Send(HttpMethod.Get, $"/api/v1/statements/{s.Statement}")); Assert.Equal("100", statement.GetProperty("total").GetString()); Assert.Equal("0", statement.GetProperty("outstanding").GetString());
        var receipt = await Ok(await finance.Send(HttpMethod.Get, $"/api/v1/receipts/{r.Id}")); Assert.Equal("30", receipt.GetProperty("unapplied").GetString());
        await using var db = fixture.Db(); var original = await db.ChargeStatements.SingleAsync(x => x.Id == s.Statement); original.Total = 70; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Service_snapshots_quantity_price_and_credit_cannot_exceed_net_charge()
    {
        var s = await Setup(); using var finance = await fixture.Login("finance@example.test"); var path = $"/api/v1/statements/{s.Statement}/adjustments";
        var service = (await Ok(await finance.Send(HttpMethod.Post, path, Adjustment("Service"), key: Guid.NewGuid()))).GetProperty("result"); Assert.Equal("8", service.GetProperty("amount").GetString());
        await Ok(await finance.Send(HttpMethod.Post, path, Adjustment("Supplement", "2"), key: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, path, Adjustment("Credit", "111"), key: Guid.NewGuid())).StatusCode); Assert.Equal("110", (await Settlement(s.Account)).GetProperty("netBilled").GetString());
    }
    [Fact]
    public async Task Deposit_deduction_refund_and_final_closure_preserve_separate_liability_and_seal_account()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "20"); await Allocate(r.Id, r.Version, "Deposit", s.Account, "20"); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test"); var path = $"/api/v1/rental-accounts/{s.Account}/settlement-entries";
        await Ok(await finance.Send(HttpMethod.Post, path, Entry("Deduction", "5", s.Statement), key: Guid.NewGuid()));
        var summary = await Settlement(s.Account); Assert.Equal("15", summary.GetProperty("depositHeld").GetString()); Assert.Equal("5", summary.GetProperty("rentalPaid").GetString()); Assert.Equal("95", summary.GetProperty("outstanding").GetString());
        await Ok(await finance.Send(HttpMethod.Post, path, Entry("RefundDeposit", "15"), key: Guid.NewGuid()));
        var rental = await Receipt(s.Account, "95"); await Allocate(rental.Id, rental.Version, "Charge", s.Statement, "95"); summary = await Settlement(s.Account); Assert.True(summary.GetProperty("canClose").GetBoolean()); var version = summary.GetProperty("version").GetString(); var close = new { reason = "All goods and funds settled", closureConfirmed = true }; var key = Guid.NewGuid();
        var saved = await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{s.Account}/close", close, version, key)); Assert.Equal(saved.GetRawText(), (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{s.Account}/close", close, version, key))).GetRawText());
        await using var db = fixture.Db(); Assert.Equal("Closed", (await db.Leases.SingleAsync(x => x.Id == s.Lease)).State); Assert.Equal("Closed", (await db.RentalAccounts.SingleAsync(x => x.Id == s.Account)).State); Assert.Equal(20m, await db.DepositTransactions.Where(x => x.RentalAccountId == s.Account).SumAsync(x => x.Amount));
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Supplement"), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = s.Account, amount = "1", reference = Guid.NewGuid().ToString(), method = "Cash", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "Closed account", reason = "Cannot reopen" }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Concurrent_refunds_cannot_exceed_held_deposit_or_unapplied_receipt()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "30"); await Allocate(r.Id, r.Version, "Deposit", s.Account, "20"); await Clear(s.Lease); using var one = await fixture.Login("finance@example.test"); using var two = await fixture.Login("operator@example.test"); var path = $"/api/v1/rental-accounts/{s.Account}/settlement-entries";
        foreach (var kind in new[] { "RefundDeposit", "RefundUnapplied" }) {
            var amount = kind == "RefundDeposit" ? "15" : "7";
            var results = await Task.WhenAll(one.Send(HttpMethod.Post, path, Entry(kind, amount, receipt: kind == "RefundUnapplied" ? r.Id : null), key: Guid.NewGuid()), two.Send(HttpMethod.Post, path, Entry(kind, amount, receipt: kind == "RefundUnapplied" ? r.Id : null), key: Guid.NewGuid()));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        var summary = await Settlement(s.Account); Assert.Equal("5", summary.GetProperty("depositHeld").GetString()); Assert.Equal("3", summary.GetProperty("unappliedVerified").GetString());
    }
    [Fact]
    public async Task Credit_releases_prior_deposit_deduction_back_to_security_instead_of_income()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "20"); await Allocate(r.Id, r.Version, "Deposit", s.Account, "20"); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test");
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{s.Account}/settlement-entries", Entry("Deduction", "20", s.Statement), key: Guid.NewGuid()));
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "90"), key: Guid.NewGuid())); var summary = await Settlement(s.Account); Assert.Equal("10", summary.GetProperty("depositHeld").GetString()); Assert.Equal("10", summary.GetProperty("rentalPaid").GetString()); Assert.Equal("10", summary.GetProperty("netBilled").GetString());
    }
    [Fact]
    public async Task Closure_refuses_custody_unpaid_charges_and_unverified_funds()
    {
        var s = await Setup(); using var finance = await fixture.Login("finance@example.test"); var close = new { reason = "Cannot bypass readiness", closureConfirmed = true }; var path = $"/api/v1/rental-accounts/{s.Account}/close";
        var summary = await Settlement(s.Account); Assert.False(summary.GetProperty("canClose").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, path, close, summary.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
        await Clear(s.Lease); await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "100"), key: Guid.NewGuid()));
        var pending = (await Ok(await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = s.Account, amount = "1", reference = Guid.NewGuid().ToString(), method = "Cash", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "Needs investigation", reason = "Unverified receipt" }, key: Guid.NewGuid()))).GetProperty("result");
        summary = await Settlement(s.Account); Assert.False(summary.GetProperty("canClose").GetBoolean());
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{pending.GetProperty("resourceId").GetGuid()}/reject", new { reason = "Not an actual received payment" }, pending.GetProperty("version").GetString(), Guid.NewGuid())); Assert.True((await Settlement(s.Account)).GetProperty("canClose").GetBoolean());
    }
    [Fact]
    public async Task Finance_scope_confirmation_and_precision_are_enforced()
    {
        var s = await Setup(); using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var finance = await fixture.Login("finance@example.test"); var path = $"/api/v1/statements/{s.Statement}/adjustments";
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, path, Adjustment("Credit"), key: Guid.NewGuid())).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, path, Adjustment("Credit"), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, path, Adjustment("Credit", "0.1"), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, path, new { kind = "Credit", amount = "1", evidenceReference = "", reason = "Missing evidence", approved = false }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Cancelled_booking_and_late_receipt_verification_converge_to_refund_then_close_without_lease()
    {
        var s = await Setup(); Guid account; Guid booking;
        await using (var db = fixture.Db()) { var unit = await db.LeaseUnits.SingleAsync(x => x.Id == s.Grant); var line = await fixture.SeedAllocationOwner(unit.UnitId, Today.AddDays(10), Today.AddDays(12)); var b = await db.BookingLines.AsNoTracking().SingleAsync(x => x.Id == line); booking = b.BookingId; account = await db.RentalAccounts.Where(x => x.BookingId == booking).Select(x => x.Id).SingleAsync(); }
        using var finance = await fixture.Login("finance@example.test"); using var customer = await fixture.Login();
        var recorded = (await Ok(await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = account, amount = "20", reference = Guid.NewGuid().ToString(), method = "Cash", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "Late ledger evidence", reason = "Record before cancellation" }, key: Guid.NewGuid()))).GetProperty("result");
        var read = await Ok(await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{booking}")); await Ok(await customer.Send(HttpMethod.Post, $"/api/v1/bookings/{booking}/cancel", new { reason = "Cancel before verification" }, read.GetProperty("version").GetString(), Guid.NewGuid()));
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{recorded.GetProperty("resourceId").GetGuid()}/verify", new { reason = "Verified after cancellation" }, recorded.GetProperty("version").GetString(), Guid.NewGuid()));
        var summary = await Settlement(account); Assert.Equal("20", summary.GetProperty("unappliedVerified").GetString()); Assert.False(summary.GetProperty("canClose").GetBoolean());
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/settlement-entries", Entry("RefundUnapplied", "20", receipt: recorded.GetProperty("resourceId").GetGuid()), key: Guid.NewGuid())); summary = await Settlement(account); Assert.True(summary.GetProperty("canClose").GetBoolean());
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/close", new { reason = "No custody and refund documented", closureConfirmed = true }, summary.GetProperty("version").GetString(), Guid.NewGuid()));
    }
    [Fact]
    public async Task Closure_revalidates_unbilled_segments_version_and_immutable_settlement_journals()
    {
        var s = await Setup(); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test"); await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "100"), key: Guid.NewGuid())); var before = await Settlement(s.Account);
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Supplement", "1"), key: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{s.Account}/close", new { reason = "Snapshot no longer current", closureConfirmed = true }, before.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
        await using var db = fixture.Db(); var adjustment = await db.StatementAdjustments.FirstAsync(x => x.RentalAccountId == s.Account); adjustment.Amount = 99; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Refund_reference_is_unique_and_already_refunded_funds_cannot_be_allocated_again()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "10"); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test"); var body = Entry("RefundUnapplied", "7", receipt: r.Id, reference: "REF-" + Guid.NewGuid()); var path = $"/api/v1/rental-accounts/{s.Account}/settlement-entries";
        await Ok(await finance.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())); Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{r.Id}/allocations", new { targetType = "Charge", targetId = s.Statement, amount = "4", reason = "Cannot spend refunded money" }, r.Version, Guid.NewGuid())).StatusCode); Assert.Equal("3", (await Settlement(s.Account)).GetProperty("unappliedVerified").GetString());
    }
    [Fact]
    public async Task Credit_audit_failure_rolls_back_adjustment_release_and_command()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "100"); await Allocate(r.Id, r.Version, "Charge", s.Statement, "100"); var key = Guid.NewGuid();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectSettlementAudit()).Options)); }));
        using var finance = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await finance.RefreshCsrf(); await Ok(await finance.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "finance@example.test", password = DemoIdentitySeed.Password })); await finance.RefreshCsrf();
        Assert.Equal(HttpStatusCode.InternalServerError, (await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "30"), key: key)).StatusCode); var summary = await Settlement(s.Account); Assert.Equal("100", summary.GetProperty("netBilled").GetString()); Assert.Equal("100", summary.GetProperty("rentalPaid").GetString()); Assert.Equal("0", summary.GetProperty("unappliedVerified").GetString());
        await using var db = fixture.Db(); Assert.False(await db.StatementAdjustments.AnyAsync(x => x.RentalAccountId == s.Account)); Assert.False(await db.AllocationReleases.AnyAsync(x => x.RentalAccountId == s.Account)); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
    }
    private sealed class RejectSettlementAudit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action == "StatementAdjusted")) throw new InvalidOperationException("Synthetic settlement audit failure"); return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Expired_account_closure_cannot_be_reopened_by_customer_cancellation()
    {
        var s = await Setup(); Guid account; Guid booking; string version;
        await using (var db = fixture.Db()) {
            var unit = await db.LeaseUnits.SingleAsync(x => x.Id == s.Grant); var line = await fixture.SeedAllocationOwner(unit.UnitId, Today.AddDays(20), Today.AddDays(22)); var bline = await db.BookingLines.SingleAsync(x => x.Id == line); booking = bline.BookingId;
            var b = await db.Bookings.SingleAsync(x => x.Id == booking); b.HeldAt = DateTimeOffset.UtcNow.AddMinutes(-30); b.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5); await db.SaveChangesAsync(); version = Convert.ToBase64String(b.Version); account = await db.RentalAccounts.Where(x => x.BookingId == booking).Select(x => x.Id).SingleAsync();
        }
        using var finance = await fixture.Login("finance@example.test"); using var customer = await fixture.Login(); var summary = await Settlement(account); Assert.True(summary.GetProperty("canClose").GetBoolean());
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/close", new { reason = "Empty expired account", closureConfirmed = true }, summary.GetProperty("version").GetString(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, $"/api/v1/bookings/{booking}/cancel", new { reason = "Must not reopen closed account" }, version, Guid.NewGuid())).StatusCode);
        await using var check = fixture.Db(); Assert.Equal("Closed", (await check.RentalAccounts.SingleAsync(x => x.Id == account)).State);
    }
    [Fact]
    public async Task Unbilled_contract_version_blocks_closure_until_unique_supplement_is_issued_and_paid()
    {
        var s = await Setup(); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test"); await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "100"), key: Guid.NewGuid()));
        await using (var db = fixture.Db()) {
            var original = await (from line in db.LeaseVersionLines join v in db.LeaseVersions on line.LeaseVersionId equals v.Id where v.LeaseId == s.Lease select line).SingleAsync();
            var version = new LeaseVersion { Id = Guid.NewGuid(), LeaseId = s.Lease, VersionNo = 2, PolicyVersion = "Synthetic added segment" }; db.LeaseVersions.Add(version);
            db.LeaseVersionLines.Add(new LeaseVersionLine { Id = Guid.NewGuid(), LeaseVersionId = version.Id, LeaseUnitId = s.Grant, SourceQuoteLineId = original.SourceQuoteLineId, StartDate = Today, EndDate = Today.AddDays(1), Days = 1, AreaM2 = original.AreaM2, DailyRate = original.DailyRate, Amount = 10 }); await db.SaveChangesAsync();
        }
        var summary = await Settlement(s.Account); Assert.Contains(summary.GetProperty("blockers").EnumerateArray(), x => x.GetString() == "UNBILLED_RENTAL_SEGMENTS");
        var supplement = new { evidenceReference = "Accepted added rental version", reason = "Bill uncovered segment", approved = true };
        var results = await Task.WhenAll(finance.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/supplement-rent", supplement, key: Guid.NewGuid()), SupplementSecond());
        async Task<HttpResponseMessage> SupplementSecond() { using var other = await fixture.Login("operator@example.test"); return await other.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/supplement-rent", supplement, key: Guid.NewGuid()); }
        var added = (await Ok(results[0])).GetProperty("result").GetProperty("addedSegments").GetInt32() + (await Ok(results[1])).GetProperty("result").GetProperty("addedSegments").GetInt32(); Assert.Equal(1, added);
        var r = await Receipt(s.Account, "10"); await Allocate(r.Id, r.Version, "Charge", s.Statement, "10"); Assert.True((await Settlement(s.Account)).GetProperty("canClose").GetBoolean());
    }
    [Fact]
    public async Task Concurrent_close_and_new_supplement_cannot_both_commit()
    {
        var s = await Setup(); await Clear(s.Lease); using var one = await fixture.Login("finance@example.test"); using var two = await fixture.Login("operator@example.test"); await Ok(await one.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Credit", "100"), key: Guid.NewGuid())); var summary = await Settlement(s.Account);
        var results = await Task.WhenAll(one.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{s.Account}/close", new { reason = "Current zero balances", closureConfirmed = true }, summary.GetProperty("version").GetString(), Guid.NewGuid()), two.Send(HttpMethod.Post, $"/api/v1/statements/{s.Statement}/adjustments", Adjustment("Supplement", "1"), key: Guid.NewGuid()));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed);
    }
    [Fact]
    public async Task Concurrent_deductions_cannot_consume_the_same_security_twice()
    {
        var s = await Setup(); var r = await Receipt(s.Account, "20"); await Allocate(r.Id, r.Version, "Deposit", s.Account, "20"); await Clear(s.Lease); using var one = await fixture.Login("finance@example.test"); using var two = await fixture.Login("operator@example.test"); var path = $"/api/v1/rental-accounts/{s.Account}/settlement-entries";
        var results = await Task.WhenAll(one.Send(HttpMethod.Post, path, Entry("Deduction", "15", s.Statement), key: Guid.NewGuid()), two.Send(HttpMethod.Post, path, Entry("Deduction", "15", s.Statement), key: Guid.NewGuid())); Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("5", (await Settlement(s.Account)).GetProperty("depositHeld").GetString()); Assert.Equal("15", (await Settlement(s.Account)).GetProperty("rentalPaid").GetString());
    }
    [Fact]
    public async Task Refund_rejects_foreign_target_duplicate_reference_future_date_and_invalid_confirmation()
    {
        var s = await Setup(); var other = await Setup(); var r = await Receipt(other.Account, "10"); await Clear(s.Lease); using var finance = await fixture.Login("finance@example.test"); var path = $"/api/v1/rental-accounts/{s.Account}/settlement-entries";
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, path, Entry("RefundUnapplied", "1", receipt: r.Id), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, path, new { kind = "RefundDeposit", amount = "1", reference = "Future", evidenceReference = "Fake future proof", performedAt = DateTimeOffset.UtcNow.AddDays(1), reason = "Cannot record future transfer", confirmed = true }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, path, new { kind = "RefundDeposit", amount = "1", reference = "Unconfirmed", evidenceReference = "Evidence", performedAt = DateTimeOffset.UtcNow.AddMinutes(-1), reason = "Not confirmed", confirmed = false }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Closed_expired_account_with_documented_full_refund_has_no_outstanding_refund_warning()
    {
        var s = await Setup(); Guid account;
        await using (var db = fixture.Db()) {
            var unit = await db.LeaseUnits.SingleAsync(x => x.Id == s.Grant); var line = await fixture.SeedAllocationOwner(unit.UnitId, Today.AddDays(30), Today.AddDays(32)); var bookingId = await db.BookingLines.Where(x => x.Id == line).Select(x => x.BookingId).SingleAsync();
            var booking = await db.Bookings.SingleAsync(x => x.Id == bookingId); booking.HeldAt = DateTimeOffset.UtcNow.AddMinutes(-30); booking.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); account = await db.RentalAccounts.Where(x => x.BookingId == bookingId).Select(x => x.Id).SingleAsync(); await db.SaveChangesAsync();
        }
        var r = await Receipt(account, "20"); using var finance = await fixture.Login("finance@example.test");
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/settlement-entries", Entry("RefundUnapplied", "20", receipt: r.Id), key: Guid.NewGuid())); var summary = await Settlement(account);
        await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/rental-accounts/{account}/close", new { reason = "Refund documented, no custody", closureConfirmed = true }, summary.GetProperty("version").GetString(), Guid.NewGuid()));
        var read = await Ok(await finance.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{account}")); Assert.Equal("Closed", read.GetProperty("state").GetString()); Assert.False(read.GetProperty("refundNeeded").GetBoolean()); Assert.Equal("0", read.GetProperty("unappliedVerified").GetString());
    }
}
