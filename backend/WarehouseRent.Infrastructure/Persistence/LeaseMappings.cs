using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<Lease> Leases => Set<Lease>();
    public DbSet<LeaseUnit> LeaseUnits => Set<LeaseUnit>();
    public DbSet<LeaseVersion> LeaseVersions => Set<LeaseVersion>();
    public DbSet<LeaseVersionLine> LeaseVersionLines => Set<LeaseVersionLine>();
    public DbSet<RenewalAcceptance> RenewalAcceptances => Set<RenewalAcceptance>();
    private static void ConfigureLeases(ModelBuilder model)
    {
        model.Entity<RenewalAcceptance>(e => {
            e.HasIndex(x => new { x.LeaseId, x.QuoteId }).IsUnique();
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentalQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ExpectedLeaseVersion).HasMaxLength(32); e.Property(x => x.ExpectedQuoteVersion).HasMaxLength(32);
            e.Property(x => x.AcceptedBy).HasMaxLength(450); e.Property(x => x.Version).IsRowVersion();
        });
        model.Entity<Lease>(e => {
            e.Property(x => x.TerminatedBy).HasMaxLength(450); e.Property(x => x.TerminationReason).HasMaxLength(500);
            e.HasIndex(x => x.BookingId).IsUnique(); e.HasIndex(x => x.RentalAccountId).IsUnique();
            e.Property(x => x.State).HasMaxLength(32); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_Lease_Dates", "[StartDate]<[EndDate]");
                t.HasCheckConstraint("CK_Lease_State", "[State] IN ('Scheduled','Active','Cancelled','ClearancePending','SettlementPending','Closed')"); });
        });
        model.Entity<LeaseUnit>(e => {
            e.HasIndex(x => new { x.LeaseId, x.UnitId }).IsUnique(); e.HasAlternateKey(x => new { x.UnitId, x.Id });
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SpaceAllocation>().WithMany().HasForeignKey(x => x.InitialAllocationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaseVersion>(e => {
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.LeaseId, x.VersionNo }).IsUnique(); e.Property(x => x.PolicyVersion).HasMaxLength(40);
        });
        model.Entity<LeaseVersionLine>(e => {
            e.HasOne<LeaseVersion>().WithMany().HasForeignKey(x => x.LeaseVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QuoteLine>().WithMany().HasForeignKey(x => x.SourceQuoteLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.LeaseVersionId, x.SourceQuoteLineId }).IsUnique();
            e.Property(x => x.AreaM2).HasPrecision(12, 2); e.Property(x => x.DailyRate).HasPrecision(18, 4); e.Property(x => x.Amount).HasPrecision(18, 0);
            e.ToTable(t => t.HasCheckConstraint("CK_LeaseLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>=0 AND [Amount]>=0"));
        });
        model.Entity<UnitOccupancy>().HasOne<LeaseUnit>().WithMany().HasForeignKey(x => new { x.UnitId, x.LeaseUnitId })
            .HasPrincipalKey(x => new { x.UnitId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        model.Entity<UnitOccupancy>().HasIndex(x => x.LeaseUnitId).IsUnique();
    }
}
