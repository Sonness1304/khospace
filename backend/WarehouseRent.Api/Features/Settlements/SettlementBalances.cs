using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Settlements;

public static class SettlementBalances
{
    public static async Task<decimal> NetChargeAsync(AppDbContext db, Guid statement, CancellationToken ct)
    {
        var total = await db.ChargeStatements.Where(x => x.Id == statement).Select(x => x.Total).SingleAsync(ct);
        var additions = await db.StatementAdjustments.Where(x => x.ChargeStatementId == statement && x.Kind != "Credit").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var credits = await db.StatementAdjustments.Where(x => x.ChargeStatementId == statement && x.Kind == "Credit").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return total + additions - credits;
    }
    public static async Task<decimal> PaidAsync(AppDbContext db, Guid statement, CancellationToken ct)
    {
        var paid = await db.FundAllocations.Where(x => x.ChargeStatementId == statement).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var deductions = await db.SettlementEntries.Where(x => x.ChargeStatementId == statement && x.Kind == "Deduction").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var released = await (from r in db.AllocationReleases join adjustment in db.StatementAdjustments on r.StatementAdjustmentId equals adjustment.Id where adjustment.ChargeStatementId == statement select (decimal?)r.Amount).SumAsync(ct) ?? 0;
        return paid + deductions - released;
    }
    public static async Task<decimal> HeldAsync(AppDbContext db, Guid account, CancellationToken ct)
    {
        var received = await db.DepositTransactions.Where(x => x.RentalAccountId == account).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var disposed = await db.SettlementEntries.Where(x => x.RentalAccountId == account && x.Kind != "RefundUnapplied").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var returned = await db.AllocationReleases.Where(x => x.RentalAccountId == account && x.SettlementEntryId != null).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return received - disposed + returned;
    }
    public static async Task<decimal> UnappliedAsync(AppDbContext db, Guid receipt, CancellationToken ct)
    {
        var received = await db.PaymentReceipts.Where(x => x.Id == receipt).Select(x => x.Amount).SingleAsync(ct);
        var allocated = await db.FundAllocations.Where(x => x.PaymentReceiptId == receipt).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var released = await (from release in db.AllocationReleases join allocation in db.FundAllocations on release.FundAllocationId equals allocation.Id where allocation.PaymentReceiptId == receipt select (decimal?)release.Amount).SumAsync(ct) ?? 0;
        var refunded = await db.SettlementEntries.Where(x => x.PaymentReceiptId == receipt && x.Kind == "RefundUnapplied").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return received - allocated + released - refunded;
    }
    public static async Task<decimal> AccountUnappliedAsync(AppDbContext db, Guid account, CancellationToken ct)
    {
        var verified = db.PaymentReceipts.Where(x => x.RentalAccountId == account && x.State == "Verified");
        var total = await verified.SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var ids = verified.Select(x => x.Id);
        var allocated = await db.FundAllocations.Where(x => ids.Contains(x.PaymentReceiptId)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var released = await db.AllocationReleases.Where(x => x.RentalAccountId == account && x.FundAllocationId != null).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        var refunded = await db.SettlementEntries.Where(x => x.RentalAccountId == account && x.Kind == "RefundUnapplied").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
        return total - allocated + released - refunded;
    }
}
