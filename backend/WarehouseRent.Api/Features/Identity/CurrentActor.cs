using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Identity;

public sealed class CurrentActor(AppDbContext db, IHttpContextAccessor accessor)
{
    public Task<bool> IsOperatorAdminAsync(CancellationToken ct) => db.Users.AnyAsync(x => x.Id == UserId && x.Active && x.OperatorAdmin, ct);
    public string UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated actor required.");

    // Always read current membership/grants; no cookie role or selected client context is authoritative.
    public Task<Membership?> MembershipAsync(Guid organizationId, CancellationToken ct) => db.Memberships.AsNoTracking()
        .Where(x => x.OrganizationId == organizationId && x.UserId == UserId && x.Active)
        .Where(x => db.CustomerOrganizations.Any(org => org.Id == x.OrganizationId && org.Active))
        .SingleOrDefaultAsync(ct);

    public async Task<bool> HasFacilityAsync(Guid facilityId, string? actionRole, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(x => x.Id == UserId && x.Active && x.OperatorAdmin, ct)) return true;
        return await db.StaffFacilityScopes.AnyAsync(x => x.UserId == UserId && x.FacilityId == facilityId
            && (actionRole == null || x.StaffRole == actionRole), ct);
    }
}
