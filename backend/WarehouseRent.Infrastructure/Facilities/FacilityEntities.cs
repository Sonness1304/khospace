namespace WarehouseRent.Infrastructure.Facilities;

public sealed class Facility
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Address { get; set; }
    public required string PermittedGoods { get; set; }
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";
    public bool Published { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class Zone
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public required string Code { get; set; }
}
public sealed class RentableUnit
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public Guid ZoneId { get; set; }
    public required string Code { get; set; }
    public decimal AreaM2 { get; set; }
    public bool Published { get; set; }
    public int RateRevision { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public string? RetiredBy { get; set; }
    public string? RetirementReason { get; set; }
    public byte[] Version { get; set; } = [];
}
public sealed class RateVersion
{
    public string BillingBasis { get; set; } = "Daily";
    public decimal? MonthlyRate { get; set; }
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public DateOnly SalesValidFrom { get; set; }
    public DateOnly? SalesValidTo { get; set; }
    public decimal DailyRate { get; set; }
    public string Currency { get; set; } = "VND";
}
public sealed class MaintenanceBlock
{
    public Guid Id { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public required string Reason { get; set; }
}
public sealed class SpaceAllocation
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public required string State { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public Guid? MaintenanceId { get; set; }
    // BookingLine FK arrives with its owning module WP05.
    public Guid? BookingLineId { get; set; }
}
// Shared predicate now; WP06 owns handover/clearance commands and LeaseUnit FK.
public sealed class UnitOccupancy
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public Guid LeaseUnitId { get; set; }
    public Guid OrganizationId { get; set; }
    public DateOnly ScheduledEnd { get; set; }
    public DateTimeOffset HandedOverAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
}
public sealed class OperationalCommand
{
    public required string ActorId { get; set; }
    public Guid ScopeId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? FacilityId { get; set; }
    public string? RequiredFacilityRole { get; set; }
    public Guid Id { get; set; }
    public required string PayloadHash { get; set; }
    public required string ResultJson { get; set; }
}
