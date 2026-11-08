using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext
{
    private static void ConfigureIdentityOperations(ModelBuilder model)
    {
        model.Entity<AccessCredential>(e =>
        {
            e.ToTable("AccessCredentials", t => t.HasCheckConstraint("CK_AccessCredential_Kind", "[Kind] IN ('Invitation','Reset')"));
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(256); e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.IssuedBy).HasMaxLength(450); e.Property(x => x.Role).HasMaxLength(24);
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.Email, x.Kind, x.ExpiresAt });
            e.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CredentialDelivery>(e =>
        {
            e.ToTable("CredentialDeliveries"); e.Property(x => x.Recipient).HasMaxLength(256);
            e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.ProtectedToken).HasMaxLength(2048);
            e.HasIndex(x => x.CredentialId).IsUnique();
            e.HasOne<AccessCredential>().WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RecoveryCooldown>(e => { e.ToTable("RecoveryCooldowns"); e.HasKey(x => x.AddressHash); e.Property(x => x.AddressHash).HasMaxLength(64); });
        model.Entity<IdentityDirectoryState>(e =>
        {
            e.ToTable("IdentityDirectoryStates"); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Version).IsRowVersion();
            e.HasData(new IdentityDirectoryState { Id = 1, ChangeId = Guid.Empty });
        });
        model.Entity<IdentityOperationCommand>(e =>
        {
            e.ToTable("IdentityOperationCommands"); e.HasKey(x => new { x.ActorId, x.Id });
            e.Property(x => x.ActorId).HasMaxLength(450); e.Property(x => x.PayloadHash).HasMaxLength(64); e.Property(x => x.ResultJson).HasMaxLength(2000);
        });
    }
}
