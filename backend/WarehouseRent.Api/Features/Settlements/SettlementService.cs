using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Leases;
using WarehouseRent.Api.Features.Payments;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Api.Features.Settlements;

public sealed record AdjustStatement(string Kind, string? Amount, string? Quantity, string? UnitPrice, string EvidenceReference, string Reason, bool Approved = false);
public sealed record RecordSettlement(string Kind, string Amount, Guid? ChargeStatementId, Guid? PaymentReceiptId, string Reference, string EvidenceReference, DateTimeOffset PerformedAt, string Reason, bool Confirmed = false);
public sealed record CloseAccount(string Reason, bool ClosureConfirmed = false);
public sealed record SupplementRent(string EvidenceReference, string Reason, bool Approved = false);
public sealed record SettlementSnapshot(decimal NetBilled, decimal Paid, decimal Held, decimal Unapplied, string[] Blockers);

public sealed class SettlementService(AppDbContext db, FinanceService finance, CurrentActor actor, CommandExecutor commands, IInventoryClearance inventory)
{
    public static void RequireOpen(RentalAccount account)
    {
        if (account.State == "Closed") throw new SpaceConflict("ACCOUNT_CLOSED");
    }
    public static object AdjustmentReceipt(StatementAdjustment item) => new { resourceId = item.Id, item.ChargeStatementId, item.Kind, amount = CatalogPolicy.Money(item.Amount) };
    private async Task<(RentalAccount Account, Booking Booking, Lease? Lease, DateTimeOffset Utc)> LockAsync(Guid accountId, CancellationToken ct)
    {
        var locked = await finance.LockAccountAsync(accountId, ct);
        var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [RentalAccountId]={accountId}").SingleOrDefaultAsync(ct);
        return (locked.Account, locked.Booking, lease, locked.Utc);
    }
    public async Task<object> AdjustAsync(Guid statementId, AdjustStatement input, HttpContext context, CancellationToken ct)
    {
        var known = await db.ChargeStatements.AsNoTracking().SingleAsync(x => x.Id == statementId, ct);
        var locked = await LockAsync(known.RentalAccountId, ct); RequireOpen(locked.Account);
        if (locked.Lease?.ActivatedAt is null) throw new SpaceConflict("LEASE_NOT_ACTIVATED");
        decimal? quantity = null; decimal? price = null; decimal amount;
        if (input.Kind == "Service")
        {
            if (!CatalogPolicy.Amount(input.Quantity!, 3, 999999999999999.999m, out var q) || !CatalogPolicy.Amount(input.UnitPrice!, 4, 99999999999999.9999m, out var p)) throw new CommandFailure(422, "INVALID_SERVICE_VALUES", "Supply positive quantity and unit price snapshots.");
            quantity = q; price = p;
            if (q > 999999999999999999m / p) throw new CommandFailure(422, "AMOUNT_LIMIT", "Service amount exceeds supported precision.");
            amount = decimal.Round(q * p, 0, MidpointRounding.AwayFromZero); if (amount <= 0) throw new CommandFailure(422, "INVALID_AMOUNT", "The rounded service amount must be positive.");
        }
        else if (input.Kind is "Credit" or "Supplement") amount = FinanceService.Money(input.Amount!);
        else throw new CommandFailure(422, "INVALID_ADJUSTMENT_KIND", "Select Credit, Supplement or Service.");
        var net = await SettlementBalances.NetChargeAsync(db, statementId, ct);
        if (input.Kind == "Credit" && amount > net) throw new SpaceConflict("CREDIT_EXCEEDS_CHARGE");
        if (input.Kind != "Credit" && net > 999999999999999999m - amount) throw new CommandFailure(422, "AMOUNT_LIMIT", "Net statement amount exceeds supported precision.");
        var adjustment = new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = statementId, RentalAccountId = known.RentalAccountId, Kind = input.Kind, Amount = amount, Quantity = quantity, UnitPrice = price,
            EvidenceReference = input.EvidenceReference.Trim(), Reason = input.Reason.Trim(), ActorId = actor.UserId, At = locked.Utc };
        if (input.Kind == "Credit") await ReleaseExcessAsync(adjustment, Math.Max(0, await SettlementBalances.PaidAsync(db, statementId, ct) - (net - amount)), locked.Utc, ct);
        db.StatementAdjustments.Add(adjustment); locked.Account.UpdatedAt = locked.Utc;
        commands.Audit(context, "StatementAdjusted", adjustment.Id, input.Reason, locked.Account.OrganizationId); await db.SaveChangesAsync(ct); return AdjustmentReceipt(adjustment);
    }
    private async Task ReleaseExcessAsync(StatementAdjustment adjustment, decimal excess, DateTimeOffset utc, CancellationToken ct)
    {
        foreach (var allocation in await db.FundAllocations.AsNoTracking().Where(x => x.ChargeStatementId == adjustment.ChargeStatementId).OrderByDescending(x => x.At).ThenBy(x => x.Id).ToListAsync(ct))
        {
            if (excess <= 0) break;
            var released = await db.AllocationReleases.Where(x => x.FundAllocationId == allocation.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var amount = Math.Min(excess, allocation.Amount - released); if (amount <= 0) continue;
            db.AllocationReleases.Add(new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = adjustment.RentalAccountId, StatementAdjustmentId = adjustment.Id, FundAllocationId = allocation.Id, Amount = amount, At = utc }); excess -= amount;
        }
        foreach (var deduction in await db.SettlementEntries.AsNoTracking().Where(x => x.ChargeStatementId == adjustment.ChargeStatementId && x.Kind == "Deduction").OrderByDescending(x => x.At).ThenBy(x => x.Id).ToListAsync(ct))
        {
            if (excess <= 0) break;
            var released = await db.AllocationReleases.Where(x => x.SettlementEntryId == deduction.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var amount = Math.Min(excess, deduction.Amount - released); if (amount <= 0) continue;
            db.AllocationReleases.Add(new AllocationRelease { Id = Guid.NewGuid(), RentalAccountId = adjustment.RentalAccountId, StatementAdjustmentId = adjustment.Id, SettlementEntryId = deduction.Id, Amount = amount, At = utc }); excess -= amount;
        }
        if (excess > 0) throw new SpaceConflict("PAYMENT_RECONCILIATION_REQUIRED");
    }
    public async Task<object> SupplementAsync(Guid statementId, SupplementRent input, HttpContext context, CancellationToken ct)
    {
        var statement = await db.ChargeStatements.AsNoTracking().SingleAsync(x => x.Id == statementId, ct);
        var locked = await LockAsync(statement.RentalAccountId, ct); RequireOpen(locked.Account);
        if (locked.Lease?.ActivatedAt is null) throw new SpaceConflict("LEASE_NOT_ACTIVATED");
        var end = statement.PeriodStart.AddMonths(1);
        var sources = await (from line in db.LeaseVersionLines.AsNoTracking() join version in db.LeaseVersions on line.LeaseVersionId equals version.Id
            where version.LeaseId == statement.LeaseId && line.StartDate >= statement.PeriodStart && line.StartDate < end
                && !db.ChargeStatementLines.Any(x => x.SourceLeaseVersionLineId == line.Id) && !db.StatementAdjustments.Any(x => x.SourceLeaseVersionLineId == line.Id) select line).ToListAsync(ct);
        var net = await SettlementBalances.NetChargeAsync(db, statementId, ct);
        foreach (var source in sources)
        {
            if (net > 999999999999999999m - source.Amount) throw new CommandFailure(422, "AMOUNT_LIMIT", "Net statement amount exceeds supported precision.");
            net += source.Amount;
            db.StatementAdjustments.Add(new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = statementId, RentalAccountId = statement.RentalAccountId, SourceLeaseVersionLineId = source.Id,
                Kind = "RentSupplement", Amount = source.Amount, EvidenceReference = input.EvidenceReference.Trim(), Reason = input.Reason.Trim(), ActorId = actor.UserId, At = locked.Utc });
        }
        if (sources.Count > 0) { locked.Account.UpdatedAt = locked.Utc; commands.Audit(context, "RentalSupplementIssued", statementId, input.Reason, locked.Account.OrganizationId); await db.SaveChangesAsync(ct); }
        return new { resourceId = statementId, addedSegments = sources.Count };
    }
    private static bool Ended(Booking booking, Lease? lease, DateTimeOffset utc) => lease is { State: "SettlementPending", ClearedAt: not null }
        || lease?.ActivatedAt is null && BookingService.EffectiveState(booking, utc) is "Cancelled" or "Expired";
    public async Task<object> EntryAsync(Guid accountId, RecordSettlement input, HttpContext context, CancellationToken ct)
    {
        var locked = await LockAsync(accountId, ct); RequireOpen(locked.Account);
        if (!Ended(locked.Booking, locked.Lease, locked.Utc)) throw new SpaceConflict("SETTLEMENT_CUSTODY_NOT_READY");
        if (input.PerformedAt.Year < 2000 || input.PerformedAt > locked.Utc) throw new CommandFailure(422, "INVALID_PERFORMED_DATE", "Use the actual performed date between 2000 and database time.");
        var amount = FinanceService.Money(input.Amount);
        if (input.Kind == "Deduction")
        {
            if (locked.Lease?.ActivatedAt is null || input.ChargeStatementId is null || input.PaymentReceiptId is not null) throw new CommandFailure(422, "INVALID_SETTLEMENT_TARGET", "Deduction requires an activated lease and an issued statement.");
            if (!await db.ChargeStatements.AnyAsync(x => x.Id == input.ChargeStatementId && x.RentalAccountId == accountId, ct)) throw new CommandFailure(404, "NOT_FOUND", "Statement not found in this account.");
            if (amount > await SettlementBalances.NetChargeAsync(db, input.ChargeStatementId.Value, ct) - await SettlementBalances.PaidAsync(db, input.ChargeStatementId.Value, ct)) throw new SpaceConflict("DEDUCTION_EXCEEDS_OUTSTANDING");
            if (amount > await SettlementBalances.HeldAsync(db, accountId, ct)) throw new SpaceConflict("INSUFFICIENT_DEPOSIT");
        }
        else if (input.Kind == "RefundDeposit")
        {
            if (input.ChargeStatementId != null || input.PaymentReceiptId != null) throw new CommandFailure(422, "INVALID_SETTLEMENT_TARGET", "Deposit refund targets security only.");
            if (amount > await SettlementBalances.HeldAsync(db, accountId, ct)) throw new SpaceConflict("INSUFFICIENT_DEPOSIT");
        }
        else if (input.Kind == "RefundUnapplied")
        {
            if (input.PaymentReceiptId is null || input.ChargeStatementId is not null) throw new CommandFailure(422, "INVALID_SETTLEMENT_TARGET", "Select one verified unapplied receipt.");
            var receipt = await db.PaymentReceipts.SingleOrDefaultAsync(x => x.Id == input.PaymentReceiptId && x.RentalAccountId == accountId, ct);
            if (receipt is null) throw new CommandFailure(404, "NOT_FOUND", "Receipt not found in this account.");
            if (receipt.State != "Verified" || amount > await SettlementBalances.UnappliedAsync(db, receipt.Id, ct)) throw new SpaceConflict("INSUFFICIENT_UNAPPLIED_FUNDS");
        }
        else throw new CommandFailure(422, "INVALID_SETTLEMENT_KIND", "Select Deduction, RefundDeposit or RefundUnapplied.");
        var entry = new SettlementEntry { Id = Guid.NewGuid(), RentalAccountId = accountId, OrganizationId = locked.Account.OrganizationId, ChargeStatementId = input.ChargeStatementId, PaymentReceiptId = input.PaymentReceiptId,
            Kind = input.Kind, Amount = amount, Reference = input.Reference.Trim().ToUpperInvariant(), EvidenceReference = input.EvidenceReference.Trim(), Reason = input.Reason.Trim(), ActorId = actor.UserId, At = locked.Utc, PerformedAt = input.PerformedAt.ToUniversalTime() };
        db.SettlementEntries.Add(entry); locked.Account.UpdatedAt = locked.Utc; commands.Audit(context, "SettlementRecorded", entry.Id, input.Reason, locked.Account.OrganizationId); await db.SaveChangesAsync(ct);
        return new { resourceId = entry.Id, entry.Kind, amount = CatalogPolicy.Money(entry.Amount) };
    }
    public async Task<SettlementSnapshot> SnapshotAsync(RentalAccount account, Booking booking, Lease? lease, DateTimeOffset utc, CancellationToken ct)
    {
        var blockers = new List<string>(); decimal billed = 0; decimal paid = 0;
        foreach (var statement in await db.ChargeStatements.Where(x => x.RentalAccountId == account.Id).Select(x => x.Id).ToListAsync(ct)) { billed += await SettlementBalances.NetChargeAsync(db, statement, ct); paid += await SettlementBalances.PaidAsync(db, statement, ct); }
        var held = await SettlementBalances.HeldAsync(db, account.Id, ct); var unapplied = await SettlementBalances.AccountUnappliedAsync(db, account.Id, ct);
        if (!Ended(booking, lease, utc)) blockers.Add("PHYSICAL_CLEARANCE_REQUIRED");
        if (lease is not null)
        {
            var grants = await db.LeaseUnits.Where(x => x.LeaseId == lease.Id).Select(x => x.Id).ToArrayAsync(ct); var evidence = await inventory.ReadAsync(lease.Id, grants, ct);
            if (!evidence.Available || !evidence.ZeroStock || !evidence.NoPendingRequests || await db.UnitOccupancies.AnyAsync(x => grants.Contains(x.LeaseUnitId) && x.ClearedAt == null, ct)) blockers.Add("INVENTORY_CLEARANCE_REQUIRED");
            if (lease.ActivatedAt != null && await (from line in db.LeaseVersionLines join version in db.LeaseVersions on line.LeaseVersionId equals version.Id where version.LeaseId == lease.Id
                && !db.ChargeStatementLines.Any(x => x.SourceLeaseVersionLineId == line.Id) && !db.StatementAdjustments.Any(x => x.SourceLeaseVersionLineId == line.Id) select line.Id).AnyAsync(ct)) blockers.Add("UNBILLED_RENTAL_SEGMENTS");
        }
        if (billed != paid) blockers.Add("OUTSTANDING_CHARGES"); if (held != 0) blockers.Add("DEPOSIT_NOT_SETTLED"); if (unapplied != 0) blockers.Add("UNAPPLIED_FUNDS");
        if (await db.PaymentReceipts.AnyAsync(x => x.RentalAccountId == account.Id && x.State == "Recorded", ct)) blockers.Add("UNVERIFIED_RECEIPTS");
        if (await db.PaymentIntents.AnyAsync(x => x.RentalAccountId == account.Id && (x.State == "Created" || x.State == "Creating" || x.State == "Ready" || x.State == "Unknown" || x.State == "ReviewRequired"), ct)) blockers.Add("UNRESOLVED_ONLINE_PAYMENTS");
        return new(billed, paid, held, unapplied, blockers.ToArray());
    }
    public async Task<object> ReadAsync(Guid id, CancellationToken ct)
    {
        var locked = await LockAsync(id, ct); var snapshot = await SnapshotAsync(locked.Account, locked.Booking, locked.Lease, locked.Utc, ct);
        return new { locked.Account.Id, locked.Account.OrganizationId, locked.Booking.FacilityId, leaseId = locked.Lease?.Id, state = locked.Account.State, locked.Account.ClosedAt, locked.Account.ClosedBy, locked.Account.ClosureReason,
            version = Convert.ToBase64String(locked.Account.Version), canClose = locked.Account.State != "Closed" && snapshot.Blockers.Length == 0, blockers = snapshot.Blockers,
            netBilled = CatalogPolicy.Money(snapshot.NetBilled), rentalPaid = CatalogPolicy.Money(snapshot.Paid), outstanding = CatalogPolicy.Money(snapshot.NetBilled - snapshot.Paid), depositHeld = CatalogPolicy.Money(snapshot.Held), unappliedVerified = CatalogPolicy.Money(snapshot.Unapplied), observedAt = locked.Utc };
    }
    public async Task<object> CloseAsync(Guid id, CloseAccount input, HttpContext context, CancellationToken ct)
    {
        var locked = await LockAsync(id, ct); RequireOpen(locked.Account); CommandExecutor.Version(context, locked.Account.Version);
        var snapshot = await SnapshotAsync(locked.Account, locked.Booking, locked.Lease, locked.Utc, ct); if (snapshot.Blockers.Length > 0) throw new SpaceConflict(snapshot.Blockers[0]);
        locked.Account.State = "Closed"; locked.Account.ClosedAt = locked.Utc; locked.Account.ClosedBy = actor.UserId; locked.Account.ClosureReason = input.Reason.Trim(); locked.Account.UpdatedAt = locked.Utc; locked.Account.RefundNeeded = false;
        if (locked.Lease is { ActivatedAt: not null }) { locked.Lease.State = "Closed"; locked.Lease.ClosedAt = locked.Utc; }
        var lines = db.BookingLines.Where(x => x.BookingId == locked.Booking.Id).Select(x => x.Id);
        var today = CatalogPolicy.Today(locked.Utc);
        foreach (var allocation in await db.SpaceAllocations.Where(x => x.BookingLineId != null && lines.Contains(x.BookingLineId.Value) && (x.State == "Confirmed" || x.State == "Held") && x.EndDate > today).ToListAsync(ct))
        {
            // Keep the confirmed historical prefix; release only the remaining schedule.
            if (allocation.State == "Confirmed" && allocation.StartDate < today) allocation.EndDate = today;
            else allocation.State = "Released";
        }
        commands.Audit(context, "RentalAccountClosed", id, input.Reason, locked.Account.OrganizationId); await db.SaveChangesAsync(ct);
        return new { resourceId = id, locked.Account.State, locked.Account.ClosedAt, version = Convert.ToBase64String(locked.Account.Version) };
    }
}
