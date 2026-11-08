using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingLine> BookingLines => Set<BookingLine>();
    public DbSet<RentalAccount> RentalAccounts => Set<RentalAccount>();
    private static void ConfigureBookings(ModelBuilder model)
    {
        model.Entity<Booking>(e => {
            e.Property(x => x.State).HasMaxLength(24); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<RentalQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.QuoteId, x.State }); e.HasIndex(x => new { x.OrganizationId, x.HeldAt });
            e.ToTable(t => { t.HasCheckConstraint("CK_Booking_Dates", "[StartDate]<[EndDate] AND [HeldAt]<[ExpiresAt]");
                t.HasCheckConstraint("CK_Booking_State", "[State] IN ('Held','Confirmed','Cancelled','Expired')"); });
        });
        model.Entity<BookingLine>(e => {
            e.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentableUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.BookingId, x.UnitId }).IsUnique();
            e.HasAlternateKey(x => new { x.UnitId, x.Id });
            e.ToTable(t => t.HasCheckConstraint("CK_BookingLine_Dates", "[StartDate]<[EndDate]"));
        });
        model.Entity<RentalAccount>(e => {
            e.Property(x => x.RequiredDeposit).HasPrecision(18, 0); e.Property(x => x.State).HasMaxLength(32); e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.BookingId).IsUnique();
            e.HasOne<Booking>().WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Account_Deposit", "[RequiredDeposit]>=0"));
        });
        model.Entity<SpaceAllocation>().HasOne<BookingLine>().WithMany().HasForeignKey(x => new { x.UnitId, x.BookingLineId })
            .HasPrincipalKey(x => new { x.UnitId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
