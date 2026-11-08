namespace WarehouseRent.Infrastructure.Rentals;
public sealed class Lease
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid RentalAccountId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string State { get; set; } = "Scheduled";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
    public DateTimeOffset? TerminatedAt { get; set; }
    public string? TerminatedBy { get; set; }
    public string? TerminationReason { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class LeaseUnit
{
    public Guid Id { get; set; }
    public Guid LeaseId { get; set; }
    public Guid UnitId { get; set; }
    public Guid InitialAllocationId { get; set; }
}
public sealed class LeaseVersion
{
    public Guid Id { get; set; }
    public Guid LeaseId { get; set; }
    public int VersionNo { get; set; }
    public required string PolicyVersion { get; set; }
}
public sealed class LeaseVersionLine
{
    public string BillingBasis { get; set; } = "Daily";
    public decimal? MonthlyRate { get; set; }
    public int? CalendarDays { get; set; }
    public Guid Id { get; set; }
    public Guid LeaseVersionId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public Guid SourceQuoteLineId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Days { get; set; }
    public decimal AreaM2 { get; set; }
    public decimal DailyRate { get; set; }
    public decimal Amount { get; set; }
}
