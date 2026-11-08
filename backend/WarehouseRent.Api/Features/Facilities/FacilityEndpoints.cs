using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Persistence;
using static WarehouseRent.Api.Features.Facilities.CatalogPolicy;

namespace WarehouseRent.Api.Features.Facilities;

public sealed record FacilityInput(string Code, string Name, string Address, string PermittedGoods, bool Published, string Reason);
public sealed record ZoneInput(string Code, string Reason);
public sealed record UnitInput(Guid ZoneId, string Code, string AreaM2, bool Published, string Reason);
public sealed record RateInput(string DailyRate, DateOnly SalesValidFrom, DateOnly? SalesValidTo, string Reason, string BillingBasis = "Daily", string? MonthlyRate = null);
public sealed record MaintenanceInput(Guid FacilityId, Guid[] UnitIds, DateOnly StartDate, DateOnly EndDate, string Reason);
public sealed record RetireUnit(string Reason, bool RetirementConfirmed = false);

public static class FacilityEndpoints
{
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_CATALOG_INPUT", "Check dates, positive decimal values, codes and the required reason.");
    private static bool Valid(UnitInput i, out decimal area) => Amount(i.AreaM2, 2, 9999999999.99m, out area) && Text(i.Code, 40) && Text(i.Reason, 500);

