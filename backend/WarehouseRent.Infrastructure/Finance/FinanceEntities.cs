namespace WarehouseRent.Infrastructure.Finance;
public sealed class PaymentReceipt
{
    public Guid Id { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public required string Method { get; set; }
    public required string Reference { get; set; }
    public required string EvidenceReference { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string State { get; set; } = "Recorded";
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class FundAllocation
{
    public Guid Id { get; set; }
    public Guid PaymentReceiptId { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string TargetType { get; set; }
    public Guid? ChargeStatementId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset At { get; set; }
    public required string ActorId { get; set; }
}
public sealed class DepositTransaction
{
    public Guid Id { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid FundAllocationId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset At { get; set; }
}
public sealed class ChargeStatement
{
    public Guid Id { get; set; }
    public Guid LeaseId { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public decimal Total { get; set; }
}
public sealed class ChargeStatementLine
{
    public string BillingBasis { get; set; } = "Daily";
    public decimal? MonthlyRate { get; set; }
    public int? CalendarDays { get; set; }
    public Guid Id { get; set; }
    public Guid ChargeStatementId { get; set; }
    public Guid SourceLeaseVersionLineId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Days { get; set; }
    public decimal AreaM2 { get; set; }
    public decimal DailyRate { get; set; }
    public decimal Amount { get; set; }
}
