using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed class UnitLockService(AppDbContext db)
{
    public async Task<IReadOnlyList<RentableUnit>> LockAsync(Guid facilityId, IEnumerable<Guid> unitIds, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Unit locks require the caller's transaction.");
        var ids = unitIds.Distinct().Order().ToArray();
        if (ids.Length is 0 or > 20 || ids.Contains(Guid.Empty)) throw new SpaceConflict("INVALID_UNIT_SELECTION");
        var units = new List<RentableUnit>();
        foreach (var id in ids)
        {
            var unit = await DatabaseDialect.Rows<RentableUnit>(db, $"SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleOrDefaultAsync(ct);
            if (unit is null || unit.FacilityId != facilityId) throw new SpaceConflict("UNIT_UNAVAILABLE");
            units.Add(unit);
        }
        return units;
    }
    public async Task<DateTimeOffset> DatabaseUtcAsync(CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Authoritative allocation time requires a locked transaction.");
        return await DatabaseDialect.UtcNowAsync(db, ct);
    }
    public Task<bool> ConflictsAsync(Guid[] unitIds, DateOnly start, DateOnly end, DateTimeOffset utc, CancellationToken ct = default) =>
        db.SpaceAllocations.AnyAsync(x => unitIds.Contains(x.UnitId) && x.StartDate < end && start < x.EndDate
            && (x.State == "Confirmed" || x.State == "Maintenance" || x.State == "Held" && x.HoldExpiresAt > utc), ct);
    public Task<bool> HasOverdueOccupancyAsync(Guid[] unitIds, DateOnly today, CancellationToken ct = default) =>
        db.UnitOccupancies.AnyAsync(x => unitIds.Contains(x.UnitId) && x.ClearedAt == null && x.ScheduledEnd <= today, ct);
}
public sealed class SpaceConflict(string code) : Exception(code) { public string Code { get; } = code; }
