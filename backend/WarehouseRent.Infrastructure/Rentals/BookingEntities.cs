namespace WarehouseRent.Infrastructure.Rentals;

public sealed class Booking
{
    public Guid Id { get; set; }
    public Guid QuoteId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTimeOffset HeldAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public required string State { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class BookingLine
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid UnitId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}
public sealed class RentalAccount
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Guid OrganizationId { get; set; }
    public decimal RequiredDeposit { get; set; }
    public DateTimeOffset? ZeroDepositVerifiedAt { get; set; }
    public string? ZeroDepositVerifiedBy { get; set; }
    public bool RefundNeeded { get; set; }
    public string State { get; set; } = "Open";
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public string? ClosureReason { get; set; }
    public byte[] Version { get; set; } = [];
}
