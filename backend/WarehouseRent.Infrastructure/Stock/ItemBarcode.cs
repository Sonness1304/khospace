namespace WarehouseRent.Infrastructure.Stock;
public sealed class ItemBarcode
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ItemId { get; set; }
    public required string Kind { get; set; }
    public required string CanonicalValue { get; set; }
    public bool Active { get; set; } = true;
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
