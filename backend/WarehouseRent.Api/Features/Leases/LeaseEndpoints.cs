using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.Leases;
public sealed record LeaseAction(string Reason, bool InspectionConfirmed = false);
public sealed record TerminateLease(string Reason, bool TerminationConfirmed = false);
public static class LeaseEndpoints
{
    public static void MapLeases(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/leases/{id:guid}/terminate", async (Guid id, TerminateLease input, AppDbContext db, CurrentActor actor, LeaseService leases, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var item = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || (await actor.MembershipAsync(item.OrganizationId, ct) is null && !await actor.HasFacilityAsync(item.FacilityId, null, ct))) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(item.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500) || !input.TerminationConfirmed) return ApiFailure.Result(422, "TERMINATION_CONFIRMATION_REQUIRED", "Confirm termination and supply a reason. Custody and settlement remain outstanding.");
            return await commands.ExecuteAsync(context, item.OrganizationId, "TerminateLease", new { id, input }, token => leases.TerminateAsync(id, context, input.Reason, actor.UserId, token), ct,
                organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "FacilityManager");
        });
        api.MapPost("/bookings/{id:guid}/lease", async (Guid id, LeaseAction input, AppDbContext db, CurrentActor actor, LeaseService leases, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (booking is null) return OrganizationEndpoints.Missing();
            if (await actor.MembershipAsync(booking.OrganizationId, ct) is null && !await actor.HasFacilityAsync(booking.FacilityId, null, ct)) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(booking.FacilityId, "Sales", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, booking.OrganizationId, "CreateLease", new { id, input }, token => leases.ConvertAsync(id, context, input.Reason, token), ct,
                organizationId: booking.OrganizationId, facilityId: booking.FacilityId, facilityRole: "Sales");
        });
        api.MapGet("/leases/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var item = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || (await actor.MembershipAsync(item.OrganizationId, ct) is null && !await actor.HasFacilityAsync(item.FacilityId, null, ct))) return OrganizationEndpoints.Missing();
            var utc = await DatabaseDialect.UtcNowAsync(db, ct);
            var units = await db.LeaseUnits.AsNoTracking().Where(x => x.LeaseId == id).OrderBy(x => x.UnitId).ToListAsync(ct);
            var bookingVersion = await db.Bookings.AsNoTracking().Where(x => x.Id == item.BookingId).Select(x => x.Version).SingleAsync(ct);
            var lines = await (from line in db.LeaseVersionLines.AsNoTracking() join version in db.LeaseVersions on line.LeaseVersionId equals version.Id where version.LeaseId == id orderby line.StartDate, line.LeaseUnitId
                select new { line.Id, line.LeaseUnitId, line.SourceQuoteLineId, line.StartDate, line.EndDate, line.Days, line.AreaM2, line.DailyRate, line.BillingBasis, line.MonthlyRate, line.CalendarDays, line.Amount }).ToListAsync(ct);
            return Results.Ok(new { item.Id, item.BookingId, item.RentalAccountId, item.OrganizationId, item.FacilityId, item.StartDate, item.EndDate, item.ActivatedAt, item.ClearedAt, item.TerminatedAt, item.TerminatedBy, item.TerminationReason, item.ClosedAt,
                state = LeaseService.EffectiveState(item, CatalogPolicy.Today(utc)), observedAt = utc, version = Convert.ToBase64String(item.Version), bookingVersion = Convert.ToBase64String(bookingVersion), units,
                lines = lines.Select(x => new { x.Id, x.LeaseUnitId, x.SourceQuoteLineId, x.StartDate, x.EndDate, x.Days, areaM2 = CatalogPolicy.Money(x.AreaM2), dailyRate = CatalogPolicy.Money(x.DailyRate), x.BillingBasis, monthlyRate = x.MonthlyRate.HasValue ? CatalogPolicy.Money(x.MonthlyRate.Value) : null, x.CalendarDays, amount = CatalogPolicy.Money(x.Amount) }) });
        });
        api.MapPost("/leases/{id:guid}/handover", (Guid id, LeaseAction input, AppDbContext db, CurrentActor actor, LeaseService leases, CommandExecutor commands, HttpContext context, CancellationToken ct) => Physical(id, input, false, db, actor, leases, commands, context, ct));
        api.MapPost("/leases/{id:guid}/physical-clearance", (Guid id, LeaseAction input, AppDbContext db, CurrentActor actor, LeaseService leases, CommandExecutor commands, HttpContext context, CancellationToken ct) => Physical(id, input, true, db, actor, leases, commands, context, ct));
        api.MapPost("/leases/{id:guid}/cancel", async (Guid id, BookingAction input, AppDbContext db, CurrentActor actor, BookingService bookings, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var item = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return OrganizationEndpoints.Missing();
            var member = await actor.MembershipAsync(item.OrganizationId, ct); var sales = await actor.HasFacilityAsync(item.FacilityId, "Sales", ct);
            if (member is null && !sales) return OrganizationEndpoints.Missing();
            if (member?.Role != "CustomerAdmin" && !sales) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            // This route uses the booking's version; its shared aggregate cancellation is identical.
            return await commands.ExecuteAsync(context, item.OrganizationId, "CancelRental", new { bookingId = item.BookingId, input }, token => bookings.CancelAsync(item.BookingId, input.Reason, context, token), ct,
                organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "Sales");
        });
        api.MapGet("/operations/conflicts", async (Guid facilityId, int? page, int? affectedPage, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(facilityId, null, ct)) return OrganizationEndpoints.Forbidden();
            var utc = await DatabaseDialect.UtcNowAsync(db, ct); var today = CatalogPolicy.Today(utc); var number = Math.Clamp(page ?? 1, 1, 10000); var ap = Math.Clamp(affectedPage ?? 1, 1, 10000);
            var query = from occupancy in db.UnitOccupancies.AsNoTracking() join unit in db.RentableUnits on occupancy.UnitId equals unit.Id
                where unit.FacilityId == facilityId && occupancy.ClearedAt == null && occupancy.ScheduledEnd <= today
                select new { occupancy.Id, occupancy.UnitId, unit.Code, occupancy.LeaseUnitId, occupancy.ScheduledEnd };
            var rows = await query.OrderBy(x => x.ScheduledEnd).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            var affectedUnits = query.Select(x => x.UnitId);
            var affectedQuery = from line in db.BookingLines.AsNoTracking() join booking in db.Bookings on line.BookingId equals booking.Id
                where affectedUnits.Contains(line.UnitId) && booking.State == "Confirmed" && booking.EndDate > today
                select new { bookingId = booking.Id, line.UnitId, booking.OrganizationId, booking.StartDate, booking.EndDate };
            var affected = await affectedQuery.OrderBy(x => x.StartDate).ThenBy(x => x.bookingId).ThenBy(x => x.UnitId).Skip((ap - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { items = rows, page = number, pageSize = 50, total = await query.CountAsync(ct), affectedBookings = affected, affectedPage = ap, affectedTotal = await affectedQuery.CountAsync(ct), observedAt = utc, state = "Unresolved physical occupancy", truncated = false });
        });
    }
    private static IResult Invalid() => ApiFailure.Result(422, "REASON_REQUIRED", "Supply a reason and the required inspection confirmation.");
    private static async Task<IResult> Physical(Guid id, LeaseAction input, bool clear, AppDbContext db, CurrentActor actor, LeaseService leases, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        var item = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return OrganizationEndpoints.Missing();
        if (await actor.MembershipAsync(item.OrganizationId, ct) is null && !await actor.HasFacilityAsync(item.FacilityId, null, ct)) return OrganizationEndpoints.Missing();
        if (!await actor.HasFacilityAsync(item.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
        if (!CatalogPolicy.Text(input.Reason, 500) || !input.InspectionConfirmed) return Invalid();
        return await commands.ExecuteAsync(context, item.OrganizationId, clear ? "PhysicalClearance" : "Handover", new { id, input }, token => clear
            ? leases.ClearAsync(id, context, input.Reason, input.InspectionConfirmed, token) : leases.HandoverAsync(id, context, input.Reason, input.InspectionConfirmed, token), ct,
            organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "FacilityManager");
    }
}
