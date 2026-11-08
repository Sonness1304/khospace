using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Api.Features.Settlements;
namespace WarehouseRent.Api.Features.Payments;
public static class FinanceEndpoints
{
    public static void MapFinance(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/receipts/{id:guid}/reject", async (Guid id, FinanceAction input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var item = await db.PaymentReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(item.OrganizationId, item.FacilityId, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, item.OrganizationId, "RejectReceipt", new { id, input }, token => finance.RejectAsync(id, input, context, token), ct, organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "Finance");
        });
        api.MapPost("/receipts", async (RecordReceipt input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var scope = await AccountScope(input.RentalAccountId, db, ct);
            if (scope is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reference, 120) || !CatalogPolicy.Text(input.EvidenceReference, 240) || !CatalogPolicy.Text(input.Reason, 500) || input.Method is not ("Cash" or "BankTransfer" or "Other")) return Invalid();
            return await commands.ExecuteAsync(context, scope.Value.Org, "RecordReceipt", input, token => finance.RecordAsync(input, context, token), ct, organizationId: scope.Value.Org, facilityId: scope.Value.Facility, facilityRole: "Finance");
        });
        api.MapPost("/receipts/{id:guid}/verify", async (Guid id, FinanceAction input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var item = await db.PaymentReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(item.OrganizationId, item.FacilityId, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, item.OrganizationId, "VerifyReceipt", new { id, input }, token => finance.VerifyAsync(id, input, context, token), ct, organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "Finance");
        });
        api.MapPost("/receipts/{id:guid}/allocations", async (Guid id, AllocateFunds input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var item = await db.PaymentReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (item is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(item.OrganizationId, item.FacilityId, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, item.OrganizationId, "AllocateFunds", new { id, input }, token => finance.AllocateAsync(id, input, context, token), ct, organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "Finance");
        });
        api.MapGet("/receipts/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, FinanceService finance, CancellationToken ct) => {
            var item = await db.PaymentReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || !await CanRead(item.OrganizationId, item.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            await using var tx = await db.Database.BeginTransactionAsync(ct); await finance.LockAccountAsync(item.RentalAccountId, ct);
            item = await db.PaymentReceipts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
            var unapplied = await SettlementBalances.UnappliedAsync(db, id, ct);
            var allocated = await db.FundAllocations.Where(x => x.PaymentReceiptId == id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var allocations = await db.FundAllocations.AsNoTracking().Where(x => x.PaymentReceiptId == id).OrderBy(x => x.At).ThenBy(x => x.Id).Take(100).Select(x => new { x.Id, x.TargetType, x.RentalAccountId, x.ChargeStatementId, amount = CatalogPolicy.Money(x.Amount), x.At }).ToListAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { item.Id, item.RentalAccountId, item.OrganizationId, item.FacilityId, item.Reference, item.Method, amount = CatalogPolicy.Money(item.Amount), item.ReceivedAt, item.State, item.VerifiedAt, item.VerifiedBy,
                allocated = CatalogPolicy.Money(allocated), unapplied = CatalogPolicy.Money(unapplied), version = Convert.ToBase64String(item.Version), allocations, allocationsTruncated = allocations.Count == 100 });
        });
        api.MapGet("/rental-accounts/{id:guid}", async (Guid id, int? receiptsPage, int? statementsPage, AppDbContext db, CurrentActor actor, FinanceService finance, CancellationToken ct) => {
            var account = await db.RentalAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (account is null) return OrganizationEndpoints.Missing();
            var booking = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == account.BookingId, ct);
            if (!await CanRead(account.OrganizationId, booking.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var locked = await finance.LockAccountAsync(id, ct); account = locked.Account; booking = locked.Booking;
            // Writers use these same unit/account parents; every total below observes one committed account version.
            var held = await SettlementBalances.HeldAsync(db, id, ct);
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.RentalAccountId == id, ct);
            var receiptQuery = db.PaymentReceipts.AsNoTracking().Where(x => x.RentalAccountId == id); var chargeQuery = db.ChargeStatements.AsNoTracking().Where(x => x.RentalAccountId == id);
            var rp = Math.Clamp(receiptsPage ?? 1, 1, 10000); var sp = Math.Clamp(statementsPage ?? 1, 1, 10000);
            var receipts = await receiptQuery.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id).Skip((rp - 1) * 50).Take(50).Select(x => new { x.Id, x.State, x.Reference, amount = CatalogPolicy.Money(x.Amount) }).ToListAsync(ct);
            var statements = await chargeQuery.OrderBy(x => x.PeriodStart).ThenBy(x => x.Id).Skip((sp - 1) * 50).Take(50).Select(x => new { x.Id, x.LeaseId, x.PeriodStart, x.DueDate, total = CatalogPolicy.Money(x.Total) }).ToListAsync(ct);
            decimal billed = 0; decimal paid = 0;
            foreach (var statementId in await chargeQuery.Select(x => x.Id).ToListAsync(ct)) { billed += await SettlementBalances.NetChargeAsync(db, statementId, ct); paid += await SettlementBalances.PaidAsync(db, statementId, ct); }
            var verifiedFunds = await receiptQuery.Where(x => x.State == "Verified").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var unapplied = await SettlementBalances.AccountUnappliedAsync(db, id, ct);
            var receiptTotal = await receiptQuery.CountAsync(ct); var statementTotal = await chargeQuery.CountAsync(ct);
            var refundNeeded = account.State != "Closed" && (account.RefundNeeded || verifiedFunds > 0 && WarehouseRent.Api.Features.Bookings.BookingService.EffectiveState(booking, locked.Utc) == "Expired");
            await transaction.CommitAsync(ct);
            return Results.Ok(new { account.Id, account.BookingId, leaseId = lease?.Id, leaseActivatedAt = lease?.ActivatedAt, account.OrganizationId, booking.FacilityId, account.State, refundNeeded, requiredDeposit = CatalogPolicy.Money(account.RequiredDeposit), depositHeld = CatalogPolicy.Money(held),
                account.ZeroDepositVerifiedAt, version = Convert.ToBase64String(account.Version), billed = CatalogPolicy.Money(billed), rentalPaid = CatalogPolicy.Money(paid), outstanding = CatalogPolicy.Money(billed - paid), unappliedVerified = CatalogPolicy.Money(unapplied), receipts, statements,
                receiptsPage = rp, statementsPage = sp, receiptTotal, statementTotal });
        });
        api.MapPost("/rental-accounts/{id:guid}/verify-zero-deposit", async (Guid id, FinanceAction input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var scope = await AccountScope(id, db, ct); if (scope is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, scope.Value.Org, "VerifyZeroDeposit", new { id, input }, token => finance.ZeroDepositAsync(id, input, context, token), ct, organizationId: scope.Value.Org, facilityId: scope.Value.Facility, facilityRole: "Finance");
        });
        api.MapPost("/leases/{id:guid}/statements", async (Guid id, IssueStatement input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (lease is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(lease.OrganizationId, lease.FacilityId, actor, ct); if (denied is not null) return denied;
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, lease.OrganizationId, "IssueStatement", new { id, input }, token => finance.IssueAsync(id, input, context, token), ct, organizationId: lease.OrganizationId, facilityId: lease.FacilityId, facilityRole: "Finance");
        });
        api.MapGet("/statements/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, FinanceService finance, CancellationToken ct) => {
            var item = await db.ChargeStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || !await CanRead(item.OrganizationId, item.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            await using var tx = await db.Database.BeginTransactionAsync(ct); await finance.LockAccountAsync(item.RentalAccountId, ct);
            var paid = await SettlementBalances.PaidAsync(db, id, ct); var net = await SettlementBalances.NetChargeAsync(db, id, ct);
            var utc = await DatabaseDialect.UtcNowAsync(db, ct);
            var lines = await db.ChargeStatementLines.AsNoTracking().Where(x => x.ChargeStatementId == id).OrderBy(x => x.StartDate).ThenBy(x => x.Id).Select(x => new { x.Id, x.SourceLeaseVersionLineId, x.StartDate, x.EndDate, x.Days,
                areaM2 = CatalogPolicy.Money(x.AreaM2), dailyRate = CatalogPolicy.Money(x.DailyRate), x.BillingBasis, monthlyRate = x.MonthlyRate.HasValue ? CatalogPolicy.Money(x.MonthlyRate.Value) : null, x.CalendarDays, amount = CatalogPolicy.Money(x.Amount) }).ToListAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { item.Id, item.LeaseId, item.RentalAccountId, item.OrganizationId, item.FacilityId, item.PeriodStart, item.DueDate, item.IssuedAt, total = CatalogPolicy.Money(item.Total), netTotal = CatalogPolicy.Money(net), paid = CatalogPolicy.Money(paid), outstanding = CatalogPolicy.Money(net - paid),
                paymentState = paid >= net ? "Paid" : paid > 0 ? "PartPaid" : "Unpaid", overdue = net > paid && item.DueDate < CatalogPolicy.Today(utc), observedAt = utc, lines });
        });
    }
    private static async Task<(Guid Org, Guid Facility)?> AccountScope(Guid id, AppDbContext db, CancellationToken ct)
    {
        var account = await db.RentalAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (account is null) return null;
        var facility = await db.Bookings.Where(x => x.Id == account.BookingId).Select(x => x.FacilityId).SingleAsync(ct); return (account.OrganizationId, facility);
    }
    private static async Task<bool> CanRead(Guid org, Guid facility, CurrentActor actor, CancellationToken ct) => await actor.MembershipAsync(org, ct) is not null || await actor.HasFacilityAsync(facility, "Finance", ct);
    private static async Task<IResult?> Denied(Guid org, Guid facility, CurrentActor actor, CancellationToken ct)
    {
        if (await actor.HasFacilityAsync(facility, "Finance", ct)) return null;
        return await actor.MembershipAsync(org, ct) is not null || await actor.HasFacilityAsync(facility, null, ct) ? OrganizationEndpoints.Forbidden() : OrganizationEndpoints.Missing();
    }
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_FINANCE_INPUT", "Check reference, method, evidence and reason.");
}
