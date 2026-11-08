using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Infrastructure.Identity;

public static class DemoIdentitySeed
{
    public static readonly Guid OrganizationA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid OrganizationB = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid FacilityA = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid FacilityB = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    // PUBLIC synthetic fixture credentials. Never used for production or a real account.
    public const string Password = "WarehouseRent_Demo!42";

    internal static async Task ApplyAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var users = new[] { "admin.a@example.test", "admin2.a@example.test", "member.a@example.test", "admin.b@example.test", "clerk@example.test", "operator@example.test", "unassigned@example.test", "manager@example.test", "sales@example.test", "finance@example.test" };
        foreach (var name in users)
        {
            if (await db.Users.AnyAsync(x => x.UserName == name, cancellationToken)) continue;
            var user = new AppUser { Id = name, UserName = name, NormalizedUserName = name.ToUpperInvariant(), Email = name,
                NormalizedEmail = name.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"), LockoutEnabled = true, OperatorAdmin = name == "operator@example.test" };
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, Password);
            db.Users.Add(user);
        }
        if (!await db.CustomerOrganizations.AnyAsync(x => x.Id == OrganizationA, cancellationToken))
            db.CustomerOrganizations.AddRange(new CustomerOrganization { Id = OrganizationA, Code = "DEMO-A", Name = "Synthetic Customer A" },
                new CustomerOrganization { Id = OrganizationB, Code = "DEMO-B", Name = "Synthetic Customer B" });
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (name, org, role) in new[] { (users[0], OrganizationA, "CustomerAdmin"), (users[1], OrganizationA, "CustomerAdmin"),
                     (users[2], OrganizationA, "Member"), (users[3], OrganizationB, "CustomerAdmin") })
            if (!await db.Memberships.AnyAsync(x => x.UserId == name && x.OrganizationId == org, cancellationToken))
                db.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = name, OrganizationId = org, Role = role });
        if (!await db.StaffFacilityScopes.AnyAsync(x => x.UserId == users[4], cancellationToken))
            db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = users[4], FacilityId = FacilityA, StaffRole = "Clerk" });
        if (!await db.StaffFacilityScopes.AnyAsync(x => x.UserId == users[7], cancellationToken))
            db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = users[7], FacilityId = FacilityA, StaffRole = "FacilityManager" });
        if (!await db.StaffFacilityScopes.AnyAsync(x => x.UserId == users[8], cancellationToken))
            db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = users[8], FacilityId = FacilityA, StaffRole = "Sales" });
        if (!await db.StaffFacilityScopes.AnyAsync(x => x.UserId == users[9], cancellationToken))
            db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = users[9], FacilityId = FacilityA, StaffRole = "Finance" });
        await db.SaveChangesAsync(cancellationToken);
    }
}
