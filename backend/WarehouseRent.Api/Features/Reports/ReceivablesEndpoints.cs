using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Reports;

public static class ReceivablesEndpoints
{
    public static void MapReceivables(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/reports/receivables", (Guid organizationId, Guid facilityId, DateOnly startDate, DateOnly endDate, int? page,
            AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
            Read(organizationId, facilityId, startDate, endDate, page ?? 0, false, db, actor, commands, context, ct));
        api.MapGet("/exports/receivables", (Guid organizationId, Guid facilityId, DateOnly startDate, DateOnly endDate,
            AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
            Read(organizationId, facilityId, startDate, endDate, 0, true, db, actor, commands, context, ct));
    }
    private static IResult Invalid() => ApiFailure.Result(422, "RECEIVABLE_RANGE_INVALID", "Choose a nonempty due-date interval of at most366days, page0..99 and at most5000matched statements.");
    private static async Task<IResult> Read(Guid organization, Guid facility, DateOnly start, DateOnly end, int page, bool csv,
        AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)
            || !await db.CustomerOrganizations.AnyAsync(x => x.Id == organization && x.Active, ct)
            || !await db.Facilities.AnyAsync(x => x.Id == facility, ct)
            || !await actor.HasFacilityAsync(facility, "Finance", ct)) return OrganizationEndpoints.Missing();
        if (start == DateOnly.MinValue || end <= start || end.DayNumber - start.DayNumber > 366 || page is < 0 or > 99) return Invalid();
        ReceivableSnapshot? snapshot;
        try { snapshot = await ReceivablesReport.ReadAsync(db, organization, facility, start, end, ct); }
        catch (ReceivableIntegrityException) { return ApiFailure.Result(409, "ACCOUNTING_RECONCILIATION_REQUIRED", "Financial records do not reconcile. Resolve the linked records before exporting."); }
        if (snapshot is null) return Invalid();
        if (!csv)
        {
            await tx.CommitAsync(ct);
            return Results.Ok(new { schemaVersion = "P2-RECEIVABLES-V1", organizationId = organization, facilityId = facility, startDate = start, endDate = end,
                snapshot.ObservedAt, snapshot.AsOfDate, snapshot.TimeZone, snapshot.OrganizationName, snapshot.Summary, page, pageSize = 50,
                nextPage = (page + 1) * 50 < snapshot.Rows.Count ? (int?)(page + 1) : null, items = snapshot.Rows.Skip(page * 50).Take(50),
                source = "Current issued statement balances by due date; includes linked corrections and allocation releases. Excludes held deposits, unapplied receipts and pending payments. Not a historical as-of or tax report." });
        }
        var text = Csv(snapshot, organization, facility);
        commands.Audit(context, "ReceivablesExported", organization, $"Due-date CSV {start:yyyy-MM-dd}/{end:yyyy-MM-dd}; rows={snapshot.Rows.Count}", organization, facility);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(Encoding.UTF8.GetBytes("\uFEFF" + text), "text/csv; charset=utf-8", "receivables.csv");
    }
    private static string Csv(ReceivableSnapshot snapshot, Guid organization, Guid facility)
    {
        var csv = new StringBuilder("schema_version,observed_at_utc,as_of_date,organization_id,organization_name,facility_id,statement_id,lease_id,rental_account_id,period_start,due_date,original_vnd,additions_vnd,credits_vnd,allocations_vnd,deductions_vnd,releases_vnd,effective_charge_vnd,effective_paid_vnd,outstanding_vnd,days_overdue,bucket\r\n");
        foreach (var r in snapshot.Rows)
            csv.Append(string.Join(',', new[] { "P2-RECEIVABLES-V1", snapshot.ObservedAt.ToUniversalTime().ToString("O"), snapshot.AsOfDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), organization.ToString(), snapshot.OrganizationName,
                facility.ToString(), r.StatementId.ToString(), r.LeaseId.ToString(), r.RentalAccountId.ToString(), r.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), r.DueDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                r.OriginalVnd, r.AdditionsVnd, r.CreditsVnd, r.AllocationsVnd, r.DeductionsVnd, r.ReleasesVnd, r.EffectiveChargeVnd, r.EffectivePaidVnd, r.OutstandingVnd,
                r.DaysOverdue.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Bucket }.Select(SafeCsv.Text))).Append("\r\n");
        return csv.ToString();
    }
}
