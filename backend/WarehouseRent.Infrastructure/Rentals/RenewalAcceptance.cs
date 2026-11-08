namespace WarehouseRent.Infrastructure.Rentals;
public sealed class RenewalAcceptance
{
    public Guid Id { get; set; }
    public Guid LeaseId { get; set; }
    public Guid QuoteId { get; set; }
    public required string ExpectedLeaseVersion { get; set; }
    public required string ExpectedQuoteVersion { get; set; }
    public required string AcceptedBy { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public string? AppliedResultJson { get; set; }
    public byte[] Version { get; set; } = [];
}