    public static void MapFacilities(this WebApplication app)
    {
        app.MapGet("/api/v1/public/facilities", async (AppDbContext db, int? pageSize, string? after, CancellationToken ct) =>
        {
            if (pageSize is < 1 or > 100 || after?.Length > 40) return Invalid();
            var limit = pageSize ?? 50;
            var rows = await db.Facilities.AsNoTracking().Where(x => x.Published && (after == null || x.Code.CompareTo(after) > 0)).OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name, x.Address, x.PermittedGoods, x.TimeZoneId }).Take(limit + 1).ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(limit), nextCursor = rows.Count > limit ? rows[limit - 1].Code : null });
        });
        app.MapGet("/api/v1/public/facilities/{id:guid}/units", async (Guid id, AppDbContext db, TimeProvider clock, int? pageSize, string? after, CancellationToken ct) =>
        {
            if (pageSize is < 1 or > 100 || after?.Length > 40) return Invalid();
            if (!await db.Facilities.AnyAsync(x => x.Id == id && x.Published, ct)) return OrganizationEndpoints.Missing();
            return await PublicUnits(db, id, Today(clock.GetUtcNow()), null, null, 0, pageSize ?? 50, after, clock.GetUtcNow(), ct);
        });
        app.MapGet("/api/v1/availability", async (Guid facilityId, DateOnly startDate, DateOnly endDate, decimal? minArea, int? pageSize,
            string? after, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow(); var today = Today(now);
            if (!Dates(startDate, endDate, today) || minArea is < 0 or > 9999999999.99m || pageSize is < 1 or > 100 || after?.Length > 40) return Invalid();
            if (!await db.Facilities.AnyAsync(x => x.Id == facilityId && x.Published, ct)) return OrganizationEndpoints.Missing();
            return await PublicUnits(db, facilityId, today, startDate, endDate, minArea ?? 0, pageSize ?? 50, after, now, ct);
        });
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/commands/{id:guid}", async (Guid id, Guid scopeId, CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            var saved = await db.OperationalCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ScopeId == scopeId && x.ActorId == actor.UserId, ct);
            if (saved is null)
            {
                var membershipCommand = await db.MembershipCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == scopeId && x.ActorId == actor.UserId, ct);
                return membershipCommand is not null && await actor.MembershipAsync(scopeId, ct) is not null ? Results.Content(membershipCommand.ResultJson, "application/json") : OrganizationEndpoints.Missing();
            }
            // Reminder receipts retain the same current Finance boundary as command replay.
            using var receipt = JsonDocument.Parse(saved.ResultJson);
            if (receipt.RootElement.TryGetProperty("readbackPolicy", out var policy) && policy.GetString() == "CurrentFinance")
            {
                var financePermitted = saved.OrganizationId.HasValue && saved.FacilityId.HasValue
                    && await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)
                    && await db.CustomerOrganizations.AnyAsync(x => x.Id == saved.OrganizationId.Value && x.Active, ct)
                    && await db.Facilities.AnyAsync(x => x.Id == saved.FacilityId.Value, ct)
                    && await actor.HasFacilityAsync(saved.FacilityId.Value, "Finance", ct);
                return financePermitted ? Results.Content(saved.ResultJson, "application/json") : OrganizationEndpoints.Missing();
            }
            var permitted = await actor.IsOperatorAdminAsync(ct)
                || saved.OrganizationId.HasValue && await actor.MembershipAsync(saved.OrganizationId.Value, ct) is not null
                || saved.FacilityId.HasValue && await actor.HasFacilityAsync(saved.FacilityId.Value, saved.RequiredFacilityRole ?? "FacilityManager", ct)
                || saved.OrganizationId == null && saved.FacilityId == null && await actor.HasFacilityAsync(scopeId, "FacilityManager", ct);
            return permitted ? Results.Content(saved.ResultJson, "application/json") : OrganizationEndpoints.Missing();
        });
        api.MapGet("/facilities/{id:guid}/catalog", async (Guid id, int? unitsPage, int? zonesPage, string? search, CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(id, null, ct)) return OrganizationEndpoints.Forbidden();
            var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (facility is null) return OrganizationEndpoints.Missing();
            if (search?.Length > 100) return Invalid(); var up = Math.Clamp(unitsPage ?? 1, 1, 10000); var zp = Math.Clamp(zonesPage ?? 1, 1, 10000);
            var unitQuery = db.RentableUnits.AsNoTracking().Where(x => x.FacilityId == id); if (!string.IsNullOrWhiteSpace(search)) unitQuery = unitQuery.Where(x => x.Code.Contains(search));
            var zoneQuery = db.Zones.AsNoTracking().Where(x => x.FacilityId == id);
            var units = await unitQuery.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((up - 1) * 50).Take(50).Select(x => new { x.Id, x.Code, x.ZoneId, x.AreaM2, x.Published, x.Version, x.RetiredAt, x.RetirementReason }).ToListAsync(ct);
            var zones = await zoneQuery.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((zp - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { facility, zones, units = units.Select(x => new { x.Id, x.Code, x.ZoneId, areaM2 = Money(x.AreaM2), x.Published, x.RetiredAt, x.RetirementReason, version = Convert.ToBase64String(x.Version) }), unitsPage = up, zonesPage = zp, pageSize = 50, unitTotal = await unitQuery.CountAsync(ct), zoneTotal = await zoneQuery.CountAsync(ct) });
        });
        api.MapPost("/facilities", async (FacilityInput input, CurrentActor actor, AppDbContext db, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await actor.IsOperatorAdminAsync(ct)) return OrganizationEndpoints.Forbidden();
            if (!Text(input.Code, 40) || !Text(input.Name, 200) || !Text(input.Address, 500) || !Text(input.PermittedGoods, 500) || !Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, Guid.Empty, "CreateFacility", input, async token =>
            {
                if (await db.Facilities.AnyAsync(x => x.Code == input.Code.Trim(), token)) throw new SpaceConflict("DUPLICATE_CODE");
                var item = new Facility { Id = Guid.NewGuid(), Code = input.Code.Trim(), Name = input.Name.Trim(), Address = input.Address.Trim(), PermittedGoods = input.PermittedGoods.Trim(), Published = input.Published };
                db.Facilities.Add(item); commands.Audit(context, "FacilityCreated", item.Id, input.Reason, facility: item.Id); return new { resourceId = item.Id };
            }, ct);
        });
        api.MapPatch("/facilities/{id:guid}", async (Guid id, FacilityInput input, CurrentActor actor, AppDbContext db, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(id, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!Text(input.Code, 40) || !Text(input.Name, 200) || !Text(input.Address, 500) || !Text(input.PermittedGoods, 500) || !Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, id, "ChangeFacility", input, async token =>
            {
                var item = await LockFacility(db, id, token); CommandExecutor.Version(context, item.Version);
                item.Code = input.Code.Trim(); item.Name = input.Name.Trim(); item.Address = input.Address.Trim(); item.PermittedGoods = input.PermittedGoods.Trim(); item.Published = input.Published;
                commands.Audit(context, "FacilityChanged", item.Id, input.Reason); await db.SaveChangesAsync(token);
                return new { resourceId = item.Id, version = Convert.ToBase64String(item.Version) };
            }, ct);
        });
        api.MapPost("/facilities/{id:guid}/zones", async (Guid id, ZoneInput input, CurrentActor actor, AppDbContext db, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(id, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!Text(input.Code, 40) || !Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, id, "CreateZone", input, async token =>
            {
                await LockFacility(db, id, token);
                if (await db.Zones.AnyAsync(x => x.FacilityId == id && x.Code == input.Code.Trim(), token)) throw new SpaceConflict("DUPLICATE_CODE");
                var item = new Zone { Id = Guid.NewGuid(), FacilityId = id, Code = input.Code.Trim() }; db.Zones.Add(item);
                commands.Audit(context, "ZoneCreated", item.Id, input.Reason); return new { resourceId = item.Id };
            }, ct);
        });
        api.MapPost("/facilities/{id:guid}/units", async (Guid id, UnitInput input, CurrentActor actor, AppDbContext db, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            if (!await actor.HasFacilityAsync(id, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!Valid(input, out var area)) return Invalid();
            return await commands.ExecuteAsync(context, id, "CreateUnit", input, async token =>
            {
                await LockFacility(db, id, token);
                if (!await db.Zones.AnyAsync(x => x.Id == input.ZoneId && x.FacilityId == id, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The zone is unavailable.");
                if (await db.RentableUnits.AnyAsync(x => x.FacilityId == id && x.Code == input.Code.Trim(), token)) throw new SpaceConflict("DUPLICATE_CODE");
                var item = new RentableUnit { Id = Guid.NewGuid(), FacilityId = id, ZoneId = input.ZoneId, Code = input.Code.Trim(), AreaM2 = area, Published = input.Published };
                db.RentableUnits.Add(item); commands.Audit(context, "UnitCreated", item.Id, input.Reason); return new { resourceId = item.Id };
            }, ct);
        });
        api.MapPatch("/units/{id:guid}", ChangeUnit);
        api.MapPost("/units/{id:guid}/retire", Retire);
        api.MapPost("/units/{id:guid}/rates", ChangeRate);
        api.MapGet("/units/{id:guid}/rates", RateHistory);
        api.MapPost("/maintenance-blocks", CreateMaintenance);
    }

    private static async Task<IResult> PublicUnits(AppDbContext db, Guid facility, DateOnly today, DateOnly? start, DateOnly? end,
        decimal minArea, int limit, string? after, DateTimeOffset utc, CancellationToken ct)
    {
        var units = db.RentableUnits.AsNoTracking().Where(u => u.FacilityId == facility && u.Published && u.RetiredAt == null && u.AreaM2 >= minArea && (after == null || u.Code.CompareTo(after) > 0));
        if (start.HasValue)
            units = units.Where(u => !db.SpaceAllocations.Any(a => a.UnitId == u.Id && a.StartDate < end && start < a.EndDate
                && (a.State == "Confirmed" || a.State == "Maintenance" || a.State == "Held" && a.HoldExpiresAt > utc))
                && !db.UnitOccupancies.Any(o => o.UnitId == u.Id && o.ClearedAt == null && o.ScheduledEnd <= today));
        var rows = await (from u in units join r in db.RateVersions.AsNoTracking() on u.Id equals r.UnitId
            where r.SalesValidFrom <= today && (r.SalesValidTo == null || today < r.SalesValidTo)
            orderby u.Code select new { u.Id, u.Code, u.AreaM2, r.DailyRate, r.BillingBasis, r.MonthlyRate, r.Currency }).Take(limit + 1).ToListAsync(ct);
        return Results.Ok(new { items = rows.Take(limit).Select(x => new { x.Id, x.Code, areaM2 = Money(x.AreaM2), dailyRate = Money(x.DailyRate), x.BillingBasis, monthlyRate = x.MonthlyRate.HasValue ? Money(x.MonthlyRate.Value) : null, x.Currency }),
            nextCursor = rows.Count > limit ? rows[limit - 1].Code : null, observedAt = utc, reservation = false });
    }
    private static async Task<IResult> RateHistory(Guid id, CurrentActor actor, AppDbContext db, int? page, CancellationToken ct)
    {
        var unit = await db.RentableUnits.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if (unit == null || !await db.Users.AnyAsync(x=>x.Id==actor.UserId && x.Active,ct) || !await actor.HasFacilityAsync(unit.FacilityId,"FacilityManager",ct)) return OrganizationEndpoints.Missing();
        if (page is < 0 or > 10000) return Invalid();
        var index=page??0;
        var rows=await db.RateVersions.AsNoTracking().Where(x=>x.UnitId==id).OrderByDescending(x=>x.SalesValidFrom).Skip(index*50).Take(51).ToListAsync(ct);
        return Results.Ok(new { items=rows.Take(50).Select(x=>new { x.Id,x.UnitId,x.SalesValidFrom,x.SalesValidTo,x.BillingBasis,dailyRate=Money(x.DailyRate),monthlyRate=x.MonthlyRate.HasValue?Money(x.MonthlyRate.Value):null,x.Currency }),nextPage=rows.Count>50?(int?)(index+1):null });
    }
    private static async Task<Facility> LockFacility(AppDbContext db, Guid id, CancellationToken ct) =>
        await DatabaseDialect.Rows<Facility>(db, $"SELECT * FROM [Facilities] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct)
            ?? throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The facility is unavailable.");

    private static async Task<IResult> ChangeUnit(Guid id, UnitInput input, CurrentActor actor, AppDbContext db, UnitLockService locks, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        var known = await db.RentableUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (known is null) return OrganizationEndpoints.Missing();
        if (!await actor.HasFacilityAsync(known.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
        if (!Valid(input, out var area)) return Invalid();
        return await commands.ExecuteAsync(context, known.FacilityId, "ChangeUnit", new { id, input }, async token =>
        {
            var unit = (await locks.LockAsync(known.FacilityId, [id], token))[0];
            CommandExecutor.Version(context, unit.Version);
            if (unit.RetiredAt is not null) throw new SpaceConflict("UNIT_RETIRED");
            var utc = await locks.DatabaseUtcAsync(token); var today = Today(utc);
            if (!await db.Zones.AnyAsync(x => x.Id == input.ZoneId && x.FacilityId == unit.FacilityId, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The zone is unavailable.");
            if (area != unit.AreaM2 && (await db.SpaceAllocations.AnyAsync(x => x.UnitId == id && x.EndDate > today && (x.State == "Confirmed" || x.State == "Maintenance" || x.State == "Held" && x.HoldExpiresAt > utc), token)
                || await db.UnitOccupancies.AnyAsync(x => x.UnitId == id && x.ClearedAt == null, token))) throw new SpaceConflict("AREA_COMMITTED");
            unit.Code = input.Code.Trim(); unit.AreaM2 = area; unit.Published = input.Published; unit.ZoneId = input.ZoneId;
            commands.Audit(context, "UnitChanged", id, input.Reason); await db.SaveChangesAsync(token);
            return new { resourceId = id, version = Convert.ToBase64String(unit.Version) };
        }, ct);
    }
    private static async Task<IResult> ChangeRate(Guid id, RateInput input, CurrentActor actor, AppDbContext db, UnitLockService locks, CommandExecutor commands, HttpContext context, TimeProvider clock, CancellationToken ct)
    {
        var known = await db.RentableUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (known is null) return OrganizationEndpoints.Missing();
        if (!await actor.HasFacilityAsync(known.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
        var monthly = input.BillingBasis == "CalendarMonth";
        decimal rate = 0;
        if (input.BillingBasis is not ("Daily" or "CalendarMonth") || !Text(input.Reason, 500) || input.SalesValidFrom == DateOnly.MinValue || input.SalesValidTo <= input.SalesValidFrom
            || (monthly ? input.DailyRate != "0" || !Amount(input.MonthlyRate ?? "", 4, 99999999999999.9999m, out rate) : input.MonthlyRate != null || !Amount(input.DailyRate, 4, 99999999999999.9999m, out rate))) return Invalid();
        return await commands.ExecuteAsync(context, known.FacilityId, "SetRate", new { id, input }, async token =>
        {
            // Match payment's unit-before-directory order, then recheck the current grant before mutation.
            var unit = (await locks.LockAsync(known.FacilityId, [id], token))[0];
            await WarehouseRent.Api.Features.Invitations.IdentityWriteLock.Acquire(db, token);
            if (!await db.Users.AnyAsync(x=>x.Id==actor.UserId && x.Active,token) || !await actor.HasFacilityAsync(known.FacilityId,"FacilityManager",token)) throw new CommandFailure(404,"RESOURCE_NOT_FOUND","Unit unavailable.");
            CommandExecutor.Version(context, unit.Version);
            if (unit.RetiredAt is not null) throw new SpaceConflict("UNIT_RETIRED");
            if (input.SalesValidFrom < Today(await locks.DatabaseUtcAsync(token))) throw new CommandFailure(422, "INVALID_DATES", "Refresh the sales-validity date.");
            var existing = await db.RateVersions.Where(x => x.UnitId == id && (x.SalesValidTo == null || x.SalesValidTo > input.SalesValidFrom)
                && (input.SalesValidTo == null || x.SalesValidFrom < input.SalesValidTo)).ToListAsync(token);
            if (existing.Any(x => x.SalesValidFrom >= input.SalesValidFrom)) throw new SpaceConflict("RATE_OVERLAP");
            foreach (var prior in existing) prior.SalesValidTo = input.SalesValidFrom;
            var item = new RateVersion { Id = Guid.NewGuid(), UnitId = id, DailyRate = monthly ? 0 : rate, BillingBasis = input.BillingBasis, MonthlyRate = monthly ? rate : null, SalesValidFrom = input.SalesValidFrom, SalesValidTo = input.SalesValidTo };
            db.RateVersions.Add(item); unit.RateRevision++;
            commands.Audit(context, "RateCreated", item.Id, input.Reason); await db.SaveChangesAsync(token);
            return new { resourceId = item.Id, version = Convert.ToBase64String(unit.Version) };
        }, ct);
    }
    private static async Task<IResult> CreateMaintenance(MaintenanceInput input, CurrentActor actor, AppDbContext db, UnitLockService locks, CommandExecutor commands, HttpContext context, TimeProvider clock, CancellationToken ct)
    {
        if (!await actor.HasFacilityAsync(input.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
        if (input.UnitIds is not { Length: > 0 and <= 20 } || input.UnitIds.Distinct().Count() != input.UnitIds.Length || !Text(input.Reason, 500) || !Dates(input.StartDate, input.EndDate, DateOnly.MinValue)) return Invalid();
        return await commands.ExecuteAsync(context, input.FacilityId, "CreateMaintenance", input, async token =>
        {
            var units = await locks.LockAsync(input.FacilityId, input.UnitIds, token);
            if (units.Any(x => x.RetiredAt != null)) throw new SpaceConflict("UNIT_RETIRED");
            var utc = await locks.DatabaseUtcAsync(token);
            if (!Dates(input.StartDate, input.EndDate, Today(utc))) throw new CommandFailure(422, "INVALID_DATES", "Refresh the local rental dates.");
            if (await locks.ConflictsAsync(input.UnitIds, input.StartDate, input.EndDate, utc, token)
                || await locks.HasOverdueOccupancyAsync(input.UnitIds, Today(utc), token)) throw new SpaceConflict("SPACE_CONFLICT");
            var item = new MaintenanceBlock { Id = Guid.NewGuid(), FacilityId = input.FacilityId, StartDate = input.StartDate, EndDate = input.EndDate, Reason = input.Reason };
            db.MaintenanceBlocks.Add(item);
            foreach (var unit in input.UnitIds) db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = unit, StartDate = input.StartDate, EndDate = input.EndDate, State = "Maintenance", MaintenanceId = item.Id });
            commands.Audit(context, "MaintenanceCreated", item.Id, input.Reason); return new { resourceId = item.Id };
        }, ct);
    }
    private static async Task<IResult> Retire(Guid id, RetireUnit input, CurrentActor actor, AppDbContext db, UnitLockService locks, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        var known = await db.RentableUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (known is null || !await actor.HasFacilityAsync(known.FacilityId, null, ct)) return OrganizationEndpoints.Missing();
        if (!await actor.HasFacilityAsync(known.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
        if (!Text(input.Reason, 500) || !input.RetirementConfirmed) return Invalid();
        return await commands.ExecuteAsync(context, known.FacilityId, "RetireUnit", new { id, input }, async token =>
        {
            var unit = (await locks.LockAsync(known.FacilityId, [id], token))[0]; CommandExecutor.Version(context, unit.Version);
            if (unit.RetiredAt != null) throw new SpaceConflict("UNIT_RETIRED");
            var utc = await locks.DatabaseUtcAsync(token); var today = Today(utc);
            var grants = db.LeaseUnits.Where(x => x.UnitId == id).Select(x => x.Id);
            if (await db.SpaceAllocations.AnyAsync(x => x.UnitId == id && x.EndDate > today && (x.State == "Confirmed" || x.State == "Maintenance" || x.State == "Held" && x.HoldExpiresAt > utc), token)
                || await db.UnitOccupancies.AnyAsync(x => x.UnitId == id && x.ClearedAt == null, token)
                || await db.StockBalances.AnyAsync(x => grants.Contains(x.LeaseUnitId) && x.Quantity != 0, token)
                || await db.StockRequests.AnyAsync(x => grants.Contains(x.LeaseUnitId) && (x.State == "Draft" || x.State == "Submitted" || x.State == "Approved"), token)
                || await db.StockCounts.AnyAsync(x => grants.Contains(x.LeaseUnitId) && x.State == "Pending", token)) throw new SpaceConflict("RETIREMENT_COMMITMENTS_REMAIN");
            unit.Published = false; unit.RetiredAt = utc; unit.RetiredBy = actor.UserId; unit.RetirementReason = input.Reason.Trim();
            commands.Audit(context, "UnitRetired", id, input.Reason); await db.SaveChangesAsync(token);
            return new { resourceId = id, unit.RetiredAt, version = Convert.ToBase64String(unit.Version) };
        }, ct);
    }
}
