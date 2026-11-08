using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Jobs;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<NoticeOutbox> NoticeOutbox => Set<NoticeOutbox>();
    public DbSet<InAppNotice> InAppNotices => Set<InAppNotice>();
    private static void ConfigureNotices(ModelBuilder model)
    {
        model.Entity<NoticeOutbox>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.BusinessKey).HasMaxLength(160); e.Property(x => x.Message).HasMaxLength(500);
            e.HasIndex(x => x.BusinessKey).IsUnique(); e.HasIndex(x => new { x.FacilityId, x.DeliveredAt, x.CreatedAt });
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<InAppNotice>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.Message).HasMaxLength(500);
            e.HasIndex(x => x.OutboxId).IsUnique(); e.HasIndex(x => new { x.OrganizationId, x.FacilityId, x.CreatedAt });
            e.HasOne<NoticeOutbox>().WithMany().HasForeignKey(x => x.OutboxId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
