using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext
{
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<RentableUnit> RentableUnits => Set<RentableUnit>();
    public DbSet<RateVersion> RateVersions => Set<RateVersion>();
    public DbSet<MaintenanceBlock> MaintenanceBlocks => Set<MaintenanceBlock>();
    public DbSet<SpaceAllocation> SpaceAllocations => Set<SpaceAllocation>();
    public DbSet<UnitOccupancy> UnitOccupancies => Set<UnitOccupancy>();
    public DbSet<OperationalCommand> OperationalCommands => Set<OperationalCommand>();
    private static void ConfigureFacilities(ModelBuilder model)
    {
        model.Entity<Facility>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40); e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.PermittedGoods).HasMaxLength(500); e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.Version).IsRowVersion();
        });
        model.Entity<Zone>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40); e.HasIndex(x => new { x.FacilityId, x.Code }).IsUnique();
            e.HasAlternateKey(x => new { x.FacilityId, x.Id });
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RentableUnit>(e =>
        {
            e.Property(x => x.RetiredBy).HasMaxLength(450); e.Property(x => x.RetirementReason).HasMaxLength(500);
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.AreaM2).HasPrecision(12, 2);
            e.HasIndex(x => new { x.FacilityId, x.Code }).IsUnique(); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Zone>().WithMany().HasForeignKey(x => new { x.FacilityId, x.ZoneId }).HasPrincipalKey(x => new { x.FacilityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Unit_Area", "[AreaM2]>0"));
        });
        model.Entity<RateVersion>(e =>
        {
            e.Property(x => x.DailyRate).HasPrecision(18, 4); e.Property(x => x.Currency).HasMaxLength(3);
            e.HasIndex(x => new { x.UnitId, x.SalesValidFrom }).IsUnique();
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_Rate_Positive", "[DailyRate]>=0 AND [Currency]='VND'");
                t.HasCheckConstraint("CK_Rate_Dates", "[SalesValidTo] IS NULL OR [SalesValidFrom]<[SalesValidTo]"); });
        });
        model.Entity<MaintenanceBlock>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Maintenance_Dates", "[StartDate]<[EndDate]"));
        });
        model.Entity<SpaceAllocation>(e =>
        {
            e.Property(x => x.State).HasMaxLength(24); e.HasIndex(x => new { x.UnitId, x.StartDate, x.EndDate });
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MaintenanceBlock>().WithMany().HasForeignKey(x => x.MaintenanceId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => {
                t.HasCheckConstraint("CK_Allocation_Dates", "[StartDate]<[EndDate]");
                t.HasCheckConstraint("CK_Allocation_Source", "([MaintenanceId] IS NOT NULL AND [BookingLineId] IS NULL) OR ([MaintenanceId] IS NULL AND [BookingLineId] IS NOT NULL)");
                t.HasCheckConstraint("CK_Allocation_State", "[State] IN ('Held','Confirmed','Maintenance','Released') AND ([State]<>'Held' OR [HoldExpiresAt] IS NOT NULL)"); });
        });
        model.Entity<UnitOccupancy>(e =>
        {
            e.HasIndex(x => x.UnitId).IsUnique().HasFilter("[ClearedAt] IS NULL");
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OperationalCommand>(e =>
        {
            e.HasKey(x => new { x.ActorId, x.ScopeId, x.Id });
            e.Property(x => x.ActorId).HasMaxLength(450); e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.RequiredFacilityRole).HasMaxLength(24);
            e.Property(x => x.ResultJson).HasMaxLength(16000);
        });
    }
}
