using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Reports;
public static class ReportEndpoints
{
    private static IResult Invalid() => ApiFailure.Result(422, "REPORT_RANGE_INVALID", "Choose a nonempty interval of at most 366 days; exports allow at most 5000 rows.");
    private static bool Range(DateOnly start, DateOnly end) => start != DateOnly.MinValue && end > start && end.DayNumber - start.DayNumber <= 366;
    private static DateTimeOffset Boundary(DateOnly date, TimeZoneInfo zone) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
    private static int Days(DateOnly a, DateOnly b, DateOnly start, DateOnly end) => Math.Max(0, Math.Min(b.DayNumber, end.DayNumber) - Math.Max(a.DayNumber, start.DayNumber));
    private static int UnionDays(IEnumerable<(DateOnly Start, DateOnly End)> ranges)
    {
        var total = 0; var last = int.MinValue;
        foreach (var (start, end) in ranges.OrderBy(x => x.Start)) { total += Math.Max(0, end.DayNumber - Math.Max(start.DayNumber, last)); last = Math.Max(last, end.DayNumber); }
        return total;
    }
    private static async Task<bool> ExportAllowed(AppDbContext db, CurrentActor actor, Guid org, Guid facility, bool finance, CancellationToken ct)
    {
        if (!await db.CustomerOrganizations.AnyAsync(x => x.Id == org && x.Active, ct) || !await db.Facilities.AnyAsync(x => x.Id == facility, ct)) return false;
        var member = await actor.MembershipAsync(org, ct);
        return member is not null && (!finance || member.Role == "CustomerAdmin") || await actor.HasFacilityAsync(facility, finance ? "Finance" : "Clerk", ct)
            || !finance && await actor.HasFacilityAsync(facility, "FacilityManager", ct);
    }
    public static void MapReports(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/reports/facilities/{facilityId:guid}", async (Guid facilityId, DateOnly startDate, DateOnly endDate, AppDbContext db, CurrentActor actor, TimeProvider time, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(facilityId, "FacilityManager", ct) && !await actor.HasFacilityAsync(facilityId, "Finance", ct) && !await actor.HasFacilityAsync(facilityId, "Sales", ct)) return OrganizationEndpoints.Forbidden();
            if (!Range(startDate, endDate)) return Invalid();
            var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == facilityId, ct); if (facility is null) return OrganizationEndpoints.Missing();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(facility.TimeZoneId); var fromUtc = Boundary(startDate, zone); var toUtc = Boundary(endDate, zone); var now = time.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var units = await db.RentableUnits.AsNoTracking().Where(x => x.FacilityId == facilityId).Select(x => new { x.Id, x.AreaM2, x.RetiredAt }).ToListAsync(ct);
            var allocations = await (from a in db.SpaceAllocations.AsNoTracking() join u in db.RentableUnits on a.UnitId equals u.Id
                where u.FacilityId == facilityId && a.StartDate < endDate && startDate < a.EndDate && (a.State == "Confirmed" || a.State == "Maintenance" || a.State == "Released")
                select new { a.UnitId, a.StartDate, a.EndDate, a.State, a.BookingLineId }).ToListAsync(ct);
            // Recover historical prefixes from allocations released by older financial closure code.
            // A cancelled pre-handover booking has no activated lease and contributes no such history.
            var releasedHistory = await (from line in db.BookingLines join lease in db.Leases on line.BookingId equals lease.BookingId
                where lease.FacilityId == facilityId && lease.ActivatedAt != null && (lease.TerminatedAt != null || lease.ClosedAt != null)
                select new { line.Id, Until = lease.TerminatedAt ?? lease.ClosedAt }).ToListAsync(ct);
            var historicalEnds = releasedHistory.ToDictionary(x => x.Id, x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Until!.Value, zone).DateTime));
            decimal rentable = 0, booked = 0, maintenance = 0;
            foreach (var unit in units)
            {
                var until = unit.RetiredAt is null ? endDate : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(unit.RetiredAt.Value, zone).DateTime);
                if (until > endDate) until = endDate; if (until <= startDate) continue;
                var spans = allocations.Where(x => x.UnitId == unit.Id && (x.State != "Released" || x.BookingLineId is Guid lineId && historicalEnds.ContainsKey(lineId)))
                    .Select(x => { var end = x.EndDate > until ? until : x.EndDate;
                        if (x.State == "Released" && x.BookingLineId is Guid lineId && historicalEnds.TryGetValue(lineId, out var historicalEnd) && historicalEnd < end) end = historicalEnd;
                        return (State: x.State == "Released" ? "Confirmed" : x.State, Start: x.StartDate < startDate ? startDate : x.StartDate, End: end); })
                    .Where(x => x.Start < x.End).ToArray();
                var blocked = UnionDays(spans.Where(x => x.State == "Maintenance").Select(x => (x.Start, x.End)));
                maintenance += unit.AreaM2 * blocked; rentable += unit.AreaM2 * (Days(startDate, until, startDate, endDate) - blocked);
                booked += unit.AreaM2 * UnionDays(spans.Where(x => x.State == "Confirmed").Select(x => (x.Start, x.End)));
            }
            var occupied = await (from o in db.UnitOccupancies.AsNoTracking() join u in db.RentableUnits on o.UnitId equals u.Id where u.FacilityId == facilityId && o.ClearedAt == null select u.Id).Distinct().ToArrayAsync(ct);
            var currentArea = units.Where(x => x.RetiredAt == null).Sum(x => x.AreaM2); var occupiedArea = units.Where(x => occupied.Contains(x.Id)).Sum(x => x.AreaM2);
            var accounts = from a in db.RentalAccounts join b in db.Bookings on a.BookingId equals b.Id where b.FacilityId == facilityId select a.Id;
            var verified = await db.PaymentReceipts.Where(x => x.FacilityId == facilityId && x.State == "Verified" && x.ReceivedAt >= fromUtc && x.ReceivedAt < toUtc).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var refunds = await db.SettlementEntries.Where(x => accounts.Contains(x.RentalAccountId) && x.Kind != "Deduction" && x.PerformedAt >= fromUtc && x.PerformedAt < toUtc).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var receivedDeposit = await db.DepositTransactions.Where(x => accounts.Contains(x.RentalAccountId)).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var disposedDeposit = await db.SettlementEntries.Where(x => accounts.Contains(x.RentalAccountId) && x.Kind != "RefundUnapplied").SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            var returnedDeposit = await db.AllocationReleases.Where(x => accounts.Contains(x.RentalAccountId) && x.SettlementEntryId != null).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
            await tx.CommitAsync(ct);
            return Results.Ok(new { facilityId, startDate, endDate, timeZone = facility.TimeZoneId, observedAt = now,
                scheduled = new { bookedAreaDays = booked, rentableAreaDays = rentable, maintenanceAreaDays = maintenance, utilizationPercent = rentable == 0 ? (decimal?)null : decimal.Round(booked / rentable * 100, 2), source = "Confirmed SpaceAllocations; current catalog areas; retired dates clipped; maintenance excluded; temporary holds excluded" },
                physical = new { occupiedAreaM2 = occupiedArea, currentCatalogAreaM2 = currentArea, source = "Current uncleared UnitOccupancies, distinct unit area; current observation, not the requested historical interval" },
                collections = new { grossVerifiedReceiptsVnd = verified, recordedRefundsVnd = refunds, heldDepositNowVnd = receivedDeposit - disposedDeposit + returnedDeposit,
                    source = "Currently Verified receipts by ReceivedAt [start,end); includes deposits/unapplied money. Recorded refunds by PerformedAt. Held deposit is a separate current liability using releases/deductions/refunds; no rental revenue claim" } });
        });
        api.MapGet("/exports/stock", async (Guid organizationId, Guid facilityId, DateOnly startDate, DateOnly endDate, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await ExportAllowed(db, actor, organizationId, facilityId, false, ct)) return OrganizationEndpoints.Missing();
            if (!Range(startDate, endDate)) return Invalid();
            var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Facilities.Where(x => x.Id == facilityId).Select(x => x.TimeZoneId).SingleAsync(ct)); var fromUtc = Boundary(startDate, zone); var toUtc = Boundary(endDate, zone);
            var rows = await (from l in db.StockLedger.AsNoTracking() join p in db.StockPostings on l.StockPostingId equals p.Id join item in db.Items on l.ItemId equals item.Id
                where l.OrganizationId == organizationId && p.FacilityId == facilityId && l.PostedAt >= fromUtc && l.PostedAt < toUtc orderby l.PostedAt, l.Id
                select new { l.Id, l.StockPostingId, item.Sku, item.Description, item.BaseUom, l.LeaseUnitId, l.Delta, l.PostedAt, p.Type }).Take(5001).ToListAsync(ct);
            if (rows.Count > 5000) return Invalid();
            var csv = new StringBuilder("posting_id,ledger_id,sku,description,uom,lease_unit_id,delta,posted_at_utc,type\r\n");
            foreach (var r in rows) csv.AppendLine(string.Join(',', SafeCsv.Text(r.StockPostingId.ToString()), SafeCsv.Text(r.Id.ToString()), SafeCsv.Text(r.Sku), SafeCsv.Text(r.Description), SafeCsv.Text(r.BaseUom), SafeCsv.Text(r.LeaseUnitId.ToString()), SafeCsv.Number(r.Delta), SafeCsv.Text(r.PostedAt.ToUniversalTime().ToString("O")), SafeCsv.Text(r.Type)));
            commands.Audit(context, "StockExported", organizationId, $"CSV {startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}; rows={rows.Count}", organizationId, facilityId); await db.SaveChangesAsync(ct);
            return Csv(context, csv.ToString(), "stock-history.csv");
        });
        api.MapGet("/exports/statements", async (Guid organizationId, Guid facilityId, DateOnly startDate, DateOnly endDate, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await ExportAllowed(db, actor, organizationId, facilityId, true, ct)) return OrganizationEndpoints.Missing(); if (!Range(startDate, endDate)) return Invalid();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var rows = await db.ChargeStatements.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.FacilityId == facilityId && x.PeriodStart >= startDate && x.PeriodStart < endDate)
                .OrderBy(x => x.PeriodStart).ThenBy(x => x.Id).Take(5001).ToListAsync(ct); if (rows.Count > 5000) return Invalid();
            var ids = rows.Select(x => x.Id).ToArray();
            var adjustments = await db.StatementAdjustments.AsNoTracking().Where(x => ids.Contains(x.ChargeStatementId)).ToListAsync(ct);
            var funds = await db.FundAllocations.AsNoTracking().Where(x => x.ChargeStatementId != null && ids.Contains(x.ChargeStatementId.Value)).ToListAsync(ct);
            var deductions = await db.SettlementEntries.AsNoTracking().Where(x => x.Kind == "Deduction" && x.ChargeStatementId != null && ids.Contains(x.ChargeStatementId.Value)).ToListAsync(ct);
            var releases = await (from r in db.AllocationReleases join a in db.StatementAdjustments on r.StatementAdjustmentId equals a.Id where ids.Contains(a.ChargeStatementId) select new { a.ChargeStatementId, r.Amount }).ToListAsync(ct);
            var csv = new StringBuilder("statement_id,lease_id,period_start,due_date,original_vnd,effective_charge_vnd,effective_paid_vnd,outstanding_vnd\r\n");
            foreach (var row in rows)
            {
                var charge = row.Total + adjustments.Where(x => x.ChargeStatementId == row.Id).Sum(x => x.Kind == "Credit" ? -x.Amount : x.Amount);
                var paid = funds.Where(x => x.ChargeStatementId == row.Id).Sum(x => x.Amount) + deductions.Where(x => x.ChargeStatementId == row.Id).Sum(x => x.Amount) - releases.Where(x => x.ChargeStatementId == row.Id).Sum(x => x.Amount);
                csv.AppendLine(string.Join(',', SafeCsv.Text(row.Id.ToString()), SafeCsv.Text(row.LeaseId.ToString()), SafeCsv.Text(row.PeriodStart.ToString("yyyy-MM-dd")), SafeCsv.Text(row.DueDate.ToString("yyyy-MM-dd")), SafeCsv.Number(row.Total), SafeCsv.Number(charge), SafeCsv.Number(paid), SafeCsv.Number(charge - paid)));
            }
            commands.Audit(context, "StatementsExported", organizationId, $"CSV {startDate:yyyy-MM-dd}/{endDate:yyyy-MM-dd}; rows={rows.Count}", organizationId, facilityId); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Csv(context, csv.ToString(), "statements.csv");
        });
    }
    private static IResult Csv(HttpContext context, string value, string fileName)
    { context.Response.Headers["X-Content-Type-Options"] = "nosniff"; return Results.File(Encoding.UTF8.GetBytes("\uFEFF" + value), "text/csv; charset=utf-8", fileName); }
}
