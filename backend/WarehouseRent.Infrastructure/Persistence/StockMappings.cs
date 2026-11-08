using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<CustomerItem> Items => Set<CustomerItem>();
    public DbSet<StockRequest> StockRequests => Set<StockRequest>();
    public DbSet<StockRequestLine> StockRequestLines => Set<StockRequestLine>();
    private static void ConfigureStock(ModelBuilder model)
    {
        model.Entity<CustomerItem>(e => {
            e.HasAlternateKey(x => new { x.OrganizationId, x.Id });
            e.HasIndex(x => new { x.OrganizationId, x.Sku }).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(64); e.Property(x => x.Description).HasMaxLength(200); e.Property(x => x.BaseUom).HasMaxLength(24); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<StockRequest>(e => {
            e.HasAlternateKey(x => new { x.OrganizationId, x.Id });
            e.HasIndex(x => x.ReplacesRequestId).IsUnique().HasFilter("[ReplacesRequestId] IS NOT NULL");
            e.HasIndex(x => new { x.LeaseUnitId, x.State });
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockRequest>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ReplacesRequestId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Type).HasMaxLength(16); e.Property(x => x.State).HasMaxLength(24); e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.ClearanceAuthorizedBy).HasMaxLength(450); e.Property(x => x.Version).IsRowVersion();
            e.ToTable(t => { t.HasCheckConstraint("CK_StockRequest_Type", "[Type] IN ('Receipt','Issue')"); t.HasCheckConstraint("CK_StockRequest_State", "[State] IN ('Draft','Submitted','Approved','Rejected','Cancelled','Fulfilled')"); });
        });
        model.Entity<StockRequestLine>(e => {
            e.HasIndex(x => new { x.StockRequestId, x.ItemId }).IsUnique();
            e.HasOne<CustomerItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockRequest>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.StockRequestId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.FulfilledQuantity).HasPrecision(18, 3);
            e.ToTable(t => t.HasCheckConstraint("CK_StockRequestLine_Quantity", "[Quantity]>0 AND [FulfilledQuantity]>=0 AND [FulfilledQuantity]<=[Quantity]"));
        });
    }
}
