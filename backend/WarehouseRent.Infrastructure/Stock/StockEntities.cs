namespace WarehouseRent.Infrastructure.Stock;
public sealed class CustomerItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public required string Sku { get; set; }
    public required string Description { get; set; }
    public required string BaseUom { get; set; }
    public bool Active { get; set; } = true;
    public byte[] Version { get; set; } = [];
}
public sealed class StockRequest
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid LeaseId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public required string Type { get; set; }
    public string State { get; set; } = "Draft";
    public required string CreatedBy { get; set; }
    public Guid? ReplacesRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ClearanceAuthorizedAt { get; set; }
    public string? ClearanceAuthorizedBy { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class StockRequestLine
{
    public Guid Id { get; set; }
    public Guid StockRequestId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal FulfilledQuantity { get; set; }
}

public sealed class StockCount
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public Guid ItemId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public byte[] ExpectedBalanceVersion { get; set; } = [];
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string State { get; set; } = "Pending";
    public required string Reason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
