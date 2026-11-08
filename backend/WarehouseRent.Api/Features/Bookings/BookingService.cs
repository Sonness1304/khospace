using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Quotes;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Api.Features.Bookings;

public sealed class BookingService(AppDbContext db, UnitLockService locks, IDepositVerification deposits, ICancelScheduledLease leases, CommandExecutor commands)
{
    public static string EffectiveState(Booking booking, DateTimeOffset now) => booking.State == "Held" && booking.ExpiresAt <= now ? "Expired" : booking.State;
    public static object Receipt(Booking booking, RentalAccount account) => new { resourceId = booking.Id, rentalAccountId = account.Id, booking.State,
        booking.ExpiresAt, version = Convert.ToBase64String(booking.Version) };

    public async Task<object> HoldAsync(Guid quoteId, string expectedVersion, HttpContext context, CancellationToken ct)
    {
        var quote = await db.Quotes.AsNoTracking().SingleAsync(x => x.Id == quoteId, ct);
        var snapshots = await db.QuoteLines.AsNoTracking().Where(x => x.QuoteId == quoteId).ToListAsync(ct);
        var ids = snapshots.Select(x => x.UnitId).Distinct().ToArray();
        var units = await locks.LockAsync(quote.FacilityId, ids, ct);
        var utc = await locks.DatabaseUtcAsync(ct); var today = CatalogPolicy.Today(utc);
        if (expectedVersion != Convert.ToBase64String(quote.Version)) throw new CommandFailure(412, "QUOTE_VERSION_CONFLICT", "Read and accept the current quote.");
        // New keys still cannot create two live bookings from the same quote.
        var current = await db.Bookings.SingleOrDefaultAsync(x => x.QuoteId == quoteId && (x.State == "Confirmed" || x.State == "Held" && x.ExpiresAt > utc), ct);
        if (current is not null) return Receipt(current, await db.RentalAccounts.SingleAsync(x => x.BookingId == current.Id, ct));
        if (quote.ExpiresAt <= utc) throw new SpaceConflict("QUOTE_EXPIRED");
        if (!CatalogPolicy.Dates(quote.StartDate, quote.EndDate, today)) throw new CommandFailure(422, "INVALID_DATES", "Refresh rental dates before requesting a new quote.");
        var rates = await db.RateVersions.AsNoTracking().Where(x => ids.Contains(x.UnitId) && x.SalesValidFrom <= today && (x.SalesValidTo == null || today < x.SalesValidTo)).ToListAsync(ct);
        if (!QuoteEndpoints.SupportedPolicy(quote.PolicyVersion) || rates.Count != units.Count || units.Any(unit => snapshots.Any(line => line.UnitId == unit.Id && line.AreaM2 != unit.AreaM2)
            || rates.Count(rate => rate.UnitId == unit.Id && snapshots.Any(line => line.UnitId == unit.Id && line.RateVersionId == rate.Id)) != 1)) throw new SpaceConflict("REQUOTE_REQUIRED");
        if (!await db.Facilities.AnyAsync(x => x.Id == quote.FacilityId && x.Published, ct) || units.Any(x => !x.Published || x.RetiredAt != null)) throw new SpaceConflict("UNIT_INELIGIBLE");
        if (await locks.HasOverdueOccupancyAsync(ids, today, ct) || await locks.ConflictsAsync(ids, quote.StartDate, quote.EndDate, utc, ct)) throw new SpaceConflict("SPACE_CONFLICT");
        var booking = new Booking { Id = Guid.NewGuid(), QuoteId = quote.Id, OrganizationId = quote.OrganizationId, FacilityId = quote.FacilityId,
            StartDate = quote.StartDate, EndDate = quote.EndDate, HeldAt = utc, ExpiresAt = utc.AddMinutes(15), State = "Held" };
        var account = new RentalAccount { Id = Guid.NewGuid(), BookingId = booking.Id, OrganizationId = booking.OrganizationId, RequiredDeposit = quote.RequiredDeposit };
        db.Bookings.Add(booking); db.RentalAccounts.Add(account);
        foreach (var id in ids)
        {
            var line = new BookingLine { Id = Guid.NewGuid(), BookingId = booking.Id, UnitId = id, StartDate = booking.StartDate, EndDate = booking.EndDate };
            db.BookingLines.Add(line);
            db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = id, StartDate = booking.StartDate, EndDate = booking.EndDate, State = "Held", HoldExpiresAt = booking.ExpiresAt, BookingLineId = line.Id });
        }
        commands.Audit(context, "HoldCreated", booking.Id, "Explicit15-minute whole-unit hold", booking.OrganizationId); await db.SaveChangesAsync(ct);
        return Receipt(booking, account);
    }
    public async Task<(Booking Booking, RentalAccount Account, DateTimeOffset Utc)> LockAsync(Guid id, CancellationToken ct)
    {
        var known = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var units = await db.BookingLines.AsNoTracking().Where(x => x.BookingId == id).Select(x => x.UnitId).ToArrayAsync(ct);
        await locks.LockAsync(known.FacilityId, units, ct);
        var booking = await DatabaseDialect.Rows<Booking>(db, $"SELECT * FROM [Bookings] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        var account = await DatabaseDialect.Rows<RentalAccount>(db, $"SELECT * FROM [RentalAccounts] WITH (UPDLOCK,HOLDLOCK) WHERE [BookingId]={id}").SingleAsync(ct);
        return (booking, account, await locks.DatabaseUtcAsync(ct));
    }
    public async Task<object> ConfirmAsync(Guid id, string reason, HttpContext context, CancellationToken ct)
    {
        var (booking, account, utc) = await LockAsync(id, ct);
        if (booking.State == "Confirmed") return Receipt(booking, account);
        CommandExecutor.Version(context, booking.Version);
        if (EffectiveState(booking, utc) != "Held") throw new SpaceConflict("HOLD_NOT_LIVE");
        var evidence = await deposits.ReadAsync(account, ct);
        if (!evidence.Verified || evidence.Amount < account.RequiredDeposit) throw new SpaceConflict("DEPOSIT_UNVERIFIED");
        booking.State = "Confirmed"; booking.ConfirmedAt = utc;
        var lineIds = db.BookingLines.Where(x => x.BookingId == id).Select(x => x.Id);
        foreach (var allocation in await db.SpaceAllocations.Where(x => x.BookingLineId != null && lineIds.Contains(x.BookingLineId.Value)).ToListAsync(ct)) allocation.State = "Confirmed";
        commands.Audit(context, "BookingConfirmed", id, reason, booking.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(booking, account);
    }
    public async Task<object> CancelAsync(Guid id, string reason, HttpContext context, CancellationToken ct)
    {
        var (booking, account, utc) = await LockAsync(id, ct);
        if (account.State == "Closed") throw new SpaceConflict("ACCOUNT_CLOSED");
        if (booking.State == "Cancelled") return Receipt(booking, account);
        CommandExecutor.Version(context, booking.Version);
        await leases.CancelAsync(id, ct);
        var evidence = await deposits.ReadAsync(account, ct);
        booking.State = "Cancelled"; account.State = "Cancelled"; account.RefundNeeded |= evidence.Amount > 0 || evidence.HasFunds;
        var lineIds = db.BookingLines.Where(x => x.BookingId == id).Select(x => x.Id);
        foreach (var allocation in await db.SpaceAllocations.Where(x => x.BookingLineId != null && lineIds.Contains(x.BookingLineId.Value)).ToListAsync(ct)) allocation.State = "Released";
        commands.Audit(context, "RentalCancelled", id, reason, booking.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(booking, account);
    }
}
