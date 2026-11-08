using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<FoundationMarker> FoundationMarkers => Set<FoundationMarker>();
    public DbSet<CustomerOrganization> CustomerOrganizations => Set<CustomerOrganization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<StaffFacilityScope> StaffFacilityScopes => Set<StaffFacilityScope>();
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<SecurityAudit> SecurityAudits => Set<SecurityAudit>();
    public DbSet<MembershipCommand> MembershipCommands => Set<MembershipCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureFacilities(modelBuilder);
        ConfigureQuotes(modelBuilder);
        ConfigureBookings(modelBuilder);
        ConfigureLeases(modelBuilder);
        ConfigureStock(modelBuilder);
        ConfigureStockPosting(modelBuilder);
        ConfigureFinance(modelBuilder);
        ConfigureSettlements(modelBuilder);
        ConfigureIdentityOperations(modelBuilder);
        ConfigureDocuments(modelBuilder);
        ConfigureNotices(modelBuilder);
        ConfigureBarcodes(modelBuilder);
        ConfigureOnlinePayments(modelBuilder);
        ConfigureGates(modelBuilder);
        ConfigureTariffs(modelBuilder);
        modelBuilder.Entity<CustomerOrganization>(entity =>
        {
            entity.Property(x => x.Code).HasMaxLength(40);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Version).IsRowVersion();
        });
        modelBuilder.Entity<Membership>(entity =>
        {
            entity.Property(x => x.Role).HasMaxLength(24);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => new { x.OrganizationId, x.UserId }).IsUnique();
            entity.HasOne<CustomerOrganization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_Membership_Role", "[Role] IN ('CustomerAdmin','Member')"));
        });
        modelBuilder.Entity<StaffFacilityScope>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.FacilityId, x.StaffRole });
            entity.Property(x => x.StaffRole).HasMaxLength(24);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<LoginSession>(entity =>
        {
            entity.Property(x => x.SecurityStamp).HasMaxLength(128);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });
        modelBuilder.Entity<SecurityAudit>(entity =>
        {
            entity.Property(x => x.ActorId).HasMaxLength(450);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasIndex(x => new { x.AggregateId, x.At });
            entity.HasIndex(x => new { x.FacilityId, x.At });
        });
        modelBuilder.Entity<MembershipCommand>(entity =>
        {
            entity.HasKey(x => new { x.ActorId, x.OrganizationId, x.Id });
            entity.Property(x => x.ActorId).HasMaxLength(450);
            entity.Property(x => x.PayloadHash).HasMaxLength(64);
            entity.Property(x => x.Operation).HasMaxLength(24);
            entity.Property(x => x.ResultJson).HasMaxLength(2000);
        });
        modelBuilder.Entity<FoundationMarker>(entity =>
        {
            entity.ToTable("FoundationMarkers");
            entity.HasKey(marker => marker.Id);
            entity.Property(marker => marker.Id).HasMaxLength(64);
            entity.Property(marker => marker.Description).HasMaxLength(200);
        });
        if (Database.IsNpgsql()) ConfigurePostgresPreview(modelBuilder);
    }
}

// A bootstrap provenance record, not a facility, customer, lease, payment or stock fixture.
public sealed class FoundationMarker
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
