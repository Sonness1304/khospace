using Microsoft.AspNetCore.Identity;

namespace WarehouseRent.Infrastructure.Identity;

public sealed class AppUser : IdentityUser
{
    public bool Active { get; set; } = true;
    public bool OperatorAdmin { get; set; }
}

public sealed class CustomerOrganization
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
    public byte[] Version { get; set; } = [];
}

public sealed class Membership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string UserId { get; set; }
    public required string Role { get; set; }
    public bool Active { get; set; } = true;
    public byte[] Version { get; set; } = [];
}

// This is an authorization grant, not a Facility catalog entity (WP03).
public sealed class StaffFacilityScope
{
    public required string UserId { get; set; }
    public Guid FacilityId { get; set; }
    public required string StaffRole { get; set; }
}

public sealed class LoginSession
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string SecurityStamp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class SecurityAudit
{
    public Guid? FacilityId { get; set; }
    public Guid Id { get; set; }
    public required string ActorId { get; set; }
    public Guid? OrganizationId { get; set; }
    public required string Action { get; set; }
    public Guid AggregateId { get; set; }
    public required string CorrelationId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class MembershipCommand
{
    public Guid Id { get; set; }
    public required string ActorId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string PayloadHash { get; set; }
    public required string Operation { get; set; }
    public Guid MembershipId { get; set; }
    public required string ResultJson { get; set; }
}
