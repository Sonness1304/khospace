namespace WarehouseRent.Infrastructure.Jobs;
public sealed class NoticeOutbox
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid AggregateId { get; set; }
    public required string Kind { get; set; }
    public required string BusinessKey { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
public sealed class InAppNotice
{
    public Guid Id { get; set; }
    public Guid OutboxId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public required string Kind { get; set; }
    public Guid AggregateId { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
