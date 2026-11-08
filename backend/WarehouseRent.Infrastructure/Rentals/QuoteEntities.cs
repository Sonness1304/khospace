namespace WarehouseRent.Infrastructure.Rentals;

public sealed class RentalQuote
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal RentalSubtotal { get; set; }
    public decimal RequiredDeposit { get; set; }
    public required string PolicyVersion { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class QuoteLine
{
    public string BillingBasis { get; set; } = "Daily";
    public decimal? MonthlyRate { get; set; }
    public int? CalendarDays { get; set; }
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public Guid UnitId { get; set; }
    public required string UnitCode { get; set; }
    public byte[] UnitVersion { get; set; } = [];
    public Guid RateVersionId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Days { get; set; }
    public decimal AreaM2 { get; set; }
    public decimal DailyRate { get; set; }
    public decimal Amount { get; set; }
}
