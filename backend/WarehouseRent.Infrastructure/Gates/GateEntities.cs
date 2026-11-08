namespace WarehouseRent.Infrastructure.Gates;

public sealed class GateLane
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public required string Code { get; set; }
    public bool Active { get; set; } = true;
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}

public sealed class GateVisit
{
    public Guid Id { get; set; }
    public Guid LaneId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid LeaseId { get; set; }
    public required string Direction { get; set; }
    public required string Vehicle { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string State { get; set; } = "Scheduled";
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ArrivedBy { get; set; }
    public DateTimeOffset? ArrivedAt { get; set; }
    public string? DepartedBy { get; set; }
    public DateTimeOffset? DepartedAt { get; set; }
    public string? CancelledBy { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public byte[] Version { get; set; } = [];
}
