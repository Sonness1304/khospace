using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext
{
    public DbSet<RentalQuote> Quotes => Set<RentalQuote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    private static void ConfigureQuotes(ModelBuilder model)
    {
        model.Entity<RentalQuote>(e => {
            e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.PolicyVersion).HasMaxLength(40);
            e.Property(x => x.Version).IsRowVersion(); e.Property(x => x.RentalSubtotal).HasPrecision(18, 0); e.Property(x => x.RequiredDeposit).HasPrecision(18, 0);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OrganizationId, x.CreatedAt }); e.HasIndex(x => x.ExpiresAt);
            e.ToTable(t => { t.HasCheckConstraint("CK_Quote_Dates", "[StartDate]<[EndDate] AND [CreatedAt]<[ExpiresAt]");
                t.HasCheckConstraint("CK_Quote_Amounts", "[RentalSubtotal]>=0 AND [RequiredDeposit]>=0"); });
        });
        model.Entity<QuoteLine>(e => {
            e.Property(x => x.UnitCode).HasMaxLength(40); e.Property(x => x.UnitVersion).HasMaxLength(8);
            e.Property(x => x.AreaM2).HasPrecision(12, 2); e.Property(x => x.DailyRate).HasPrecision(18, 4); e.Property(x => x.Amount).HasPrecision(18, 0);
            e.HasOne<RentalQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RateVersion>().WithMany().HasForeignKey(x => x.RateVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.QuoteId, x.UnitId, x.StartDate }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_QuoteLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>=0 AND [Amount]>=0"));
        });
    }
}
