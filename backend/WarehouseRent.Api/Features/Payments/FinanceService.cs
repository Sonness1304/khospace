using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Api.Features.Settlements;
namespace WarehouseRent.Api.Features.Payments;
public sealed record RecordReceipt(Guid RentalAccountId, string Amount, string Reference, string Method, DateTimeOffset ReceivedAt, string EvidenceReference, string Reason);
public sealed record FinanceAction(string Reason);
public sealed record AllocateFunds(string TargetType, Guid TargetId, string Amount, string Reason);
public sealed record IssueStatement(DateOnly PeriodStart, DateOnly DueDate, string Reason);
public sealed class VerifiedDepositReader(AppDbContext db) : IDepositVerification
{
    public async Task<DepositEvidence> ReadAsync(RentalAccount account, CancellationToken ct)
    {
        var held = await SettlementBalances.HeldAsync(db, account.Id, ct);
        var any = await db.PaymentReceipts.AnyAsync(x => x.RentalAccountId == account.Id && x.State == "Verified", ct);
        return new DepositEvidence(account.RequiredDeposit == 0 ? account.ZeroDepositVerifiedAt is not null : held >= account.RequiredDeposit, held, any);
    }
}
public sealed class FinanceService(AppDbContext db, BookingService bookings, CurrentActor actor, CommandExecutor commands)
{
    public static decimal Money(string amount)
    {
        if (!CatalogPolicy.Amount(amount, 0, 999999999999999999m, out var value)) throw new CommandFailure(422, "INVALID_AMOUNT", "Supply a positive whole-VND amount within supported precision.");
        return value;
    }
    public async Task<(RentalAccount Account, Booking Booking, DateTimeOffset Utc)> LockAccountAsync(Guid id, CancellationToken ct)
    {
        var known = await db.RentalAccounts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var locked = await bookings.LockAsync(known.BookingId, ct); return (locked.Account, locked.Booking, locked.Utc);
    }
    public static object Receipt(PaymentReceipt receipt) => new { resourceId = receipt.Id, receipt.State, version = Convert.ToBase64String(receipt.Version) };
    public async Task<object> RecordAsync(RecordReceipt input, HttpContext context, CancellationToken ct)
    {
        var (account, booking, utc) = await LockAccountAsync(input.RentalAccountId, ct);
        SettlementService.RequireOpen(account); account.UpdatedAt = utc;
        if (input.ReceivedAt > utc || input.ReceivedAt.Year < 2000) throw new CommandFailure(422, "INVALID_RECEIVED_DATE", "Record the actual received date, between2000 and current database time.");
        var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = account.Id, OrganizationId = account.OrganizationId, FacilityId = booking.FacilityId,
            Amount = Money(input.Amount), Reference = input.Reference.Trim().ToUpperInvariant(), Method = input.Method, ReceivedAt = input.ReceivedAt.ToUniversalTime(), EvidenceReference = input.EvidenceReference.Trim(), UpdatedAt = utc };
        db.PaymentReceipts.Add(receipt); commands.Audit(context, "ReceiptRecorded", receipt.Id, input.Reason, account.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(receipt);
    }
    private async Task<(PaymentReceipt Receipt, RentalAccount Account, Booking Booking, DateTimeOffset Utc)> LockReceiptAsync(Guid id, CancellationToken ct)
    {
        var known = await db.PaymentReceipts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var locked = await LockAccountAsync(known.RentalAccountId, ct);
        var receipt = await DatabaseDialect.Rows<PaymentReceipt>(db, $"SELECT * FROM [PaymentReceipts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        return (receipt, locked.Account, locked.Booking, locked.Utc);
    }
    public async Task<object> VerifyAsync(Guid id, FinanceAction input, HttpContext context, CancellationToken ct)
    {
        var (receipt, account, booking, utc) = await LockReceiptAsync(id, ct);
        SettlementService.RequireOpen(account);
        if (receipt.State == "Verified") return Receipt(receipt);
        if (receipt.State != "Recorded") throw new SpaceConflict("RECEIPT_NOT_RECORDED");
        CommandExecutor.Version(context, receipt.Version);
        receipt.State = "Verified"; receipt.VerifiedAt = utc; receipt.VerifiedBy = actor.UserId; receipt.UpdatedAt = utc;
        account.UpdatedAt = utc;
        if (account.State == "Cancelled" || BookingService.EffectiveState(booking, utc) is "Expired" or "Cancelled") account.RefundNeeded = true;
        commands.Audit(context, "ReceiptVerified", id, input.Reason, receipt.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(receipt);
    }
    public async Task<object> AllocateAsync(Guid id, AllocateFunds input, HttpContext context, CancellationToken ct)
    {
        var (receipt, account, booking, utc) = await LockReceiptAsync(id, ct);
        SettlementService.RequireOpen(account);
        CommandExecutor.Version(context, receipt.Version);
        if (receipt.State != "Verified") throw new SpaceConflict("RECEIPT_UNVERIFIED");
        var amount = Money(input.Amount);
        if (amount > await SettlementBalances.UnappliedAsync(db, id, ct)) throw new SpaceConflict("INSUFFICIENT_UNAPPLIED_FUNDS");
        Guid? statementId = null;
        if (input.TargetType == "Deposit")
        {
            if (input.TargetId != account.Id) throw new CommandFailure(404, "NOT_FOUND", "Target not found in this rental account.");
            if (account.State != "Open" || BookingService.EffectiveState(booking, utc) is not ("Held" or "Confirmed")) throw new SpaceConflict("DEPOSIT_TARGET_NOT_LIVE");
            var held = await SettlementBalances.HeldAsync(db, account.Id, ct);
            if (await db.SettlementEntries.AnyAsync(x => x.RentalAccountId == account.Id, ct)) throw new SpaceConflict("DEPOSIT_SETTLEMENT_STARTED");
            if (amount > account.RequiredDeposit - held) throw new SpaceConflict("DEPOSIT_TARGET_FULL");
        }
        else if (input.TargetType == "Charge")
        {
            var target = await DatabaseDialect.Rows<ChargeStatement>(db, $"SELECT * FROM [ChargeStatements] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={input.TargetId}").SingleOrDefaultAsync(ct);
            if (target is null || target.RentalAccountId != account.Id || target.OrganizationId != receipt.OrganizationId) throw new CommandFailure(404, "NOT_FOUND", "Target not found in this rental account.");
            var paid = await SettlementBalances.PaidAsync(db, target.Id, ct);
            if (amount > await SettlementBalances.NetChargeAsync(db, target.Id, ct) - paid) throw new SpaceConflict("CHARGE_TARGET_FULL");
            statementId = target.Id;
        }
        else throw new CommandFailure(422, "INVALID_FUND_TARGET", "Select Deposit or Charge.");
        var allocation = new FundAllocation { Id = Guid.NewGuid(), PaymentReceiptId = id, RentalAccountId = account.Id, OrganizationId = receipt.OrganizationId, TargetType = input.TargetType, ChargeStatementId = statementId,
            Amount = amount, At = utc, ActorId = actor.UserId };
        db.FundAllocations.Add(allocation);
        if (input.TargetType == "Deposit") db.DepositTransactions.Add(new DepositTransaction { Id = Guid.NewGuid(), RentalAccountId = account.Id, FundAllocationId = allocation.Id, Amount = amount, At = utc });
        receipt.UpdatedAt = utc;
        account.UpdatedAt = utc;
        commands.Audit(context, "FundsAllocated", id, input.Reason, receipt.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(receipt);
    }
    public async Task<object> ZeroDepositAsync(Guid id, FinanceAction input, HttpContext context, CancellationToken ct)
    {
        var (account, booking, utc) = await LockAccountAsync(id, ct); CommandExecutor.Version(context, account.Version);
        SettlementService.RequireOpen(account); account.UpdatedAt = utc;
        if (account.RequiredDeposit != 0 || BookingService.EffectiveState(booking, utc) != "Held" || account.State != "Open") throw new SpaceConflict("ZERO_DEPOSIT_NOT_APPLICABLE");
        account.ZeroDepositVerifiedAt = utc; account.ZeroDepositVerifiedBy = actor.UserId;
        commands.Audit(context, "ZeroDepositVerified", id, input.Reason, account.OrganizationId); await db.SaveChangesAsync(ct); return new { resourceId = id, version = Convert.ToBase64String(account.Version) };
    }
    public async Task<object> IssueAsync(Guid leaseId, IssueStatement input, HttpContext context, CancellationToken ct)
    {
        var known = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == leaseId, ct);
        var (account, _, utc) = await LockAccountAsync(known.RentalAccountId, ct);
        SettlementService.RequireOpen(account);
        var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={leaseId}").SingleAsync(ct);
        if (lease.ActivatedAt is null || lease.State == "Cancelled") throw new SpaceConflict("LEASE_NOT_ACTIVATED");
        if (input.PeriodStart.Year == 9999 || input.PeriodStart.Day != 1 || input.DueDate < input.PeriodStart || input.DueDate > input.PeriodStart.AddYears(1)) throw new CommandFailure(422, "INVALID_PERIOD", "Use a calendar-month start and an explicit due date within the following year.");
        var existing = await db.ChargeStatements.SingleOrDefaultAsync(x => x.LeaseId == leaseId && x.PeriodStart == input.PeriodStart, ct);
        if (existing is not null)
        {
            if (existing.DueDate != input.DueDate) throw new SpaceConflict("ISSUED_STATEMENT_IMMUTABLE");
            return new { resourceId = existing.Id };
        }
        var end = input.PeriodStart.AddMonths(1);
        var sources = await (from line in db.LeaseVersionLines.AsNoTracking() join version in db.LeaseVersions on line.LeaseVersionId equals version.Id
            where version.LeaseId == leaseId && line.StartDate >= input.PeriodStart && line.StartDate < end select line).ToListAsync(ct);
        if (sources.Count == 0) throw new CommandFailure(422, "NO_CHARGE_SEGMENTS", "No agreed charge segments exist for that month.");
        var statement = new ChargeStatement { Id = Guid.NewGuid(), LeaseId = leaseId, RentalAccountId = account.Id, OrganizationId = account.OrganizationId, FacilityId = lease.FacilityId,
            PeriodStart = input.PeriodStart, DueDate = input.DueDate, IssuedAt = utc, Total = sources.Sum(x => x.Amount) };
        db.ChargeStatements.Add(statement);
        account.UpdatedAt = utc;
        foreach (var source in sources) db.ChargeStatementLines.Add(new ChargeStatementLine { Id = Guid.NewGuid(), ChargeStatementId = statement.Id, SourceLeaseVersionLineId = source.Id,
            StartDate = source.StartDate, EndDate = source.EndDate, Days = source.Days, AreaM2 = source.AreaM2, DailyRate = source.DailyRate, BillingBasis = source.BillingBasis, MonthlyRate = source.MonthlyRate, CalendarDays = source.CalendarDays, Amount = source.Amount });
        commands.Audit(context, "StatementIssued", statement.Id, input.Reason, account.OrganizationId); await db.SaveChangesAsync(ct); return new { resourceId = statement.Id };
    }
    public async Task<object> RejectAsync(Guid id, FinanceAction input, HttpContext context, CancellationToken ct)
    {
        var (receipt, account, _, utc) = await LockReceiptAsync(id, ct); SettlementService.RequireOpen(account); CommandExecutor.Version(context, receipt.Version);
        if (receipt.State != "Recorded") throw new SpaceConflict("RECEIPT_NOT_RECORDED");
        receipt.State = "Rejected"; receipt.UpdatedAt = utc; account.UpdatedAt = utc;
        commands.Audit(context, "ReceiptRejected", id, input.Reason, account.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(receipt);
    }
}
