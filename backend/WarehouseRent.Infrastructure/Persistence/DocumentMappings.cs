using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Documents;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<PrivateDocument> PrivateDocuments => Set<PrivateDocument>();
    private static void ConfigureDocuments(ModelBuilder model)
    {
        model.Entity<PrivateDocument>(e =>
        {
            e.Property(x => x.StorageKey).HasMaxLength(50); e.HasIndex(x => x.StorageKey).IsUnique();
            e.Property(x => x.FileName).HasMaxLength(150); e.Property(x => x.ContentType).HasMaxLength(32);
            e.Property(x => x.Sha256).HasMaxLength(64); e.Property(x => x.UploadedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.LeaseId, x.UploadedAt });
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Document_Content", "DATALENGTH([Content]) BETWEEN 1 AND 10485760 AND [ContentType] IN ('image/png','image/jpeg','application/pdf')"));
        });
    }
}
