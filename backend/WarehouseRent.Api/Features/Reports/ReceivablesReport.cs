using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Reports;

public sealed record ReceivableRow(Guid StatementId, Guid LeaseId, Guid RentalAccountId, DateOnly PeriodStart, DateOnly DueDate,
    string OriginalVnd, string AdditionsVnd, string CreditsVnd, string AllocationsVnd, string DeductionsVnd, string ReleasesVnd,
    string EffectiveChargeVnd, string EffectivePaidVnd, string OutstandingVnd, int DaysOverdue, string Bucket);
public sealed record ReceivableSummary(int MatchedStatements, int OpenStatements, string EffectiveChargeVnd, string EffectivePaidVnd,
    string OutstandingVnd, string CurrentVnd, string Overdue1To30Vnd, string Overdue31To60Vnd, string Overdue61To90Vnd, string OverdueOver90Vnd);
public sealed record ReceivableSnapshot(DateTimeOffset ObservedAt, DateOnly AsOfDate, string TimeZone, string OrganizationName,
    ReceivableSummary Summary, IReadOnlyList<ReceivableRow> Rows);

public static class ReceivablesReport
{
    public const int Limit = 5000;
    public static string Money(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);
    public static string Bucket(DateOnly due, DateOnly today, decimal outstanding)
    {
        if (outstanding == 0) return "Settled";
        var days = today.DayNumber - due.DayNumber;
        return days <= 0 ? "Current" : days <= 30 ? "Overdue1To30" : days <= 60 ? "Overdue31To60" : days <= 90 ? "Overdue61To90" : "OverdueOver90";
    }
    // Caller owns the serializable transaction spanning authorization, these reads and any export audit.
    public static async Task<ReceivableSnapshot?> ReadAsync(AppDbContext db, Guid organization, Guid facility,
        DateOnly start, DateOnly end, CancellationToken ct)
    {
        var utc = await DatabaseDialect.UtcNowAsync(db, ct);
        var zoneId = await db.Facilities.Where(x => x.Id == facility).Select(x => x.TimeZoneId).SingleAsync(ct);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime);
        var name = await db.CustomerOrganizations.Where(x => x.Id == organization).Select(x => x.Name).SingleAsync(ct);
        var statements = await db.ChargeStatements.AsNoTracking().Where(x => x.OrganizationId == organization && x.FacilityId == facility && x.DueDate >= start && x.DueDate < end)
            .OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(ct);
        if (statements.Count > Limit) return null;
        var ids = statements.Select(x => x.Id).ToArray();
        var adjustments = await db.StatementAdjustments.AsNoTracking().Where(x => ids.Contains(x.ChargeStatementId))
            .GroupBy(x => new { x.ChargeStatementId, x.Kind }).Select(g => new { g.Key.ChargeStatementId, g.Key.Kind, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var allocations = await db.FundAllocations.AsNoTracking().Where(x => x.ChargeStatementId != null && ids.Contains(x.ChargeStatementId.Value))
            .GroupBy(x => x.ChargeStatementId!.Value).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var deductions = await db.SettlementEntries.AsNoTracking().Where(x => x.Kind == "Deduction" && x.ChargeStatementId != null && ids.Contains(x.ChargeStatementId.Value))
            .GroupBy(x => x.ChargeStatementId!.Value).Select(g => new { Id = g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var releases = await (from r in db.AllocationReleases.AsNoTracking() join a in db.StatementAdjustments on r.StatementAdjustmentId equals a.Id
            where ids.Contains(a.ChargeStatementId) group r by a.ChargeStatementId into g select new { Id = g.Key, Amount = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        var rows = new List<ReceivableRow>();
        decimal chargeTotal = 0, paidTotal = 0, outstandingTotal = 0;
        var buckets = new Dictionary<string, decimal>(); var open = 0;
        foreach (var s in statements)
        {
            var additions = adjustments.Where(x => x.ChargeStatementId == s.Id && x.Kind != "Credit").Sum(x => x.Amount);
            var credits = adjustments.Where(x => x.ChargeStatementId == s.Id && x.Kind == "Credit").Sum(x => x.Amount);
            var allocated = allocations.GetValueOrDefault(s.Id); var deducted = deductions.GetValueOrDefault(s.Id); var released = releases.GetValueOrDefault(s.Id);
            var charge = s.Total + additions - credits; var paid = allocated + deducted - released; var outstanding = charge - paid;
            if (charge < 0 || paid < 0 || outstanding < 0) throw new ReceivableIntegrityException();
            var bucket = Bucket(s.DueDate, today, outstanding);
            chargeTotal += charge; paidTotal += paid; outstandingTotal += outstanding; if (outstanding > 0) open++;
            buckets[bucket] = buckets.GetValueOrDefault(bucket) + outstanding;
            rows.Add(new(s.Id, s.LeaseId, s.RentalAccountId, s.PeriodStart, s.DueDate, Money(s.Total), Money(additions), Money(credits), Money(allocated), Money(deducted), Money(released),
                Money(charge), Money(paid), Money(outstanding), Math.Max(0, today.DayNumber - s.DueDate.DayNumber), bucket));
        }
        return new(utc, today, zoneId, name, new(statements.Count, open, Money(chargeTotal), Money(paidTotal), Money(outstandingTotal),
            Money(buckets.GetValueOrDefault("Current")), Money(buckets.GetValueOrDefault("Overdue1To30")), Money(buckets.GetValueOrDefault("Overdue31To60")),
            Money(buckets.GetValueOrDefault("Overdue61To90")), Money(buckets.GetValueOrDefault("OverdueOver90"))), rows);
    }
}
public sealed class ReceivableIntegrityException : Exception;
