using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    private const string BankTransferConstraint = "([Provider]='SePay' AND [Method]='SePayQr' AND [BankTransferReference] IS NOT NULL AND LEN([BankTransferReference])=15 AND LEFT([BankTransferReference],5)='SEVQR' AND [BankTransferReference] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2 AND [ExpiresAt] IS NOT NULL AND [ExpiresAt]>[CreatedAt] AND LEN([MerchantId])=64 AND [MerchantId] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2 AND [CheckoutUrl] IS NULL) OR ([Provider]<>'SePay' AND [BankTransferReference] IS NULL AND [ExpiresAt] IS NULL)";
    public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
    private static void ConfigureOnlinePayments(ModelBuilder model)
    {
        model.Entity<PaymentIntent>(e => {
            e.Property(x => x.Provider).HasMaxLength(16); e.Property(x => x.Mode).HasMaxLength(16); e.Property(x => x.MerchantId).HasMaxLength(64); e.Property(x => x.Method).HasMaxLength(24);
            e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.State).HasMaxLength(24); e.Property(x => x.Amount).HasPrecision(18, 0); e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.CheckoutUrl).HasMaxLength(4096); e.Property(x => x.TransactionId).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.BankTransferReference).HasMaxLength(15).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => x.BankTransferReference).IsUnique().HasFilter("[BankTransferReference] IS NOT NULL");
            e.HasIndex(x => new { x.Provider, x.Mode, x.MerchantId, x.TransactionId }).IsUnique().HasFilter("[TransactionId] IS NOT NULL");
            e.HasIndex(x => x.PaymentReceiptId).IsUnique().HasFilter("[PaymentReceiptId] IS NOT NULL"); e.HasIndex(x => new { x.RentalAccountId, x.CreatedAt, x.Id });
            e.HasOne<RentalAccount>().WithMany().HasForeignKey(x => x.RentalAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PaymentReceipt>().WithMany().HasForeignKey(x => x.PaymentReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => { t.HasCheckConstraint("CK_PaymentIntent_Amount", "[Amount] BETWEEN 1000 AND 50000000"); t.HasCheckConstraint("CK_PaymentIntent_State", "[State] IN ('Created','Creating','Ready','Unknown','Failed','Paid','ReviewRequired')"); t.HasCheckConstraint("CK_PaymentIntent_Mode", "[Mode] IN ('Sandbox','Live') AND [Provider] IN ('Vnpay','Momo','SePay') AND ([Mode]='Live' OR [PaymentReceiptId] IS NULL)");
                t.HasCheckConstraint("CK_PaymentIntent_BankTransfer", BankTransferConstraint);
            });
        });
    }
    private void ProtectOnlinePayments()
    {
        foreach (var e in ChangeTracker.Entries<PaymentIntent>())
            if (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified &&
                (p.Metadata.Name is not ("State" or "CheckoutUrl" or "TransactionId" or "PaymentReceiptId" or "PaidAt" or "UpdatedAt") ||
                (string?)e.OriginalValues["State"] is "Paid" or "ReviewRequired" && p.Metadata.Name != "UpdatedAt")))
                throw new InvalidOperationException("Payment identities and settled evidence are immutable.");
    }
}
