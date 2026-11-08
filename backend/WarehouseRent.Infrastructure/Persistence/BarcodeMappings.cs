using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<ItemBarcode> ItemBarcodes => Set<ItemBarcode>();
    private static void ConfigureBarcodes(ModelBuilder model)
    {
        model.Entity<ItemBarcode>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.CanonicalValue).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.OrganizationId, x.Kind, x.CanonicalValue }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.ItemId });
            e.HasOne<CustomerItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Barcode_KindValue", "[Kind] IN ('GTIN','Internal') AND LEN([CanonicalValue]) BETWEEN 1 AND 64"));
        });
    }
    private void ProtectBarcodeMappings()
    {
        foreach (var entry in ChangeTracker.Entries<ItemBarcode>())
            if (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && entry.Properties.Any(x => x.IsModified && x.Metadata.Name is not ("Active" or "UpdatedAt")))
                throw new InvalidOperationException("Barcode mappings retain their original item and label identity. Enable or disable the original mapping instead.");
    }

}
