using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Settlements;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.Recovery;

// Rehearsal-only reader; intentionally has no HTTP endpoint or backup/restore permissions.
public static class RecoverySnapshot
{
    public static async Task<string> Capture(AppDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var ledger = await db.StockLedger.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StockPostingId, x.OrganizationId, x.ItemId, x.LeaseUnitId, x.Delta }).ToListAsync(ct);
        var balances = await db.StockBalances.AsNoTracking().OrderBy(x => x.OrganizationId).ThenBy(x => x.ItemId).ThenBy(x => x.LeaseUnitId).ToListAsync(ct);
        var expected = ledger.GroupBy(x => (x.OrganizationId, x.ItemId, x.LeaseUnitId)).ToDictionary(x => x.Key, x => x.Sum(l => l.Delta));
        foreach (var balance in balances)
        {
            if (balance.Quantity < 0 || balance.Quantity != expected.GetValueOrDefault((balance.OrganizationId, balance.ItemId, balance.LeaseUnitId))) throw new InvalidOperationException("Stock reconciliation failed.");
            expected.Remove((balance.OrganizationId, balance.ItemId, balance.LeaseUnitId));
        }
        if (expected.Any(x => x.Value != 0)) throw new InvalidOperationException("A stock balance is missing.");
        var allocations = await db.SpaceAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var occupancies = await db.UnitOccupancies.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var leases = await db.Leases.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var accounts = await db.RentalAccounts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var funds = new List<object>();
        foreach (var account in accounts)
        {
            var held = await SettlementBalances.HeldAsync(db, account.Id, ct); var unapplied = await SettlementBalances.AccountUnappliedAsync(db, account.Id, ct);
            if (held < 0 || unapplied < 0) throw new InvalidOperationException("Fund reconciliation failed.");
            funds.Add(new { account.Id, account.State, held, unapplied });
        }
        var statements = await db.ChargeStatements.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var charges = new List<object>();
        foreach (var statement in statements)
        { var charge = await SettlementBalances.NetChargeAsync(db, statement.Id, ct); var paid = await SettlementBalances.PaidAsync(db, statement.Id, ct); if (charge < paid || paid < 0) throw new InvalidOperationException("Statement reconciliation failed."); charges.Add(new { statement.Id, charge, paid }); }
        var docs = await db.PrivateDocuments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var doc in docs) if (doc.Sha256 != Convert.ToHexString(SHA256.HashData(doc.Content))) throw new InvalidOperationException("Private content reconciliation failed.");
        var users = await db.Users.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Active, x.OperatorAdmin, x.SecurityStamp }).ToListAsync(ct);
        var memberships = await db.Memberships.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var staff = await db.StaffFacilityScopes.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.FacilityId).ThenBy(x => x.StaffRole).ToListAsync(ct);
        var notices = await db.NoticeOutbox.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var issuedNotices = await db.InAppNotices.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var receipts = await db.PaymentReceipts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var moneyAllocations = await db.FundAllocations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var deposits = await db.DepositTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var adjustments = await db.StatementAdjustments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var settlements = await db.SettlementEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var releases = await db.AllocationReleases.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
        var barcodes = await db.ItemBarcodes.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OrganizationId, x.ItemId, x.Kind, x.CanonicalValue, x.Active, x.CreatedBy, x.CreatedAt, x.UpdatedAt }).ToListAsync(ct);
        var onlinePayments = await db.PaymentIntents.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RentalAccountId, x.OrganizationId, x.FacilityId, x.Provider, x.Mode, x.MerchantId, x.Method, x.Amount, x.CreatedBy, x.CreatedAt, x.UpdatedAt, x.State, x.TransactionId, x.PaymentReceiptId, x.PaidAt, x.BankTransferReference, x.ExpiresAt }).ToListAsync(ct);
        var gateLanes = await db.GateLanes.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.FacilityId, x.Code, x.Active, x.CreatedBy, x.CreatedAt, x.UpdatedAt }).ToListAsync(ct);
        var gateVisits = await db.GateVisits.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.LaneId, x.FacilityId, x.OrganizationId, x.LeaseId, x.Direction, x.Vehicle, x.StartUtc, x.EndUtc, x.State, x.CreatedBy, x.CreatedAt, x.UpdatedAt, x.ArrivedBy, x.ArrivedAt, x.DepartedBy, x.DepartedAt, x.CancelledBy, x.CancelledAt }).ToListAsync(ct);
        var rates = await db.RateVersions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new { x.Id,x.UnitId,x.SalesValidFrom,x.SalesValidTo,x.BillingBasis,x.DailyRate,x.MonthlyRate,x.Currency }).ToListAsync(ct);
        var quotes = await db.Quotes.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new { x.Id,x.OrganizationId,x.FacilityId,x.StartDate,x.EndDate,x.PolicyVersion,x.RentalSubtotal,x.RequiredDeposit }).ToListAsync(ct);
        var quoteLines = await db.QuoteLines.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new { x.Id,x.QuoteId,x.UnitId,x.RateVersionId,x.StartDate,x.EndDate,x.Days,x.AreaM2,x.DailyRate,x.BillingBasis,x.MonthlyRate,x.CalendarDays,x.Amount }).ToListAsync(ct);
        var leaseVersions = await db.LeaseVersions.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(ct);
        var leasePrices = await db.LeaseVersionLines.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(ct);
        var statementPrices = await db.ChargeStatementLines.AsNoTracking().OrderBy(x=>x.Id).ToListAsync(ct);
        var value = JsonSerializer.Serialize(new { rates,quotes,quoteLines,leaseVersions,leasePrices,statementPrices,gateLanes, gateVisits, onlinePayments, barcodes, receipts, moneyAllocations, deposits, statements, adjustments, settlements, releases, ledger, balances, allocations, occupancies, leases, accounts, funds, charges, users, memberships, staff, notices, issuedNotices,
            documents = docs.Select(x => new { x.Id, x.LeaseId, x.OrganizationId, x.FacilityId, x.StorageKey, x.FileName, x.Sha256, x.RevokedAt }) });
        await tx.CommitAsync(ct); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
