using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Organizations;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record AddMembership(Guid OrganizationId, string UserName, string Role, string Reason);
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ChangeMembership(Guid OrganizationId, string Role, bool Active, string Reason);

public static class OrganizationEndpoints
{
    public static void MapOrganizations(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/organizations/current", ReadCurrent);
        api.MapGet("/organizations/{organizationId:guid}", ReadCurrent);
        api.MapGet("/memberships", async (Guid organizationId, int? page, CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            var current = await actor.MembershipAsync(organizationId, ct);
            if (current is null) return Missing();
            if (current.Role != "CustomerAdmin") return Forbidden();
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var total = await db.Memberships.CountAsync(x => x.OrganizationId == organizationId, ct);
            var rows = await db.Memberships.AsNoTracking().Where(x => x.OrganizationId == organizationId).OrderBy(x => x.UserId).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50)
                .Select(x => new { x.Id, x.UserId, x.Role, x.Active, x.Version }).ToListAsync(ct);
            return Results.Ok(new { items = rows, page = number, pageSize = 50, total, nextCursor = (string?)null });
        });
        api.MapPost("/memberships", (AddMembership input, MembershipService service, HttpContext context, CancellationToken ct) =>
            service.WriteAsync(context, input.OrganizationId, null, input.UserName, input.Role, true, input.Reason, "Add", ct));
        api.MapPatch("/memberships/{id:guid}", (Guid id, ChangeMembership input, MembershipService service, HttpContext context, CancellationToken ct) =>
            service.WriteAsync(context, input.OrganizationId, id, null, input.Role, input.Active, input.Reason, "Change", ct));
        api.MapDelete("/memberships/{id:guid}", (Guid id, Guid organizationId, string reason, MembershipService service, HttpContext context, CancellationToken ct) =>
            service.WriteAsync(context, organizationId, id, null, null, false, reason, "Deactivate", ct));
    }

    private static async Task<IResult> ReadCurrent(Guid organizationId, CurrentActor actor, AppDbContext db, CancellationToken ct)
    {
        var current = await actor.MembershipAsync(organizationId, ct);
        if (current is null) return Missing();
        var organization = await db.CustomerOrganizations.AsNoTracking().SingleAsync(x => x.Id == organizationId, ct);
        return Results.Ok(new { organization.Id, organization.Name, organization.Code, role = current.Role });
    }

    public static IResult Missing() => ApiFailure.Result(404, "RESOURCE_NOT_FOUND", "The requested resource is unavailable.");
    public static IResult Forbidden() => ApiFailure.Result(403, "ACTION_FORBIDDEN", "Your current membership does not allow this action.");
}
