using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.StockRequests;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Stock;
using PostingDocument = WarehouseRent.Infrastructure.Stock.StockPosting;

namespace WarehouseRent.Api.Features.StockCorrections;

public sealed record TransferStock(Guid TransferId, Guid OrganizationId, Guid SourceLeaseUnitId, Guid DestinationLeaseUnitId, Guid ItemId, string Quantity, string Reason);
public sealed record RecordCount(Guid OrganizationId, Guid LeaseUnitId, Guid ItemId, string ExpectedQuantity, string CountedQuantity, string Reason);
public sealed record ReverseStock(string Reason, bool ReversalConfirmed = false);
public sealed record CorrectionAction(string Reason);
public sealed record StockDelta(Guid Grant, Guid Item, decimal Quantity);

public sealed class StockCorrectionService(AppDbContext db, UnitLockService units, StockLockService balances, StockRequestService requests, CurrentActor actor, CommandExecutor commands)
{
    public static decimal Nonnegative(string value)
    {
        if (value is null || value.Length > 32 || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var quantity)
            || quantity < 0 || quantity > 999999999999999.999m || decimal.Round(quantity, 3) != quantity) throw new CommandFailure(422, "INVALID_QUANTITY", "Use a nonnegative quantity with at most three decimal places.");
        return quantity;
    }
    public static object Receipt(PostingDocument posting) => new { resourceId = posting.Id, postingId = posting.Id, posting.Type };
    public static object CountReceipt(StockCount count) => new { resourceId = count.Id, count.State, version = Convert.ToBase64String(count.Version) };
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

    private async Task<(Dictionary<Guid, Lease> Leases, DateTimeOffset Utc)> LockGrantsAsync(Guid org, Guid facility, Guid[] grants, CancellationToken ct)
    {
        var grantRows = await db.LeaseUnits.AsNoTracking().Where(x => grants.Contains(x.Id)).ToListAsync(ct);
        if (grantRows.Count != grants.Distinct().Count()) throw new CommandFailure(404, "NOT_FOUND", "Lease unit not found.");
        var leaseIds = grantRows.Select(x => x.LeaseId).Distinct().ToArray();
        var known = await db.Leases.AsNoTracking().Where(x => leaseIds.Contains(x.Id)).ToListAsync(ct);
        if (known.Count != leaseIds.Length || known.Any(x => x.OrganizationId != org || x.FacilityId != facility)) throw new CommandFailure(404, "NOT_FOUND", "Lease unit not found.");
        // Acquire ALL parent units for ALL involved leases in one global order, never one lease at a time.
        var parents = await db.LeaseUnits.AsNoTracking().Where(x => leaseIds.Contains(x.LeaseId)).Select(x => x.UnitId).Distinct().ToArrayAsync(ct);
        // A transfer may span two 20-unit leases. Keep the public per-booking limit while locking
        // the union in globally ordered batches, all before any lease or stock row is locked.
        foreach (var batch in parents.Order().Chunk(20))
        {
            var lockedUnits = await units.LockAsync(facility, batch, ct);
            if (lockedUnits.Any(x => x.RetiredAt != null)) throw new SpaceConflict("UNIT_RETIRED");
        }
        var locked = new Dictionary<Guid, Lease>();
        foreach (var leaseId in leaseIds.Order()) locked[leaseId] = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={leaseId}").SingleAsync(ct);
        return (grantRows.ToDictionary(x => x.Id, x => locked[x.LeaseId]), await units.DatabaseUtcAsync(ct));
    }
    private async Task ApplyAsync(PostingDocument document, StockDelta[] deltas, CancellationToken ct)
    {
        foreach (var delta in deltas.OrderBy(x => x.Item).ThenBy(x => x.Grant))
        {
            var balance = await balances.LockAsync(document.OrganizationId, delta.Item, delta.Grant, ct); var resulting = balance.Quantity + delta.Quantity;
            if (resulting < 0) throw new SpaceConflict("INSUFFICIENT_STOCK");
            if (resulting > 999999999999999.999m) throw new CommandFailure(422, "STOCK_LIMIT", "Resulting stock exceeds supported precision.");
            balance.Quantity = resulting;
            if (delta.Quantity != 0) db.StockLedger.Add(new StockLedgerEntry { Id = Guid.NewGuid(), StockPostingId = document.Id, OrganizationId = document.OrganizationId, ItemId = delta.Item,
                LeaseUnitId = delta.Grant, Delta = delta.Quantity, PostedAt = document.PostedAt, PostedBy = actor.UserId });
        }
        db.StockPostings.Add(document);
    }
    public async Task<object> TransferAsync(TransferStock input, Guid facility, HttpContext context, CancellationToken ct)
    {
        if (input.TransferId == Guid.Empty || input.SourceLeaseUnitId == input.DestinationLeaseUnitId) throw new CommandFailure(422, "DISTINCT_GRANTS_REQUIRED", "Supply a transfer UUID and two distinct lease units.");
        var locked = await LockGrantsAsync(input.OrganizationId, facility, [input.SourceLeaseUnitId, input.DestinationLeaseUnitId], ct); var hash = Hash(input);
        var existing = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.TransferId, ct);
        if (existing is not null)
        {
            if (existing.OrganizationId != input.OrganizationId || existing.Type != "Transfer" || existing.PayloadHash != hash) throw new SpaceConflict("TRANSFER_CONFLICT");
            return Receipt(existing);
        }
        foreach (var grant in locked.Leases.Keys) await requests.CheckCustodyAsync(locked.Leases[grant], grant, "Receipt", locked.Utc, ct);
        if (!await db.Items.AnyAsync(x => x.Id == input.ItemId && x.OrganizationId == input.OrganizationId, ct)) throw new CommandFailure(404, "NOT_FOUND", "Item not found.");
        var quantity = StockRequestService.Quantity(input.Quantity);
        var document = new PostingDocument { Id = input.TransferId, OrganizationId = input.OrganizationId, FacilityId = facility, LeaseUnitId = input.SourceLeaseUnitId,
            Type = "Transfer", PostedAt = locked.Utc, PostedBy = actor.UserId, IsFullRequest = false, PayloadHash = hash, Reason = input.Reason };
        await ApplyAsync(document, [new(input.SourceLeaseUnitId, input.ItemId, -quantity), new(input.DestinationLeaseUnitId, input.ItemId, quantity)], ct);
        commands.Audit(context, "StockTransferred", document.Id, input.Reason, input.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(document);
    }
    public async Task<object> ReverseAsync(Guid id, HttpContext context, string reason, CancellationToken ct)
    {
        var original = await db.StockPostings.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        if (original.Type is not ("Receipt" or "Issue" or "Transfer")) throw new SpaceConflict("POSTING_NOT_REVERSIBLE");
        var entries = await db.StockLedger.AsNoTracking().Where(x => x.StockPostingId == id).ToListAsync(ct);
        if (entries.Count == 0) throw new SpaceConflict("POSTING_HAS_NO_MOVEMENTS");
        var locked = await LockGrantsAsync(original.OrganizationId, original.FacilityId, entries.Select(x => x.LeaseUnitId).Distinct().ToArray(), ct);
        var prior = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.ReversesPostingId == id, ct);
        if (prior is not null) return Receipt(prior);
        foreach (var entry in entries) await requests.CheckCustodyAsync(locked.Leases[entry.LeaseUnitId], entry.LeaseUnitId, entry.Delta < 0 ? "Receipt" : "Issue", locked.Utc, ct);
        var document = new PostingDocument { Id = Guid.NewGuid(), OrganizationId = original.OrganizationId, FacilityId = original.FacilityId, LeaseUnitId = original.LeaseUnitId,
            Type = "Reversal", ReversesPostingId = id, PostedAt = locked.Utc, PostedBy = actor.UserId, IsFullRequest = false, Reason = reason };
        await ApplyAsync(document, entries.Select(x => new StockDelta(x.LeaseUnitId, x.ItemId, -x.Delta)).ToArray(), ct);
        commands.Audit(context, "StockReversed", document.Id, reason, original.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(document);
    }
    public async Task<object> RecordAsync(RecordCount input, Guid facility, HttpContext context, CancellationToken ct)
    {
        var locked = await LockGrantsAsync(input.OrganizationId, facility, [input.LeaseUnitId], ct);
        await requests.CheckCustodyAsync(locked.Leases[input.LeaseUnitId], input.LeaseUnitId, "Issue", locked.Utc, ct);
        if (!await db.Items.AnyAsync(x => x.OrganizationId == input.OrganizationId && x.Id == input.ItemId, ct)) throw new CommandFailure(404, "NOT_FOUND", "Item not found.");
        var expected = Nonnegative(input.ExpectedQuantity); var counted = Nonnegative(input.CountedQuantity); var balance = await balances.LockAsync(input.OrganizationId, input.ItemId, input.LeaseUnitId, ct);
        if (balance.Quantity != expected) throw new SpaceConflict("COUNT_SNAPSHOT_STALE");
        await db.SaveChangesAsync(ct); // Materialize a missing zero balance's rowversion inside this transaction.
        var count = new StockCount { Id = Guid.NewGuid(), OrganizationId = input.OrganizationId, FacilityId = facility, LeaseUnitId = input.LeaseUnitId, ItemId = input.ItemId,
            ExpectedQuantity = expected, CountedQuantity = counted, ExpectedBalanceVersion = balance.Version.ToArray(), CreatedBy = actor.UserId, CreatedAt = locked.Utc, Reason = input.Reason };
        db.StockCounts.Add(count); commands.Audit(context, "StockCountRecorded", count.Id, input.Reason, input.OrganizationId); await db.SaveChangesAsync(ct); return CountReceipt(count);
    }
    public async Task<object> CountActionAsync(Guid id, bool approve, string reason, HttpContext context, CancellationToken ct)
    {
        var known = await db.StockCounts.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var locked = await LockGrantsAsync(known.OrganizationId, known.FacilityId, [known.LeaseUnitId], ct);
        var count = await DatabaseDialect.Rows<StockCount>(db, $"SELECT * FROM [StockCounts] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        if (approve && count.State == "Posted") return CountReceipt(count);
        CommandExecutor.Version(context, count.Version);
        if (count.State != "Pending") throw new SpaceConflict("COUNT_STATE_CONFLICT");
        if (approve)
        {
            if (actor.UserId == count.CreatedBy) throw new CommandFailure(403, "DISTINCT_APPROVER_REQUIRED", "A different manager must approve this observation.");
            await requests.CheckCustodyAsync(locked.Leases[count.LeaseUnitId], count.LeaseUnitId, "Issue", locked.Utc, ct);
            var balance = await balances.LockAsync(count.OrganizationId, count.ItemId, count.LeaseUnitId, ct);
            if (balance.Quantity != count.ExpectedQuantity || !balance.Version.SequenceEqual(count.ExpectedBalanceVersion)) throw new SpaceConflict("COUNT_SNAPSHOT_STALE");
            var document = new PostingDocument { Id = Guid.NewGuid(), OrganizationId = count.OrganizationId, FacilityId = count.FacilityId, LeaseUnitId = count.LeaseUnitId, Type = "Adjustment",
                StockCountId = count.Id, PostedAt = locked.Utc, PostedBy = actor.UserId, IsFullRequest = false, Reason = reason };
            await ApplyAsync(document, [new(count.LeaseUnitId, count.ItemId, count.CountedQuantity - count.ExpectedQuantity)], ct);
            count.State = "Posted"; count.ApprovedAt = locked.Utc; count.ApprovedBy = actor.UserId;
        }
        else
        {
            if (actor.UserId != count.CreatedBy && !await actor.HasFacilityAsync(count.FacilityId, "FacilityManager", ct)) throw new CommandFailure(403, "FORBIDDEN", "Only the recorder or facility manager can cancel a count.");
            count.State = "Cancelled";
        }
        commands.Audit(context, approve ? "StockCountApproved" : "StockCountCancelled", count.Id, reason, count.OrganizationId); await db.SaveChangesAsync(ct); return CountReceipt(count);
    }
}
