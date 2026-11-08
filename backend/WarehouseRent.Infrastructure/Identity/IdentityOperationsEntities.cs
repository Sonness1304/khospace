namespace WarehouseRent.Infrastructure.Identity;

public sealed class AccessCredential
{
    public Guid Id { get; set; }
    public required string Kind { get; set; }
    public required string TokenHash { get; set; }
    public required string Email { get; set; }
    public string? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string? Role { get; set; }
    public required string IssuedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

// Delivery transport has no public read endpoint and no provider dispatch in the local demo.
public sealed class CredentialDelivery
{
    public Guid Id { get; set; }
    public Guid CredentialId { get; set; }
    public required string Recipient { get; set; }
    public required string Kind { get; set; }
    public required string ProtectedToken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RecoveryCooldown
{
    public required string AddressHash { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
}

public sealed class IdentityDirectoryState
{
    public int Id { get; set; }
    public byte[] Version { get; set; } = [];
    public Guid ChangeId { get; set; }
}

public sealed class IdentityOperationCommand
{
    public Guid Id { get; set; }
    public required string ActorId { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
}
