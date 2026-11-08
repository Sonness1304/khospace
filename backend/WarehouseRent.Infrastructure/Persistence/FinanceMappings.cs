using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<PaymentReceipt> PaymentReceipts => Set<PaymentReceipt>();
    public DbSet<FundAllocation> FundAllocations => Set<FundAllocation>();
    public DbSet<DepositTransaction> DepositTransactions => Set<DepositTransaction>();
    public DbSet<ChargeStatement> ChargeStatements => Set<ChargeStatement>();
    public DbSet<ChargeStatementLine> ChargeStatementLines => Set<ChargeStatementLine>();
    private static void ConfigureFinance(ModelBuilder model)
    {
        model.Entity<RentalAccount>().Property(x => x.ZeroDepositVerifiedBy).HasMaxLength(450);
        model.Entity<PaymentReceipt>(e => {
            e.HasIndex(x => new { x.OrganizationId, x.Method, x.Reference }).IsUnique(); e.Property(x => x.Method).HasMaxLength(24); e.Property(x => x.Reference).HasMaxLength(120); e.Property(x => x.EvidenceReference).HasMaxLength(240);
            e.Property(x => x.State).HasMaxLength(16); e.Property(x => x.VerifiedBy).HasMaxLength(450); e.Property(x => x.Amount).HasPrecision(18, 0); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_Receipt_Amount", "[Amount]>0"); t.HasCheckConstraint("CK_Receipt_State", "[State] IN ('Recorded','Verified','Rejected')"); });
        });
        model.Entity<ChargeStatement>(e => {
            e.HasIndex(x => new { x.LeaseId, x.PeriodStart }).IsUnique(); e.Property(x => x.Total).HasPrecision(18, 0);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => x.LeaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_Statement_Total", "[Total]>=0"));
        });
        model.Entity<ChargeStatementLine>(e => {
            e.HasIndex(x => x.SourceLeaseVersionLineId).IsUnique(); e.Property(x => x.AreaM2).HasPrecision(12, 2); e.Property(x => x.DailyRate).HasPrecision(18, 4); e.Property(x => x.Amount).HasPrecision(18, 0);
            e.HasOne<ChargeStatement>().WithMany().HasForeignKey(x => x.ChargeStatementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaseVersionLine>().WithMany().HasForeignKey(x => x.SourceLeaseVersionLineId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_ChargeLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [Amount]>=0 AND [AreaM2]>0 AND [DailyRate]>=0"));
        });
        model.Entity<FundAllocation>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 0); e.Property(x => x.TargetType).HasMaxLength(16); e.Property(x => x.ActorId).HasMaxLength(450);
            e.HasOne<PaymentReceipt>().WithMany().HasForeignKey(x => x.PaymentReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ChargeStatement>().WithMany().HasForeignKey(x => x.ChargeStatementId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PaymentReceiptId, x.TargetType, x.ChargeStatementId });
            e.ToTable(t => { t.HasCheckConstraint("CK_FundAllocation_Amount", "[Amount]>0"); t.HasCheckConstraint("CK_FundAllocation_Target", "([TargetType]='Deposit' AND [ChargeStatementId] IS NULL) OR ([TargetType]='Charge' AND [ChargeStatementId] IS NOT NULL)"); });
        });
        model.Entity<DepositTransaction>(e => {
            e.HasIndex(x => x.FundAllocationId).IsUnique(); e.Property(x => x.Amount).HasPrecision(18, 0);
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FundAllocation>().WithMany().HasForeignKey(x => x.FundAllocationId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("CK_DepositTransaction_Positive", "[Amount]>0"));
        });
    }
    private void ProtectFinance()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ChargeStatement or ChargeStatementLine or FundAllocation or DepositTransaction or StatementAdjustment or SettlementEntry or AllocationRelease && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Issued charges and financial journals are immutable. Use linked corrections.");
            if (entry.Entity is PaymentReceipt && (entry.State == EntityState.Deleted || entry.State == EntityState.Modified && (string?)entry.OriginalValues["State"] is "Verified" or "Rejected" && entry.Properties.Any(x => x.IsModified && x.Metadata.Name != "UpdatedAt")))
                throw new InvalidOperationException("Verified receipt evidence is immutable.");
        }
    }
}
