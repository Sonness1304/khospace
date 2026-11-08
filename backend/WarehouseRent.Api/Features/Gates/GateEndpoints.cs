using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Gates;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Gates;

public static class GateEndpoints
{
    public sealed record CreateLane(Guid FacilityId, string Code, string Reason);
    public sealed record LaneStatus(bool Active, string Reason);
    public sealed record ScheduleVisit(Guid LaneId, Guid LeaseId, string Direction, string Vehicle, string StartUtc, string EndUtc, string Reason);
    public sealed record VisitAction(string Reason);
    private static bool Reason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 500;
    private static object LaneView(GateLane lane) => new { lane.Id, resourceId = lane.Id, lane.FacilityId, lane.Code, lane.Active, version = Convert.ToBase64String(lane.Version) };
    private static object VisitView(GateVisit visit) => new { visit.Id, resourceId = visit.Id, visit.LaneId, visit.FacilityId, visit.OrganizationId, visit.LeaseId, visit.Direction, visit.Vehicle,
        visit.StartUtc, visit.EndUtc, visit.State, visit.CreatedAt, visit.ArrivedAt, visit.DepartedAt, visit.CancelledAt, version = Convert.ToBase64String(visit.Version) };
    private static bool TryUtc(string? value, out DateTimeOffset utc)
    {
        utc = default;
        return value != null && (value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal)) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out utc) && utc.Offset == TimeSpan.Zero;
    }
    private static IResult Invalid() => ApiFailure.Result(422, "GATE_INPUT_INVALID", "Use a reason, a valid lane/lease and UTC window of5-240 minutes within the next90 days.");
    private static async Task<bool> Staff(AppDbContext db, CurrentActor actor, Guid facility, bool manager, CancellationToken ct)
        => await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct) &&
            (await actor.HasFacilityAsync(facility, "FacilityManager", ct) || !manager && await actor.HasFacilityAsync(facility, "Clerk", ct));
    private static async Task<bool> Access(AppDbContext db, CurrentActor actor, Guid org, Guid facility, CancellationToken ct)
        => await Staff(db, actor, facility, false, ct) || await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct) && await actor.MembershipAsync(org, ct) != null;
    private static async Task<bool> ReadLanes(AppDbContext db, CurrentActor actor, Guid facility, Guid? org, CancellationToken ct)
        => await Staff(db, actor, facility, false, ct) || org.HasValue && await Access(db, actor, org.Value, facility, ct) && await db.Leases.AnyAsync(x => x.OrganizationId == org && x.FacilityId == facility && x.State == "Active", ct);
    private static Task<GateLane> LockLane(AppDbContext db, Guid id, CancellationToken ct) => DatabaseDialect.Rows<GateLane>(db, $"SELECT * FROM [GateLanes] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
    private static async Task<DateTimeOffset> Now(AppDbContext db, CancellationToken ct) => await DatabaseDialect.UtcNowAsync(db, ct);
    public static void MapGates(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/gate-lanes", async (Guid facilityId, Guid? organizationId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (!await ReadLanes(db, actor, facilityId, organizationId, ct)) return OrganizationEndpoints.Missing();
            var number = page ?? 1; if (number is < 1 or > 10000) return Invalid();
            var query = db.GateLanes.AsNoTracking().Where(x => x.FacilityId == facilityId);
            var rows = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { facilityId, page = number, total = await query.CountAsync(ct), items = rows.Select(LaneView) });
        });
        api.MapGet("/gate-lanes/{id:guid}", async (Guid id, Guid? organizationId, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var lane = await db.GateLanes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return lane != null && await ReadLanes(db, actor, lane.FacilityId, organizationId, ct) ? Results.Ok(LaneView(lane)) : OrganizationEndpoints.Missing();
        });
        api.MapPost("/gate-lanes", async (CreateLane input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext ctx, CancellationToken ct) => {
            if (!await Staff(db, actor, input.FacilityId, true, ct)) return OrganizationEndpoints.Missing();
            if (input.Code is not { Length: >= 1 and <= 40 } || !input.Code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_') || !Reason(input.Reason)) return Invalid();
            return await commands.ExecuteAsync(ctx, input.FacilityId, "CreateGateLane", input, async token => {
                await IdentityWriteLock.Acquire(db, token);
                if (!await Staff(db, actor, input.FacilityId, true, token) || !await db.Facilities.AnyAsync(x => x.Id == input.FacilityId, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Facility unavailable.");
                var now = await Now(db, token); var lane = new GateLane { Id = Guid.NewGuid(), FacilityId = input.FacilityId, Code = input.Code, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.GateLanes.Add(lane); commands.Audit(ctx, "GateLaneCreated", lane.Id, input.Reason, facility: lane.FacilityId); await db.SaveChangesAsync(token); return LaneView(lane);
            }, ct, facilityId: input.FacilityId);
        });
        api.MapPost("/gate-lanes/{id:guid}/status", async (Guid id, LaneStatus input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext ctx, CancellationToken ct) => {
            var known = await db.GateLanes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (known == null || !await Staff(db, actor, known.FacilityId, true, ct)) return OrganizationEndpoints.Missing(); if (!Reason(input.Reason)) return Invalid();
            return await commands.ExecuteAsync(ctx, known.FacilityId, "ChangeGateLane", new { id, input }, async token => {
                await IdentityWriteLock.Acquire(db, token); if (!await Staff(db, actor, known.FacilityId, true, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Lane unavailable.");
                var lane = await LockLane(db, id, token); CommandExecutor.Version(ctx, lane.Version);
                if (!input.Active && await db.GateVisits.AnyAsync(x => x.LaneId == id && (x.State == "Scheduled" || x.State == "CheckedIn"), token)) throw new CommandFailure(409, "GATE_HAS_VISITS", "Cancel scheduled visits and record departures before disabling the lane.");
                lane.Active = input.Active; lane.UpdatedAt = await Now(db, token); commands.Audit(ctx, "GateLaneStatusChanged", id, input.Reason, facility: lane.FacilityId); await db.SaveChangesAsync(token); return LaneView(lane);
            }, ct, facilityId: known.FacilityId);
        });
        api.MapPost("/gate-visits", Schedule);
        api.MapGet("/gate-visits/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var visit = await db.GateVisits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return visit != null && await Access(db, actor, visit.OrganizationId, visit.FacilityId, ct) ? Results.Ok(VisitView(visit)) : OrganizationEndpoints.Missing();
        });
        api.MapGet("/gate-visits", List);
        api.MapPost("/gate-visits/{id:guid}/{action}", Change);
    }
    private static async Task<IResult> Schedule(ScheduleVisit input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext ctx, CancellationToken ct)
    {
        var known = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.LeaseId, ct);
        if (known == null || !await Access(db, actor, known.OrganizationId, known.FacilityId, ct)) return OrganizationEndpoints.Missing();
        if (!TryUtc(input.StartUtc, out var startUtc) || !TryUtc(input.EndUtc, out var endUtc) || !Reason(input.Reason) || input.Direction is not ("Inbound" or "Outbound") || input.Vehicle is not { Length: >= 1 and <= 32 } || string.IsNullOrWhiteSpace(input.Vehicle) ||
            !input.Vehicle.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '.' or '-') || endUtc - startUtc < TimeSpan.FromMinutes(5) || endUtc - startUtc > TimeSpan.FromMinutes(240)) return Invalid();
        if (!await db.GateLanes.AnyAsync(x => x.Id == input.LaneId && x.FacilityId == known.FacilityId, ct)) return OrganizationEndpoints.Missing();
        return await commands.ExecuteAsync(ctx, known.OrganizationId, "ScheduleGateVisit", input, async token => {
            // No commercial writer takes lane locks. Identity -> lane -> lease serializes gates and lease termination without changing stock.
            await IdentityWriteLock.Acquire(db, token);
            if (!await Access(db, actor, known.OrganizationId, known.FacilityId, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Lease unavailable.");
            var lane = await LockLane(db, input.LaneId, token);
            var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={input.LeaseId}").SingleAsync(token);
            var now = await Now(db, token);
            if (!lane.Active || lease.State != "Active") throw new CommandFailure(409, "GATE_LEASE_UNAVAILABLE", "An active lane and lease are required.");
            if (startUtc < now || startUtc > now.AddDays(90)) throw new CommandFailure(422, "GATE_WINDOW_INVALID", "The visit must start in the next90 days.");
            var timezone = await db.Facilities.Where(x => x.Id == known.FacilityId).Select(x => x.TimeZoneId).SingleAsync(token);
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new CommandFailure(409, "GATE_TIMEZONE_UNAVAILABLE", "Facility timezone requires operator correction."); }
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(startUtc, zone).DateTime);
            var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(endUtc.AddTicks(-1), zone).DateTime);
            if (first < lease.StartDate || last >= lease.EndDate) throw new CommandFailure(422, "GATE_OUTSIDE_LEASE", "The whole visit must be inside facility-local lease dates.");
            if (await db.GateVisits.AnyAsync(x => x.LaneId == lane.Id && (x.State == "Scheduled" || x.State == "CheckedIn") && x.StartUtc < endUtc && startUtc < x.EndUtc, token))
                throw new CommandFailure(409, "GATE_WINDOW_OCCUPIED", "This gate lane is already reserved for that window.");
            var visit = new GateVisit { Id = Guid.NewGuid(), LaneId = lane.Id, FacilityId = lease.FacilityId, OrganizationId = lease.OrganizationId, LeaseId = lease.Id, Direction = input.Direction,
                Vehicle = input.Vehicle, StartUtc = startUtc, EndUtc = endUtc, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
            db.GateVisits.Add(visit); commands.Audit(ctx, "GateVisitScheduled", visit.Id, input.Reason, visit.OrganizationId, visit.FacilityId); await db.SaveChangesAsync(token); return VisitView(visit);
        }, ct, organizationId: known.OrganizationId, facilityId: known.FacilityId, facilityRole: await Staff(db, actor, known.FacilityId, true, ct) ? "FacilityManager" : "Clerk");
    }
    private static async Task<IResult> List(Guid facilityId, Guid? organizationId, string startUtc, string endUtc, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct)
    {
        var staff = await Staff(db, actor, facilityId, false, ct);
        if (!staff && (!organizationId.HasValue || !await Access(db, actor, organizationId.Value, facilityId, ct))) return OrganizationEndpoints.Missing();
        var number = page ?? 1;
        if (!DateTimeOffset.TryParse(startUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !DateTimeOffset.TryParse(endUtc, CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
            !startUtc.EndsWith('Z') || !endUtc.EndsWith('Z') || end <= start || end - start > TimeSpan.FromDays(31) || number is < 1 or > 10000) return Invalid();
        var query = db.GateVisits.AsNoTracking().Where(x => x.FacilityId == facilityId && (x.State == "CheckedIn" || x.StartUtc < end && start < x.EndUtc));
        if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId);
        var rows = await query.OrderBy(x => x.StartUtc).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
        return Results.Ok(new { facilityId, organizationId, page = number, total = await query.CountAsync(ct), items = rows.Select(VisitView) });
    }
    private static async Task<IResult> Change(Guid id, string action, VisitAction input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext ctx, CancellationToken ct)
    {
        if (action is not ("cancel" or "arrive" or "depart")) return OrganizationEndpoints.Missing();
        var known = await db.GateVisits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (known == null || !(action == "cancel" ? await Access(db, actor, known.OrganizationId, known.FacilityId, ct) : await Staff(db, actor, known.FacilityId, false, ct))) return OrganizationEndpoints.Missing();
        if (!Reason(input.Reason)) return Invalid();
        return await commands.ExecuteAsync(ctx, known.OrganizationId, "GateVisit-" + action, new { id, input }, async token => {
            await IdentityWriteLock.Acquire(db, token);
            if (!(action == "cancel" ? await Access(db, actor, known.OrganizationId, known.FacilityId, token) : await Staff(db, actor, known.FacilityId, false, token))) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Visit unavailable.");
            var lane = await LockLane(db, known.LaneId, token);
            var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={known.LeaseId}").SingleAsync(token);
            var visit = await DatabaseDialect.Rows<GateVisit>(db, $"SELECT * FROM [GateVisits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(token);
            CommandExecutor.Version(ctx, visit.Version); var now = await Now(db, token);
            if (action == "cancel") {
                if (visit.State != "Scheduled") throw new CommandFailure(409, "GATE_STATE_CONFLICT", "Only scheduled visits can be cancelled.");
                visit.State = "Cancelled"; visit.CancelledAt = now; visit.CancelledBy = actor.UserId;
            } else if (action == "arrive") {
                if (visit.State != "Scheduled" || !lane.Active || lease.State != "Active" || now < visit.StartUtc || now >= visit.EndUtc ||
                    await db.GateVisits.AnyAsync(x => x.LaneId == lane.Id && x.State == "CheckedIn", token)) throw new CommandFailure(409, "GATE_ARRIVAL_BLOCKED", "Check the visit window, lease and actual lane occupancy.");
                visit.State = "CheckedIn"; visit.ArrivedAt = now; visit.ArrivedBy = actor.UserId;
            } else {
                if (visit.State != "CheckedIn") throw new CommandFailure(409, "GATE_STATE_CONFLICT", "Only an arrived vehicle can depart.");
                visit.State = "Completed"; visit.DepartedAt = now; visit.DepartedBy = actor.UserId;
            }
            visit.UpdatedAt = now; commands.Audit(ctx, "GateVisit-" + action, id, input.Reason, visit.OrganizationId, visit.FacilityId); await db.SaveChangesAsync(token); return VisitView(visit);
        }, ct, organizationId: known.OrganizationId, facilityId: known.FacilityId, facilityRole: await Staff(db, actor, known.FacilityId, true, ct) ? "FacilityManager" : "Clerk");
    }
}
