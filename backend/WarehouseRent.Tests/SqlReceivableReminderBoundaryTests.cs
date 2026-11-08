using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Jobs;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;
public sealed partial class SqlReceivableReminderTests
{
    [Fact]
    public async Task Concurrent_actors_with_no_prior_daily_record_commit_one_delivery_and_a_new_day_is_distinct()
    {
        var s = await Statement(5); using var finance = await fixture.Login("finance@example.test"); using var op = await fixture.Login("operator@example.test");
        await using var db = fixture.Db(); var yesterday = Today.AddDays(-1); var prior = new NoticeOutbox { Id = Guid.NewGuid(), OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
            AggregateId = s.Id, Kind = "PaymentOverdue", BusinessKey = $"receivable/{s.Id}/{yesterday:yyyy-MM-dd}", Message = "Synthetic prior day event", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), DeliveredAt = DateTimeOffset.UtcNow.AddDays(-1) };
        db.NoticeOutbox.Add(prior); db.InAppNotices.Add(new InAppNotice { Id = Guid.NewGuid(), OutboxId = prior.Id, OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
            Kind = prior.Kind, AggregateId = s.Id, Message = prior.Message, CreatedAt = prior.CreatedAt }); await db.SaveChangesAsync();
        var callers=new[]{finance,op};var keys=new[]{Guid.NewGuid(),Guid.NewGuid()};
        var responses = await Task.WhenAll(callers.Select((caller,i)=>caller.Send(HttpMethod.Post,PathFor(),Input(),key:keys[i])));
        var count=0;
        for(var i=0;i<responses.Length;i++) {
            var response=responses[i];
            if(response.StatusCode==HttpStatusCode.Conflict) {
                Assert.True(db.Database.IsNpgsql());Assert.Contains("COMMAND_RETRY_REQUIRED",await response.Content.ReadAsStringAsync());
                Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==keys[i]));
                Assert.Equal(HttpStatusCode.NotFound,(await callers[i].Send(HttpMethod.Get,$"/api/v1/commands/{keys[i]}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
                response=await callers[i].Send(HttpMethod.Post,PathFor(),Input(),key:keys[i]);
            }
            count+=(await Result(response)).GetProperty("created").GetInt32();
        }
        Assert.Equal(1, count); Assert.Equal(2, await db.InAppNotices.CountAsync());
        var key = Guid.NewGuid(); await Result(await op.Send(HttpMethod.Post, PathFor(), Input(), key: key));
        Assert.Equal(HttpStatusCode.Conflict, (await op.Send(HttpMethod.Post, PathFor(facility: DemoIdentitySeed.FacilityB), Input(), key: key)).StatusCode);
    }
    [Fact]
    public async Task Source_page_is_stable_including_settled_and_5001_rows_refuse_all_writes()
    {
        var s = await Statement(3); await using var db = fixture.Db();
        db.ChargeStatements.AddRange(Enumerable.Range(0, 4999).Select(i => new ChargeStatement { Id = Guid.NewGuid(), LeaseId = s.LeaseId, RentalAccountId = s.RentalAccountId,
            OrganizationId = s.OrganizationId, FacilityId = s.FacilityId, PeriodStart = new DateOnly(1500, 1, 1).AddMonths(i), DueDate = s.DueDate,
            IssuedAt = DateTimeOffset.UtcNow, Total = i % 2 == 0 ? 0 : 1 })); await db.SaveChangesAsync();
        using var finance = await fixture.Login("finance@example.test");
        var last = await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(99), key: Guid.NewGuid()));
        Assert.Equal(50, last.GetProperty("scanned").GetInt32()); Assert.Equal(System.Text.Json.JsonValueKind.Null, last.GetProperty("nextPage").ValueKind);
        Assert.Equal(last.GetProperty("eligible").GetInt32(), last.GetProperty("created").GetInt32());
        var first = await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(0), key: Guid.NewGuid()));
        Assert.Equal(1, first.GetProperty("nextPage").GetInt32()); Assert.Equal(50, first.GetProperty("scanned").GetInt32());
        var before = await db.InAppNotices.CountAsync();
        db.ChargeStatements.Add(new ChargeStatement { Id = Guid.NewGuid(), LeaseId = s.LeaseId, RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId,
            FacilityId = s.FacilityId, PeriodStart = new DateOnly(2000, 1, 1), DueDate = s.DueDate, IssuedAt = DateTimeOffset.UtcNow, Total = 1 }); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, PathFor(), Input(1), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(before, await db.InAppNotices.CountAsync()); Assert.Equal(2, await db.OperationalCommands.CountAsync());
    }
    [Fact]
    public async Task Audit_fault_rolls_back_outbox_delivery_and_receipt_then_same_key_can_succeed()
    {
        await Statement(5);
        using var host = fixture.Host.WithWebHostBuilder(b => b.ConfigureServices(s => { s.RemoveAll<AppDbContext>();
            s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectReminderAudit()).Options)); }));
        using var broken = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await broken.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await broken.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "finance@example.test", password = DemoIdentitySeed.Password })).StatusCode); await broken.RefreshCsrf();
        var key = Guid.NewGuid(); Assert.Equal(HttpStatusCode.InternalServerError, (await broken.Send(HttpMethod.Post, PathFor(), Input(), key: key)).StatusCode);
        await using var db = fixture.Db(); Assert.Empty(await db.InAppNotices.ToListAsync()); Assert.Empty(await db.NoticeOutbox.ToListAsync()); Assert.Empty(await db.OperationalCommands.ToListAsync());
        using var finance = await fixture.Login("finance@example.test"); Assert.Equal(1, (await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key))).GetProperty("created").GetInt32());
    }
    [Fact]
    public async Task Linked_allocations_deductions_and_credit_releases_match_report_without_mutating_financial_evidence()
    {
        var s = await Statement(5);
        await using var db = fixture.Db();
        var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, FacilityId = s.FacilityId,
            Method = "BankTransfer", Reference = Guid.NewGuid().ToString(), EvidenceReference = "Synthetic", Amount = 1000, State = "Verified", ReceivedAt = DateTimeOffset.UtcNow, VerifiedAt = DateTimeOffset.UtcNow };
        var allocation = new FundAllocation { Id = Guid.NewGuid(), PaymentReceiptId = receipt.Id, RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId,
            TargetType = "Charge", ChargeStatementId = s.Id, Amount = 600, At = DateTimeOffset.UtcNow, ActorId = "synthetic" };
        var deduction = new SettlementEntry { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, OrganizationId = s.OrganizationId, ChargeStatementId = s.Id,
            Kind = "Deduction", Amount = 200, Reference = Guid.NewGuid().ToString(), EvidenceReference = "Synthetic", Reason = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow, PerformedAt = DateTimeOffset.UtcNow };
        var credit = new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId, Kind = "Credit", Amount = 500, EvidenceReference = "Synthetic", Reason = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow };
        db.PaymentReceipts.Add(receipt); db.FundAllocations.Add(allocation); db.SettlementEntries.Add(deduction); db.StatementAdjustments.Add(credit);
        db.AllocationReleases.AddRange(new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, StatementAdjustmentId = credit.Id, FundAllocationId = allocation.Id, Amount = 200, At = DateTimeOffset.UtcNow },
            new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = s.RentalAccountId, StatementAdjustmentId = credit.Id, SettlementEntryId = deduction.Id, Amount = 200, At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(); using var finance = await fixture.Login("finance@example.test");
        await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid()));
        Assert.Contains("outstanding VND 100.", (await db.InAppNotices.SingleAsync()).Message);
        Assert.Equal(1, await db.ChargeStatements.CountAsync()); Assert.Equal(1, await db.FundAllocations.CountAsync()); Assert.Equal(1, await db.SettlementEntries.CountAsync());
        Assert.Equal(2, await db.AllocationReleases.CountAsync()); Assert.Equal(1000m, (await db.ChargeStatements.SingleAsync()).Total);
    }
    private sealed class RejectReminderAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.State == EntityState.Added && x.Entity.Action == "ReceivableRemindersCreated")) throw new InvalidOperationException("Synthetic reminder audit fault");
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Operator_is_allowed_but_inactive_organization_missing_key_and_empty_range_are_refused()
    {
        using var op = await fixture.Login("operator@example.test");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await op.Send(HttpMethod.Post, PathFor(), Input())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await op.Send(HttpMethod.Post, PathFor(), new { startDate = Today, endDate = Today, page = 0 }, key: Guid.NewGuid())).StatusCode);
        await Result(await op.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid()));
        await using var db = fixture.Db(); var org = await db.CustomerOrganizations.SingleAsync(x => x.Id == DemoIdentitySeed.OrganizationA); org.Active = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await op.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid())).StatusCode);
    }
}
