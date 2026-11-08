using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed partial class SqlReceivablesTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static string PathFor(string kind = "reports", int page = 0, Guid? org = null, Guid? facility = null) =>
        $"/api/v1/{kind}/receivables?organizationId={org ?? DemoIdentitySeed.OrganizationA}&facilityId={facility ?? DemoIdentitySeed.FacilityA}&startDate={Today.AddDays(-120):yyyy-MM-dd}&endDate={Today.AddDays(2):yyyy-MM-dd}&page={page}";
    private async Task<ChargeStatement> Statement(DateOnly due, decimal total = 1000)
    {
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, Today, Today.AddDays(1));
        await using var db = fixture.Db();
        var lease = await db.Leases.SingleAsync(x => x.Id == db.LeaseUnits.Where(g => g.Id == grant).Select(g => g.LeaseId).Single());
        var statement = new ChargeStatement { Id = Guid.NewGuid(), LeaseId = lease.Id, RentalAccountId = lease.RentalAccountId,
            OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, PeriodStart = new DateOnly(due.Year, due.Month, 1), DueDate = due, Total = total, IssuedAt = DateTimeOffset.UtcNow };
        db.ChargeStatements.Add(statement); await db.SaveChangesAsync(); return statement;
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
    [Fact]
    public async Task Current_receivables_reconciles_credits_both_release_sources_and_ignores_unapplied_deposits()
    {
        var s = await Statement(Today.AddDays(-31));
        await using (var db = fixture.Db())
        {
            var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
                Method = "BankTransfer", Reference = Guid.NewGuid().ToString(), EvidenceReference = "Synthetic receipt", Amount = 5000, State = "Verified", ReceivedAt = DateTimeOffset.UtcNow, VerifiedAt = DateTimeOffset.UtcNow };
            db.PaymentReceipts.Add(receipt);
            db.PaymentReceipts.Add(new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
                Method = "BankTransfer", Reference = Guid.NewGuid().ToString(), EvidenceReference = "Unverified synthetic receipt", Amount = 99999, State = "Recorded", ReceivedAt = DateTimeOffset.UtcNow });
            db.PaymentIntents.Add(new PaymentIntent { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
                Provider = "Vnpay", Mode = "Sandbox", MerchantId = "SYNTHETIC", Method = "VnpayCard", Amount = 99999, CreatedBy = "synthetic", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            var allocation = new FundAllocation { Id = Guid.NewGuid(), PaymentReceiptId = receipt.Id, RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, TargetType = "Charge", ChargeStatementId = s.Id, Amount = 600, At = DateTimeOffset.UtcNow, ActorId = "synthetic" };
            var deposit = new FundAllocation { Id = Guid.NewGuid(), PaymentReceiptId = receipt.Id, RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, TargetType = "Deposit", Amount = 2000, At = DateTimeOffset.UtcNow, ActorId = "synthetic" };
            db.FundAllocations.AddRange(allocation, deposit);
            db.DepositTransactions.Add(new DepositTransaction { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, FundAllocationId = deposit.Id, Amount = 2000, At = DateTimeOffset.UtcNow });
            var deduction = new SettlementEntry { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, ChargeStatementId = s.Id, Kind = "Deduction", Amount = 200, Reference = Guid.NewGuid().ToString(), EvidenceReference = "Synthetic", Reason = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow, PerformedAt = DateTimeOffset.UtcNow };
            db.SettlementEntries.Add(deduction);
            var credit = new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId, Kind = "Credit", Amount = 500, EvidenceReference = "Synthetic", Reason = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow };
            db.StatementAdjustments.Add(credit);
            db.AllocationReleases.AddRange(new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, StatementAdjustmentId = credit.Id, FundAllocationId = allocation.Id, Amount = 100, At = DateTimeOffset.UtcNow },
                new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, StatementAdjustmentId = credit.Id, SettlementEntryId = deduction.Id, Amount = 200, At = DateTimeOffset.UtcNow });
            db.StatementAdjustments.Add(new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId, Kind = "Supplement", Amount = 250, EvidenceReference = "Synthetic", Reason = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        using var finance = await fixture.Login("finance@example.test"); var data = await Read(await finance.Send(HttpMethod.Get, PathFor()));
        var row = data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("statementId").GetGuid() == s.Id);
        Assert.Equal("750", row.GetProperty("effectiveChargeVnd").GetString()); Assert.Equal("500", row.GetProperty("effectivePaidVnd").GetString());
        Assert.Equal("250", row.GetProperty("outstandingVnd").GetString()); Assert.Equal("300", row.GetProperty("releasesVnd").GetString());
        Assert.Equal("Overdue31To60", row.GetProperty("bucket").GetString()); Assert.Equal(31, row.GetProperty("daysOverdue").GetInt32());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), data.GetProperty("asOfDate").GetString());
        await using var verified = fixture.Db();
        Assert.Equal(750, await WarehouseRent.Api.Features.Settlements.SettlementBalances.NetChargeAsync(verified, s.Id, default));
        Assert.Equal(500, await WarehouseRent.Api.Features.Settlements.SettlementBalances.PaidAsync(verified, s.Id, default));
    }
    [Fact]
    public async Task Aging_boundaries_and_totals_are_whole_range_even_on_later_page()
    {
        foreach (var age in new[] { -1, 0, 1, 30, 31, 60, 61, 90, 91 }) await Statement(Today.AddDays(-age), 10);
        for (var i = 0; i < 43; i++) await Statement(Today, 0);
        using var finance = await fixture.Login("finance@example.test");
        var first = await Read(await finance.Send(HttpMethod.Get, PathFor())); var next = await Read(await finance.Send(HttpMethod.Get, PathFor(page: 1)));
        Assert.Equal(50, first.GetProperty("items").GetArrayLength()); Assert.True(next.GetProperty("items").GetArrayLength() >= 2);
        Assert.Equal(first.GetProperty("summary").GetRawText(), next.GetProperty("summary").GetRawText());
        var all = first.GetProperty("items").EnumerateArray().Concat(next.GetProperty("items").EnumerateArray()).ToArray();
        foreach (var (age, bucket) in new[] { (0,"Current"),(1,"Overdue1To30"),(30,"Overdue1To30"),(31,"Overdue31To60"),(60,"Overdue31To60"),(61,"Overdue61To90"),(90,"Overdue61To90"),(91,"OverdueOver90") })
            Assert.Contains(all, x => x.GetProperty("daysOverdue").GetInt32() == age && x.GetProperty("bucket").GetString() == bucket);
        Assert.Contains(all, x => x.GetProperty("bucket").GetString() == "Settled");
        Assert.Equal(all.Length, all.Select(x => x.GetProperty("statementId").GetGuid()).Distinct().Count());
        var summary = first.GetProperty("summary");
        Assert.Equal(decimal.Parse(summary.GetProperty("outstandingVnd").GetString()!), new[] { "currentVnd", "overdue1To30Vnd", "overdue31To60Vnd", "overdue61To90Vnd", "overdueOver90Vnd" }.Sum(key => decimal.Parse(summary.GetProperty(key).GetString()!)));
    }
    [Theory]
    [InlineData("admin.a@example.test")][InlineData("admin.b@example.test")][InlineData("manager@example.test")][InlineData("clerk@example.test")][InlineData("sales@example.test")]
    public async Task Non_finance_cannot_read_or_export(string user)
    {
        using var client = await fixture.Login(user);
        Assert.Equal(HttpStatusCode.NotFound, (await client.Send(HttpMethod.Get, PathFor())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.Send(HttpMethod.Get, PathFor("exports"))).StatusCode);
    }
    [Fact]
    public async Task Csv_is_scoped_download_with_component_columns_and_audit()
    {
        var statement = await Statement(Today); using var finance = await fixture.Login("finance@example.test");
        var response = await finance.Send(HttpMethod.Get, PathFor("exports"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString()); Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        var csv = await response.Content.ReadAsStringAsync(); Assert.Contains("effective_charge_vnd", csv); Assert.Contains(statement.Id.ToString(), csv);
        await using var db = fixture.Db(); Assert.True(await db.SecurityAudits.AnyAsync(x => x.Action == "ReceivablesExported" && x.ActorId == "finance@example.test" && x.OrganizationId == statement.OrganizationId));
    }
    [Fact]
    public async Task Invalid_range_or_page_is_refused()
    {
        using var finance = await fixture.Login("finance@example.test");
        foreach (var suffix in new[] { "&page=-1", "&page=100" }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Get, PathFor().Replace("&page=0", suffix))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Get, PathFor().Replace(Today.AddDays(-120).ToString("yyyy-MM-dd"), Today.AddDays(-400).ToString("yyyy-MM-dd")))).StatusCode);
    }
}
