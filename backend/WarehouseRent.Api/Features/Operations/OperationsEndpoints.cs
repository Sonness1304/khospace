using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.Operations;
public static class OperationsEndpoints
{
    public static void MapOperations(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/facilities", async (int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (search?.Length > 100) return Invalid(); var number = Page(page); var admin = await actor.IsOperatorAdminAsync(ct);
            var scopes = db.StaffFacilityScopes.Where(x => x.UserId == actor.UserId).Select(x => x.FacilityId);
            var query = db.Facilities.AsNoTracking().Where(x => admin || scopes.Contains(x.Id));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
            var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.Code, x.Name, x.Address, x.PermittedGoods, x.Published, version = Convert.ToBase64String(x.Version) }).ToListAsync(ct);
            var grants = await db.StaffFacilityScopes.AsNoTracking().Where(x => x.UserId == actor.UserId).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(x => new { x.Id, x.Code, x.Name, x.Address, x.PermittedGoods, x.Published, x.version, roles = grants.Where(g => g.FacilityId == x.Id).Select(g => g.StaffRole) }), page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
        api.MapGet("/bookings", async (Guid facilityId, Guid? organizationId, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var denied = await Denied(facilityId, organizationId, actor, ct); if (denied is not null) return denied; if (search?.Length > 100) return Invalid(); var number = Page(page); var utc = await Utc(db, ct);
            var query = from booking in db.Bookings.AsNoTracking() join org in db.CustomerOrganizations on booking.OrganizationId equals org.Id join account in db.RentalAccounts on booking.Id equals account.BookingId
                where booking.FacilityId == facilityId && (organizationId == null || booking.OrganizationId == organizationId)
                select new { booking.Id, booking.OrganizationId, organizationName = org.Name, booking.FacilityId, booking.StartDate, booking.EndDate, booking.State, booking.ExpiresAt, rentalAccountId = account.Id };
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.organizationName.Contains(search) || x.State.Contains(search));
            var rows = await query.OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { items = rows.Select(x => new { x.Id, x.OrganizationId, x.organizationName, x.FacilityId, x.StartDate, x.EndDate, state = x.State == "Held" && x.ExpiresAt <= utc ? "Expired" : x.State, x.ExpiresAt, x.rentalAccountId }), page = number, pageSize = 50, total = await query.CountAsync(ct), observedAt = utc });
        });
        api.MapGet("/leases", async (Guid facilityId, Guid? organizationId, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var denied = await Denied(facilityId, organizationId, actor, ct); if (denied is not null) return denied; if (search?.Length > 100) return Invalid(); var number = Page(page); var utc = await Utc(db, ct); var today = CatalogPolicy.Today(utc);
            var query = from lease in db.Leases.AsNoTracking() join org in db.CustomerOrganizations on lease.OrganizationId equals org.Id
                where lease.FacilityId == facilityId && (organizationId == null || lease.OrganizationId == organizationId)
                select new { lease.Id, lease.BookingId, lease.RentalAccountId, lease.OrganizationId, organizationName = org.Name, lease.FacilityId, lease.StartDate, lease.EndDate, lease.State, lease.ActivatedAt };
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.organizationName.Contains(search) || x.State.Contains(search));
            var rows = await query.OrderByDescending(x => x.StartDate).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { items = rows.Select(x => new { x.Id, x.BookingId, x.RentalAccountId, x.OrganizationId, x.organizationName, x.FacilityId, x.StartDate, x.EndDate, state = x.State == "Active" && x.EndDate <= today ? "ClearancePending" : x.State, x.ActivatedAt }), page = number, pageSize = 50, total = await query.CountAsync(ct), observedAt = utc });
        });
        api.MapGet("/operations/calendar", async (Guid facilityId, DateOnly startDate, DateOnly endDate, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (!await actor.HasFacilityAsync(facilityId, null, ct)) return OrganizationEndpoints.Forbidden();
            if (startDate == DateOnly.MinValue || endDate <= startDate || endDate.DayNumber - startDate.DayNumber > 93 || search?.Length > 100) return Invalid(); var number = Page(page); var utc = await Utc(db, ct);
            var units = db.RentableUnits.AsNoTracking().Where(x => x.FacilityId == facilityId);
            if (!string.IsNullOrWhiteSpace(search)) units = units.Where(x => x.Code.Contains(search));
            var rows = await units.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.Code, x.AreaM2 }).ToListAsync(ct); var ids = rows.Select(x => x.Id).ToArray();
            var schedules = await (from allocation in db.SpaceAllocations.AsNoTracking() join line in db.BookingLines on allocation.BookingLineId equals line.Id into lineJoin from line in lineJoin.DefaultIfEmpty()
                join booking in db.Bookings on line.BookingId equals booking.Id into bookingJoin from booking in bookingJoin.DefaultIfEmpty()
                join org in db.CustomerOrganizations on booking.OrganizationId equals org.Id into orgJoin from org in orgJoin.DefaultIfEmpty()
                where ids.Contains(allocation.UnitId) && allocation.StartDate < endDate && startDate < allocation.EndDate && (allocation.State == "Confirmed" || allocation.State == "Maintenance" || allocation.State == "Held" && allocation.HoldExpiresAt > utc)
                orderby allocation.StartDate, allocation.Id select new { allocation.Id, allocation.UnitId, allocation.StartDate, allocation.EndDate, allocation.State, bookingId = (Guid?)booking.Id, organizationId = (Guid?)org.Id, organizationName = org.Name }).ToListAsync(ct);
            var startUtc = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime(); var endUtc = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
            var occupancies = await db.UnitOccupancies.AsNoTracking().Where(x => ids.Contains(x.UnitId) && x.HandedOverAt < endUtc && (x.ClearedAt == null || x.ClearedAt >= startUtc)).OrderBy(x => x.HandedOverAt).ThenBy(x => x.Id).ToListAsync(ct);
            return Results.Ok(new { items = rows.Select(x => new { x.Id, x.Code, areaM2 = CatalogPolicy.Money(x.AreaM2), schedules = schedules.Where(a => a.UnitId == x.Id), custody = occupancies.Where(o => o.UnitId == x.Id).Select(o => new { o.Id, o.LeaseUnitId, o.OrganizationId, o.HandedOverAt, o.ScheduledEnd, o.ClearedAt, state = o.ClearedAt is not null ? "PhysicallyCleared" : o.ScheduledEnd <= CatalogPolicy.Today(utc) ? "ClearancePending" : "PhysicallyOccupied" }) }),
                page = number, pageSize = 50, total = await units.CountAsync(ct), observedAt = utc, startDate, endDate });
        });
        api.MapGet("/audit", async (Guid? facilityId, Guid? organizationId, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var query = db.SecurityAudits.AsNoTracking();
            if (facilityId is { } facility) { if (!await actor.HasFacilityAsync(facility, null, ct)) return OrganizationEndpoints.Forbidden(); query = query.Where(x => x.FacilityId == facility); }
            else if (organizationId is { } org) { if ((await actor.MembershipAsync(org, ct))?.Role != "CustomerAdmin" && !await actor.IsOperatorAdminAsync(ct)) return OrganizationEndpoints.Forbidden(); query = query.Where(x => x.OrganizationId == org); }
            else if (!await actor.IsOperatorAdminAsync(ct)) return OrganizationEndpoints.Forbidden();
            if (search?.Length > 100) return Invalid(); if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Action.Contains(search) || x.Reason.Contains(search) || x.ActorId.Contains(search)); var number = Page(page);
            var items = await query.OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.ActorId, x.OrganizationId, x.FacilityId, x.Action, x.AggregateId, x.CorrelationId, x.Reason, x.At }).ToListAsync(ct);
            return Results.Ok(new { items, page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
    }
    private static int Page(int? value) => Math.Clamp(value ?? 1, 1, 10000);
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_QUERY", "Check date range and search length.");
    private static async Task<DateTimeOffset> Utc(AppDbContext db, CancellationToken ct) => await DatabaseDialect.UtcNowAsync(db, ct);
    private static async Task<IResult?> Denied(Guid facility, Guid? org, CurrentActor actor, CancellationToken ct)
    {
        if (await actor.HasFacilityAsync(facility, null, ct)) return null;
        if (org is { } id && await actor.MembershipAsync(id, ct) is not null) return null;
        return org is not null ? OrganizationEndpoints.Missing() : OrganizationEndpoints.Forbidden();
    }
}
