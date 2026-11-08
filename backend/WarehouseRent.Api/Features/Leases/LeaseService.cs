using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Api.Features.Leases;

public sealed record ClearanceEvidence(bool Available, bool ZeroStock, bool NoPendingRequests);
public interface IInventoryClearance
{
    Task<ClearanceEvidence> ReadAsync(Guid leaseId, Guid[] leaseUnitIds, CancellationToken ct);
}
// WP08 registers the real stock/request reader. Missing inventory evidence cannot clear physical occupancy.
public sealed class UnavailableInventoryClearance : IInventoryClearance
{
    public Task<ClearanceEvidence> ReadAsync(Guid leaseId, Guid[] leaseUnitIds, CancellationToken ct) => Task.FromResult(new ClearanceEvidence(false, false, false));
}
public sealed class LeaseService(AppDbContext db, BookingService bookings, CommandExecutor commands, IInventoryClearance inventory)
{
    public static string EffectiveState(Lease item, DateOnly today) => item.State == "Active" && item.EndDate <= today ? "ClearancePending" : item.State;
    public static object Receipt(Lease item) => new { resourceId = item.Id, item.RentalAccountId, item.State, version = Convert.ToBase64String(item.Version) };
    public async Task<object> TerminateAsync(Guid id, HttpContext context, string reason, string actorId, CancellationToken ct)
    {
        var (item, utc) = await LockAsync(id, ct);
        CommandExecutor.Version(context, item.Version);
        if (item.TerminatedAt is not null || item.State is not ("Active" or "ClearancePending") || item.ClearedAt is not null)
            throw new SpaceConflict("TERMINATION_NOT_READY");
        var today = CatalogPolicy.Today(utc);
        var lineIds = await db.BookingLines.Where(x => x.BookingId == item.BookingId).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var allocation in await db.SpaceAllocations.Where(x => x.BookingLineId != null && lineIds.Contains(x.BookingLineId.Value) && x.State == "Confirmed" && x.EndDate > today).ToListAsync(ct))
        {
            if (allocation.StartDate < today) allocation.EndDate = today;
            else allocation.State = "Released";
        }
        var grants = await db.LeaseUnits.Where(x => x.LeaseId == id).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var occupancy in await db.UnitOccupancies.Where(x => grants.Contains(x.LeaseUnitId) && x.ClearedAt == null && x.ScheduledEnd > today).ToListAsync(ct)) occupancy.ScheduledEnd = today;
        item.State = "ClearancePending"; item.TerminatedAt = utc; item.TerminatedBy = actorId; item.TerminationReason = reason.Trim();
        commands.Audit(context, "LeaseTerminated", id, reason, item.OrganizationId);
        await db.SaveChangesAsync(ct); return Receipt(item);
    }
    public async Task<object> ConvertAsync(Guid bookingId, HttpContext context, string reason, CancellationToken ct)
    {
        var (booking, account, utc) = await bookings.LockAsync(bookingId, ct);
        var existing = await db.Leases.SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (existing is not null) return Receipt(existing);
        CommandExecutor.Version(context, booking.Version);
        if (booking.State != "Confirmed") throw new SpaceConflict("BOOKING_NOT_CONFIRMED");
        var quote = await db.Quotes.AsNoTracking().SingleAsync(x => x.Id == booking.QuoteId, ct);
        var item = new Lease { Id = Guid.NewGuid(), BookingId = bookingId, RentalAccountId = account.Id, OrganizationId = booking.OrganizationId, FacilityId = booking.FacilityId, StartDate = booking.StartDate, EndDate = booking.EndDate };
        db.Leases.Add(item); var version = new LeaseVersion { Id = Guid.NewGuid(), LeaseId = item.Id, VersionNo = 1, PolicyVersion = quote.PolicyVersion }; db.LeaseVersions.Add(version);
        var sources = await db.BookingLines.Where(x => x.BookingId == bookingId).ToListAsync(ct);
        foreach (var line in sources)
        {
            var allocation = await db.SpaceAllocations.SingleAsync(x => x.BookingLineId == line.Id && x.State == "Confirmed", ct);
            var unit = new LeaseUnit { Id = Guid.NewGuid(), LeaseId = item.Id, UnitId = line.UnitId, InitialAllocationId = allocation.Id }; db.LeaseUnits.Add(unit);
            foreach (var snapshot in await db.QuoteLines.AsNoTracking().Where(x => x.QuoteId == quote.Id && x.UnitId == line.UnitId).ToListAsync(ct))
                db.LeaseVersionLines.Add(new LeaseVersionLine { Id = Guid.NewGuid(), LeaseVersionId = version.Id, LeaseUnitId = unit.Id, SourceQuoteLineId = snapshot.Id,
                    StartDate = snapshot.StartDate, EndDate = snapshot.EndDate, Days = snapshot.Days, AreaM2 = snapshot.AreaM2, DailyRate = snapshot.DailyRate, BillingBasis = snapshot.BillingBasis, MonthlyRate = snapshot.MonthlyRate, CalendarDays = snapshot.CalendarDays, Amount = snapshot.Amount });
        }
        commands.Audit(context, "LeaseCreated", item.Id, reason, item.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(item);
    }
    private async Task<(Lease Lease, DateTimeOffset Utc)> LockAsync(Guid id, CancellationToken ct)
    {
        var known = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var locked = await bookings.LockAsync(known.BookingId, ct);
        var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        return (lease, locked.Utc);
    }
    public async Task<object> HandoverAsync(Guid id, HttpContext context, string reason, bool inspected, CancellationToken ct)
    {
        var (item, utc) = await LockAsync(id, ct);
        if (item.State == "Active") return Receipt(item);
        CommandExecutor.Version(context, item.Version);
        var today = CatalogPolicy.Today(utc);
        if (item.State != "Scheduled" || item.StartDate > today || item.EndDate <= today || !inspected) throw new SpaceConflict("HANDOVER_NOT_READY");
        var units = await db.LeaseUnits.Where(x => x.LeaseId == id).ToListAsync(ct); var ids = units.Select(x => x.UnitId).ToArray();
        if (await db.UnitOccupancies.AnyAsync(x => ids.Contains(x.UnitId) && x.ClearedAt == null, ct)) throw new SpaceConflict("PHYSICALLY_OCCUPIED");
        foreach (var unit in units) db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = unit.Id, OrganizationId = item.OrganizationId,
            ScheduledEnd = item.EndDate, HandedOverAt = utc });
        item.State = "Active"; item.ActivatedAt = utc;
        commands.Audit(context, "LeaseHandedOver", item.Id, reason, item.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(item);
    }
    public async Task<object> ClearAsync(Guid id, HttpContext context, string reason, bool inspected, CancellationToken ct)
    {
        var (item, utc) = await LockAsync(id, ct);
        if (item.State == "SettlementPending") return Receipt(item);
        CommandExecutor.Version(context, item.Version);
        if (item.State is not ("Active" or "ClearancePending") || !inspected) throw new SpaceConflict("CLEARANCE_NOT_READY");
        var ids = await db.LeaseUnits.Where(x => x.LeaseId == id).Select(x => x.Id).ToArrayAsync(ct);
        var evidence = await inventory.ReadAsync(id, ids, ct);
        if (!evidence.Available || !evidence.ZeroStock || !evidence.NoPendingRequests) throw new SpaceConflict("INVENTORY_CLEARANCE_REQUIRED");
        foreach (var occupancy in await db.UnitOccupancies.Where(x => ids.Contains(x.LeaseUnitId) && x.ClearedAt == null).ToListAsync(ct)) occupancy.ClearedAt = utc;
        item.State = "SettlementPending"; item.ClearedAt = utc;
        commands.Audit(context, "PhysicalClearance", item.Id, reason, item.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(item);
    }
}
public sealed class CancelScheduledLease(AppDbContext db) : ICancelScheduledLease
{
    public async Task CancelAsync(Guid bookingId, CancellationToken ct)
    {
        // Caller already owns sorted unit, booking and account locks in the same transaction.
        var item = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [BookingId]={bookingId}").SingleOrDefaultAsync(ct);
        if (item is null || item.State == "Cancelled") return;
        if (item.State != "Scheduled") throw new SpaceConflict("RENTAL_ALREADY_HANDED_OVER");
        item.State = "Cancelled";
    }
}
