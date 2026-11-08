namespace WarehouseRent.Infrastructure.Finance;
public sealed class PaymentIntent
{
    public Guid Id { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public required string Provider { get; set; }
    public required string Mode { get; set; }
    public required string MerchantId { get; set; }
    public required string Method { get; set; }
    public decimal Amount { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string State { get; set; } = "Created";
    public string? CheckoutUrl { get; set; }
    public string? TransactionId { get; set; }
    public string? BankTransferReference { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? PaymentReceiptId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public byte[] Version { get; set; } = [];
}
