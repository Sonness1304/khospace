using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<StatementAdjustment> StatementAdjustments => Set<StatementAdjustment>();
    public DbSet<SettlementEntry> SettlementEntries => Set<SettlementEntry>();
    public DbSet<AllocationRelease> AllocationReleases => Set<AllocationRelease>();
    private static void ConfigureSettlements(ModelBuilder model)
    {
        model.Entity<RentalAccount>().Property(x => x.ClosedBy).HasMaxLength(450);
        model.Entity<RentalAccount>().Property(x => x.ClosureReason).HasMaxLength(500);
        model.Entity<StatementAdjustment>(e => {
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.Amount).HasPrecision(18, 0); e.Property(x => x.Quantity).HasPrecision(18, 3); e.Property(x => x.UnitPrice).HasPrecision(18, 4);
            e.Property(x => x.EvidenceReference).HasMaxLength(240); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.ActorId).HasMaxLength(450);
            e.HasOne<ChargeStatement>().WithMany().HasForeignKey(x => x.ChargeStatementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseVersionLine>().WithMany().HasForeignKey(x => x.SourceLeaseVersionLineId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SourceLeaseVersionLineId).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_Adjustment_Values", "[Amount]>=0 AND (([Kind] IN ('Credit','Supplement') AND [Amount]>0 AND [SourceLeaseVersionLineId] IS NULL AND [Quantity] IS NULL AND [UnitPrice] IS NULL) OR ([Kind]='Service' AND [Amount]>0 AND [Quantity]>0 AND [UnitPrice]>0 AND [SourceLeaseVersionLineId] IS NULL) OR ([Kind]='RentSupplement' AND [SourceLeaseVersionLineId] IS NOT NULL AND [Quantity] IS NULL AND [UnitPrice] IS NULL))"));
        });
        model.Entity<SettlementEntry>(e => {
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.Amount).HasPrecision(18, 0); e.Property(x => x.Reference).HasMaxLength(120);
            e.Property(x => x.EvidenceReference).HasMaxLength(240); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.ActorId).HasMaxLength(450);
            e.HasIndex(x => new { x.OrganizationId, x.Reference }).IsUnique(); e.HasIndex(x => x.RentalAccountId);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ChargeStatement>().WithMany().HasForeignKey(x => x.ChargeStatementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentReceipt>().WithMany().HasForeignKey(x => x.PaymentReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Settlement_Values", "[Amount]>0 AND (([Kind]='Deduction' AND [ChargeStatementId] IS NOT NULL AND [PaymentReceiptId] IS NULL) OR ([Kind]='RefundDeposit' AND [ChargeStatementId] IS NULL AND [PaymentReceiptId] IS NULL) OR ([Kind]='RefundUnapplied' AND [ChargeStatementId] IS NULL AND [PaymentReceiptId] IS NOT NULL))"));
        });
        model.Entity<AllocationRelease>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 0);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StatementAdjustment>().WithMany().HasForeignKey(x => x.StatementAdjustmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FundAllocation>().WithMany().HasForeignKey(x => x.FundAllocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SettlementEntry>().WithMany().HasForeignKey(x => x.SettlementEntryId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Release_Values", "[Amount]>0 AND (([FundAllocationId] IS NOT NULL AND [SettlementEntryId] IS NULL) OR ([FundAllocationId] IS NULL AND [SettlementEntryId] IS NOT NULL))"));
        });
    }
}
