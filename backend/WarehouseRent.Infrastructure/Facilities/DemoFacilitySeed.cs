using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Infrastructure.Facilities;

public static class DemoFacilitySeed
{
    internal static async Task ApplyAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var (id, prefix) in new[] { (DemoIdentitySeed.FacilityA, "A"), (DemoIdentitySeed.FacilityB, "B") })
        {
            if (!await db.Facilities.AnyAsync(x => x.Id == id, ct))
                db.Facilities.Add(new Facility { Id = id, Code = $"DEMO-{prefix}", Name = $"Synthetic {prefix} Logistics", Address = "Synthetic demo location, Ho Chi Minh City", PermittedGoods = "Dry, non-hazardous packaged goods only", Published = true });
            var zoneId = Guid.Parse($"{prefix.ToLowerInvariant()}0000000-0000-4000-8000-000000000000");
            if (!await db.Zones.AnyAsync(x => x.Id == zoneId, ct)) db.Zones.Add(new Zone { Id = zoneId, FacilityId = id, Code = "DRY" });
            await db.SaveChangesAsync(ct);
            for (var index = 1; index <= 4; index++)
            {
                var unitId = Guid.Parse($"{prefix.ToLowerInvariant()}000000{index}-0000-4000-8000-00000000000{index}");
                if (await db.RentableUnits.AnyAsync(x => x.Id == unitId, ct)) continue;
                db.RentableUnits.Add(new RentableUnit { Id = unitId, FacilityId = id, ZoneId = zoneId, Code = $"{prefix}-{index:00}", AreaM2 = index == 1 ? 10.5m : index * 10m, Published = index != 4 });
                db.RateVersions.Add(new RateVersion { Id = Guid.NewGuid(), UnitId = unitId, SalesValidFrom = new DateOnly(2026, 1, 1), DailyRate = index == 1 ? 101m : 5000m });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
