namespace WarehouseRent.Infrastructure.Stock;
public sealed class StockPosting
{
    public Guid Id { get; set; }
    public Guid? StockRequestId { get; set; }
    public Guid? FulfillmentId { get; set; }
    public bool IsFullRequest { get; set; } = true;
    public Guid? ReversesPostingId { get; set; }
    public Guid? StockCountId { get; set; }
    public string? PayloadHash { get; set; }
    public string Reason { get; set; } = "";
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public required string Type { get; set; }
    public required string PostedBy { get; set; }
    public DateTimeOffset PostedAt { get; set; }
}
public sealed class StockLedgerEntry
{
    public Guid Id { get; set; }
    public Guid StockPostingId { get; set; }
    public Guid? RequestLineId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public decimal Delta { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public required string PostedBy { get; set; }
}
public sealed class StockBalance
{
    public Guid OrganizationId { get; set; }
    public Guid ItemId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public decimal Quantity { get; set; }
    public byte[] Version { get; set; } = [];
}
