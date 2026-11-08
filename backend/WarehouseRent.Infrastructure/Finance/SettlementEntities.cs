namespace WarehouseRent.Infrastructure.Finance;

public sealed class StatementAdjustment
{
    public Guid Id { get; set; }
    public Guid ChargeStatementId { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid? SourceLeaseVersionLineId { get; set; }
    public required string Kind { get; set; }
    public decimal Amount { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public required string EvidenceReference { get; set; }
    public required string Reason { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset At { get; set; }
}
public sealed class SettlementEntry
{
    public Guid Id { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ChargeStatementId { get; set; }
    public Guid? PaymentReceiptId { get; set; }
    public required string Kind { get; set; }
    public decimal Amount { get; set; }
    public required string Reference { get; set; }
    public required string EvidenceReference { get; set; }
    public required string Reason { get; set; }
    public required string ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
}
public sealed class AllocationRelease
{
    public Guid Id { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid StatementAdjustmentId { get; set; }
    public Guid? FundAllocationId { get; set; }
    public Guid? SettlementEntryId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset At { get; set; }
}
