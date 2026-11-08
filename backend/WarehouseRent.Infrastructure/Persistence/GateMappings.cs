using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Gates;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Infrastructure.Persistence;
public sealed partial class AppDbContext
{
    public DbSet<GateLane> GateLanes => Set<GateLane>();
    public DbSet<GateVisit> GateVisits => Set<GateVisit>();
    private static void ConfigureGates(ModelBuilder model)
    {
        model.Entity<Lease>().HasAlternateKey(x => new { x.OrganizationId, x.FacilityId, x.Id });
        model.Entity<GateLane>(e => {
            e.HasAlternateKey(x => new { x.FacilityId, x.Id });
            e.HasIndex(x => new { x.FacilityId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GateVisit>(e => {
            e.Property(x => x.Direction).HasMaxLength(16); e.Property(x => x.Vehicle).HasMaxLength(32);
            e.Property(x => x.State).HasMaxLength(16); e.Property(x => x.Version).IsRowVersion();
            e.Property(x => x.CreatedBy).HasMaxLength(450); e.Property(x => x.ArrivedBy).HasMaxLength(450);
            e.Property(x => x.DepartedBy).HasMaxLength(450); e.Property(x => x.CancelledBy).HasMaxLength(450);
            e.HasOne<GateLane>().WithMany().HasForeignKey(x => new { x.FacilityId, x.LaneId }).HasPrincipalKey(x => new { x.FacilityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lease>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.FacilityId, x.LeaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.FacilityId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.LaneId).IsUnique().HasFilter("[State]='CheckedIn'");
            e.HasIndex(x => new { x.LaneId, x.StartUtc, x.EndUtc });
            e.HasIndex(x => new { x.FacilityId, x.OrganizationId, x.StartUtc, x.Id });
            e.ToTable(t => {
                t.HasCheckConstraint("CK_GateVisit_Window", "[StartUtc]<[EndUtc] AND DATEDIFF_BIG(SECOND,[StartUtc],[EndUtc]) BETWEEN 300 AND 14400 AND DATEPART(TZOFFSET,[StartUtc])=0 AND DATEPART(TZOFFSET,[EndUtc])=0");
                t.HasCheckConstraint("CK_GateVisit_Direction", "[Direction] IN ('Inbound','Outbound')");
                t.HasCheckConstraint("CK_GateVisit_Lifecycle", "([State]='Scheduled' AND [ArrivedAt] IS NULL AND [DepartedAt] IS NULL AND [CancelledAt] IS NULL) OR ([State]='CheckedIn' AND [ArrivedAt] IS NOT NULL AND [ArrivedBy] IS NOT NULL AND [DepartedAt] IS NULL AND [CancelledAt] IS NULL) OR ([State]='Completed' AND [ArrivedAt] IS NOT NULL AND [ArrivedBy] IS NOT NULL AND [DepartedAt] IS NOT NULL AND [DepartedAt]>=[ArrivedAt] AND [DepartedBy] IS NOT NULL AND [CancelledAt] IS NULL) OR ([State]='Cancelled' AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [ArrivedAt] IS NULL AND [DepartedAt] IS NULL)");
            });
        });
    }
    private void ProtectGates()
    {
        foreach (var e in ChangeTracker.Entries<GateLane>())
            if (e.State == EntityState.Deleted || e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("Active" or "UpdatedAt")))
                throw new InvalidOperationException("Gate lane identity is permanent.");
        foreach (var e in ChangeTracker.Entries<GateVisit>()) {
            if (e.State == EntityState.Deleted) throw new InvalidOperationException("Gate visit history is permanent.");
            if (e.State != EntityState.Modified) continue;
            var before = (string)e.OriginalValues["State"]!;
            if (before != e.Entity.State && !((before == "Scheduled" && e.Entity.State is "CheckedIn" or "Cancelled") || (before == "CheckedIn" && e.Entity.State == "Completed")))
                throw new InvalidOperationException("Invalid gate visit transition.");
            foreach (var p in e.Properties.Where(p => p.IsModified))
                if (p.Metadata.Name is not ("State" or "UpdatedAt" or "ArrivedBy" or "ArrivedAt" or "DepartedBy" or "DepartedAt" or "CancelledBy" or "CancelledAt") ||
                    p.Metadata.Name is not ("State" or "UpdatedAt") && p.OriginalValue != null)
                    throw new InvalidOperationException("Gate visit identity and observed events are immutable.");
        }
    }
}
