using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<StockPosting> StockPostings => Set<StockPosting>();
    public DbSet<StockLedgerEntry> StockLedger => Set<StockLedgerEntry>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    private static void ConfigureStockPosting(ModelBuilder model)
    {
        model.Entity<StockPosting>(e => {
            e.HasIndex(x => x.StockRequestId).IsUnique().HasFilter("[StockRequestId] IS NOT NULL AND [IsFullRequest] = 1"); e.Property(x => x.Type).HasMaxLength(16); e.Property(x => x.PostedBy).HasMaxLength(450);
            e.HasIndex(x => x.FulfillmentId).IsUnique(); e.HasIndex(x => x.ReversesPostingId).IsUnique(); e.HasIndex(x => x.StockCountId).IsUnique();
            e.Property(x => x.PayloadHash).HasMaxLength(64); e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<StockPosting>().WithMany().HasForeignKey(x => x.ReversesPostingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockCount>().WithMany().HasForeignKey(x => x.StockCountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockRequest>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.StockRequestId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Posting_Source", "([Type] IN ('Receipt','Issue') AND [StockRequestId] IS NOT NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NULL) OR ([Type]='Transfer' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NULL) OR ([Type]='Reversal' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NOT NULL AND [StockCountId] IS NULL) OR ([Type]='Adjustment' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NOT NULL)"));
        });
        model.Entity<StockLedgerEntry>(e => {
            e.HasIndex(x => new { x.StockPostingId, x.RequestLineId }).IsUnique();
            e.HasIndex(x => new { x.StockPostingId, x.LeaseUnitId, x.ItemId }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.LeaseUnitId, x.PostedAt });
            e.Property(x => x.Delta).HasPrecision(18, 3); e.Property(x => x.PostedBy).HasMaxLength(450);
            e.HasOne<StockPosting>().WithMany().HasForeignKey(x => x.StockPostingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StockRequestLine>().WithMany().HasForeignKey(x => x.RequestLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Ledger_Nonzero", "[Delta]<>0"));
        });
        model.Entity<StockBalance>(e => {
            e.HasKey(x => new { x.OrganizationId, x.ItemId, x.LeaseUnitId }); e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<CustomerItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_StockBalance_Nonnegative", "[Quantity]>=0"));
        });
        model.Entity<StockCount>(e => {
            e.Property(x => x.ExpectedQuantity).HasPrecision(18, 3); e.Property(x => x.CountedQuantity).HasPrecision(18, 3);
            e.Property(x => x.ExpectedBalanceVersion).HasMaxLength(8); e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.State).HasMaxLength(16); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.ApprovedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.LeaseUnitId, x.State });
            e.HasOne<LeaseUnit>().WithMany().HasForeignKey(x => x.LeaseUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerItem>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ItemId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_Count_Quantity", "[ExpectedQuantity]>=0 AND [CountedQuantity]>=0"); t.HasCheckConstraint("CK_Count_State", "[State] IN ('Pending','Posted','Cancelled') AND ([State]<>'Posted' OR ([ApprovedAt] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [ApprovedBy]<>[CreatedBy]))"); });
        });
    }
    private void ProtectPostedStock()
    {
        if (ChangeTracker.Entries().Any(x => x.Entity is StockPosting or StockLedgerEntry && x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Posted stock is append-only. Corrections require a linked compensating document.");
        if (ChangeTracker.Entries<StockCount>().Any(x => x.State == EntityState.Deleted || x.State == EntityState.Modified && ((string?)x.OriginalValues["State"] != "Pending" || x.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("State" or "ApprovedBy" or "ApprovedAt" or "Version")))))
            throw new InvalidOperationException("Count observations are immutable; cancel stale counts and record a new observation.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { ProtectPostedStock(); ProtectFinance(); ProtectBarcodeMappings(); ProtectOnlinePayments(); ProtectGates(); ProtectTariffs(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { ProtectPostedStock(); ProtectFinance(); ProtectBarcodeMappings(); ProtectOnlinePayments(); ProtectGates(); ProtectTariffs(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
}
