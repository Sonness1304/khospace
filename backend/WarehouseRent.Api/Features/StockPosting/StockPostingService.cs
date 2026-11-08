using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Leases;
using WarehouseRent.Api.Features.StockRequests;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Stock;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace WarehouseRent.Api.Features.StockPosting;
public sealed record ObservedLine(Guid RequestLineId, string Quantity);
public sealed record PostStock(string Reason, ObservedLine[] Lines);
public sealed record FulfillStock(Guid FulfillmentId, string Reason, ObservedLine[] Lines);
public sealed class StockPostingService(AppDbContext db, StockRequestService requests, StockLockService balances, CommandExecutor commands, CurrentActor actor)
{
    public async Task<object> PostAsync(Guid id, PostStock input, HttpContext context, CancellationToken ct)
    {
        var (request, lease, utc) = await requests.LockAsync(id, ct);
        var lines = await db.StockRequestLines.Where(x => x.StockRequestId == id).OrderBy(x => x.ItemId).ToListAsync(ct);
        if (input.Lines is null || input.Lines.Length != lines.Count || input.Lines.Any(x => x is null) || input.Lines.Select(x => x.RequestLineId).Distinct().Count() != lines.Count)
            throw new CommandFailure(422, "COMPLETE_OBSERVATION_REQUIRED", "Observe every approved line exactly once.");
        foreach (var line in lines)
        {
            var observed = input.Lines.SingleOrDefault(x => x.RequestLineId == line.Id);
            if (observed is null || StockRequestService.Quantity(observed.Quantity) != line.Quantity) throw new SpaceConflict("CUSTOMER_REPLACEMENT_REQUIRED");
        }
        var existing = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.StockRequestId == id && x.IsFullRequest, ct);
        if (existing is not null) return Receipt(existing, request);
        CommandExecutor.Version(context, request.Version);
        if (lines.Any(x => x.FulfilledQuantity > 0)) throw new SpaceConflict("PARTIAL_FULFILLMENT_EXISTS");
        var posting = await PostLinesAsync(request, lease, utc, lines.Select(x => (x, x.Quantity)).ToArray(), input.Reason, true, null, null, context, ct);
        return Receipt(posting, request);
    }
    public async Task<object> FulfillAsync(Guid id, FulfillStock input, HttpContext context, CancellationToken ct)
    {
        var (request, lease, utc) = await requests.LockAsync(id, ct);
        if (input.FulfillmentId == Guid.Empty || input.Lines is not { Length: > 0 and <= 50 } || input.Lines.Any(x => x is null) || input.Lines.Select(x => x.RequestLineId).Distinct().Count() != input.Lines.Length)
            throw new CommandFailure(422, "INVALID_FULFILLMENT", "Supply a fulfillment UUID and distinct positive observed lines.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, input }))));
        var existing = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.FulfillmentId == input.FulfillmentId, ct);
        if (existing is not null)
        {
            if (existing.StockRequestId != id || existing.PayloadHash != hash) throw new SpaceConflict("FULFILLMENT_CONFLICT");
            return PartialReceipt(existing);
        }
        CommandExecutor.Version(context, request.Version);
        var lines = await db.StockRequestLines.Where(x => x.StockRequestId == id).OrderBy(x => x.ItemId).ToListAsync(ct);
        if (input.Lines.Any(x => !lines.Any(l => l.Id == x.RequestLineId))) throw new CommandFailure(404, "NOT_FOUND", "Request line not found.");
        var observed = lines.Where(x => input.Lines.Any(l => l.RequestLineId == x.Id)).Select(x => (Line: x, Quantity: StockRequestService.Quantity(input.Lines.Single(l => l.RequestLineId == x.Id).Quantity))).ToArray();
        if (observed.Any(x => x.Quantity > x.Line.Quantity - x.Line.FulfilledQuantity)) throw new SpaceConflict("FULFILLMENT_EXCEEDS_APPROVED");
        var posting = await PostLinesAsync(request, lease, utc, observed, input.Reason, false, input.FulfillmentId, hash, context, ct);
        return PartialReceipt(posting);
    }
    private static object PartialReceipt(WarehouseRent.Infrastructure.Stock.StockPosting posting) => new { resourceId = posting.StockRequestId, postingId = posting.Id, posting.FulfillmentId };
    private async Task<WarehouseRent.Infrastructure.Stock.StockPosting> PostLinesAsync(StockRequest request, WarehouseRent.Infrastructure.Rentals.Lease lease, DateTimeOffset utc,
        (StockRequestLine Line, decimal Quantity)[] observed, string reason, bool full, Guid? fulfillmentId, string? hash, HttpContext context, CancellationToken ct)
    {
        if (request.State != "Approved") throw new SpaceConflict("REQUEST_NOT_APPROVED");
        await requests.CheckCustodyAsync(lease, request.LeaseUnitId, request.Type, utc, ct);
        if (request.Type == "Issue" && (lease.TerminatedAt != null || lease.EndDate <= CatalogPolicy.Today(utc))
            && (request.ClearanceAuthorizedAt is null || lease.TerminatedAt != null && request.ClearanceAuthorizedAt < lease.TerminatedAt)) throw new SpaceConflict("CLEARANCE_APPROVAL_REQUIRED");
        var itemIds = observed.Select(x => x.Line.ItemId).ToArray();
        if (request.Type == "Receipt" && await db.Items.AnyAsync(x => itemIds.Contains(x.Id) && !x.Active, ct)) throw new SpaceConflict("ITEM_INACTIVE");
        var documentId = Guid.NewGuid();
        var posting = new WarehouseRent.Infrastructure.Stock.StockPosting { Id = documentId, StockRequestId = request.Id, OrganizationId = request.OrganizationId, FacilityId = request.FacilityId, LeaseUnitId = request.LeaseUnitId,
            Type = request.Type, PostedBy = actor.UserId, PostedAt = utc, Reason = reason, IsFullRequest = full, FulfillmentId = fulfillmentId ?? documentId, PayloadHash = hash };
        db.StockPostings.Add(posting);
        foreach (var (line, quantity) in observed)
        {
            var balance = await balances.LockAsync(request.OrganizationId, line.ItemId, request.LeaseUnitId, ct);
            var delta = request.Type == "Receipt" ? quantity : -quantity;
            var resulting = balance.Quantity + delta;
            if (resulting < 0) throw new SpaceConflict("INSUFFICIENT_STOCK");
            if (resulting > 999999999999999.999m) throw new CommandFailure(422, "STOCK_LIMIT", "The resulting balance exceeds supported quantity precision.");
            balance.Quantity = resulting;
            line.FulfilledQuantity += quantity;
            db.StockLedger.Add(new StockLedgerEntry { Id = Guid.NewGuid(), StockPostingId = posting.Id, RequestLineId = line.Id, OrganizationId = request.OrganizationId,
                ItemId = line.ItemId, LeaseUnitId = request.LeaseUnitId, Delta = delta, PostedAt = utc, PostedBy = actor.UserId });
        }
        // Query tracked lines so a subset posting leaves the remainder approved.
        var allLines = await db.StockRequestLines.Where(x => x.StockRequestId == request.Id).ToListAsync(ct);
        request.State = allLines.All(x => x.FulfilledQuantity == x.Quantity) ? "Fulfilled" : "Approved"; request.UpdatedAt = utc;
        commands.Audit(context, "StockPosted", request.Id, reason, request.OrganizationId); await db.SaveChangesAsync(ct); return posting;
    }
    private static object Receipt(WarehouseRent.Infrastructure.Stock.StockPosting posting, StockRequest request) => new { resourceId = request.Id, postingId = posting.Id, request.State, version = Convert.ToBase64String(request.Version) };
}
public sealed class InventoryClearance(AppDbContext db) : IInventoryClearance
{
    // Caller holds every rental unit parent and lease lock; request/posting writers use the same parents.
    public async Task<ClearanceEvidence> ReadAsync(Guid leaseId, Guid[] leaseUnitIds, CancellationToken ct)
        => new(true, !await db.StockBalances.AnyAsync(x => leaseUnitIds.Contains(x.LeaseUnitId) && x.Quantity != 0, ct),
            !await db.StockRequests.AnyAsync(x => x.LeaseId == leaseId && (x.State == "Draft" || x.State == "Submitted" || x.State == "Approved"), ct)
            && !await db.StockCounts.AnyAsync(x => leaseUnitIds.Contains(x.LeaseUnitId) && x.State == "Pending", ct));
}
