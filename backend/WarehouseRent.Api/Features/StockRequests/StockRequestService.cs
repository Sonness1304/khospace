using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Api.Features.StockRequests;
public sealed record RequestLineInput(Guid ItemId, string Quantity);
public sealed record CreateStockRequest(Guid OrganizationId, Guid LeaseId, Guid LeaseUnitId, string Type, RequestLineInput[] Lines, string Reason, Guid? ReplacesRequestId = null);
public sealed record StockAction(string Reason);
public sealed class StockRequestService(AppDbContext db, BookingService bookings, CommandExecutor commands, CurrentActor actor)
{
    public static object Receipt(StockRequest item) => new { resourceId = item.Id, item.State, version = Convert.ToBase64String(item.Version) };
    public static decimal Quantity(string value)
    {
        if (value is null || value.Length > 32 || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > 999999999999999.999m || decimal.Round(amount, 3) != amount)
            throw new CommandFailure(422, "INVALID_QUANTITY", "Use a positive quantity with at most three decimal places in the item's base unit.");
        return amount;
    }
    public async Task<(Lease Lease, DateTimeOffset Utc)> LockLeaseAsync(Guid id, CancellationToken ct)
    {
        var known = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        // All request/stock writers take persistent unit parents BEFORE request or balance locks.
        var aggregate = await bookings.LockAsync(known.BookingId, ct);
        var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        return (lease, aggregate.Utc);
    }
    public async Task CheckCustodyAsync(Lease lease, Guid grant, string type, DateTimeOffset utc, CancellationToken ct)
    {
        if (lease.State is not ("Active" or "ClearancePending") || !await db.UnitOccupancies.AnyAsync(x => x.LeaseUnitId == grant && x.ClearedAt == null, ct)) throw new SpaceConflict("UNIT_NOT_HANDED_OVER");
        var today = CatalogPolicy.Today(utc);
        if (lease.StartDate > today || type == "Receipt" && (lease.EndDate <= today || lease.TerminatedAt != null)) throw new SpaceConflict("RECEIPT_OUTSIDE_TERM");
    }
    public async Task<object> CreateAsync(CreateStockRequest input, HttpContext context, CancellationToken ct)
    {
        var (lease, utc) = await LockLeaseAsync(input.LeaseId, ct);
        if (lease.OrganizationId != input.OrganizationId || !await db.LeaseUnits.AnyAsync(x => x.Id == input.LeaseUnitId && x.LeaseId == lease.Id, ct)) throw new CommandFailure(404, "NOT_FOUND", "Resource not found.");
        await CheckCustodyAsync(lease, input.LeaseUnitId, input.Type, utc, ct);
        if (input.Lines is null || input.Lines.Length is < 1 or > 50 || input.Lines.Any(x => x is null) || input.Lines.Select(x => x.ItemId).Distinct().Count() != input.Lines.Length) throw new CommandFailure(422, "INVALID_LINES", "Supply one to fifty distinct items.");
        var ids = input.Lines.Select(x => x.ItemId).ToArray();
        var items = await db.Items.Where(x => x.OrganizationId == lease.OrganizationId && ids.Contains(x.Id)).ToListAsync(ct);
        if (items.Count != ids.Length) throw new CommandFailure(404, "NOT_FOUND", "Resource not found.");
        if (input.Type == "Receipt" && items.Any(x => !x.Active)) throw new SpaceConflict("ITEM_INACTIVE");
        if (input.ReplacesRequestId is { } previousId)
        {
            var previous = await db.StockRequests.SingleOrDefaultAsync(x => x.Id == previousId && x.OrganizationId == input.OrganizationId, ct);
            if (previous is null) throw new CommandFailure(404, "NOT_FOUND", "Resource not found.");
            if (previous.State != "Rejected" || previous.Type != input.Type || previous.LeaseUnitId != input.LeaseUnitId || await db.StockRequests.AnyAsync(x => x.ReplacesRequestId == previousId, ct)) throw new SpaceConflict("REPLACEMENT_NOT_ALLOWED");
        }
        var item = new StockRequest { Id = Guid.NewGuid(), OrganizationId = input.OrganizationId, FacilityId = lease.FacilityId, LeaseId = lease.Id, LeaseUnitId = input.LeaseUnitId,
            Type = input.Type, CreatedBy = actor.UserId, ReplacesRequestId = input.ReplacesRequestId, CreatedAt = utc, UpdatedAt = utc };
        db.StockRequests.Add(item);
        foreach (var line in input.Lines) db.StockRequestLines.Add(new StockRequestLine { Id = Guid.NewGuid(), StockRequestId = item.Id, OrganizationId = item.OrganizationId, ItemId = line.ItemId, Quantity = Quantity(line.Quantity) });
        commands.Audit(context, "StockRequestCreated", item.Id, input.Reason, item.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(item);
    }
    public async Task<(StockRequest Request, Lease Lease, DateTimeOffset Utc)> LockAsync(Guid id, CancellationToken ct)
    {
        var known = await db.StockRequests.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var locked = await LockLeaseAsync(known.LeaseId, ct);
        var item = await DatabaseDialect.Rows<StockRequest>(db, $"SELECT * FROM [StockRequests] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        return (item, locked.Lease, locked.Utc);
    }
    public async Task<object> ActAsync(Guid id, string action, string reason, HttpContext context, CancellationToken ct)
    {
        var (item, lease, utc) = await LockAsync(id, ct);
        CommandExecutor.Version(context, item.Version);
        var target = action switch { "submit" => "Submitted", "approve" => "Approved", "reject" => "Rejected", "cancel" => "Cancelled", _ => throw new InvalidOperationException() };
        var valid = action switch { "submit" => item.State == "Draft", "approve" => item.State is "Submitted" or "Approved", "reject" => item.State is "Submitted" or "Approved", "cancel" => item.State is "Draft" or "Submitted" or "Approved", _ => false };
        if (!valid) throw new SpaceConflict("REQUEST_STATE_CONFLICT");
        if (action == "reject" && await db.StockRequestLines.AnyAsync(x => x.StockRequestId == id && x.FulfilledQuantity > 0, ct)) throw new SpaceConflict("CANCEL_REMAINDER_REQUIRED");
        if (action is "submit" or "approve") await CheckCustodyAsync(lease, item.LeaseUnitId, item.Type, utc, ct);
        if (action == "approve" && item.Type == "Issue" && (lease.TerminatedAt != null || lease.EndDate <= CatalogPolicy.Today(utc)))
        {
            if (!await actor.HasFacilityAsync(item.FacilityId, "FacilityManager", ct)) throw new CommandFailure(403, "CLEARANCE_APPROVAL_REQUIRED", "A facility manager must authorize post-term release.");
            item.ClearanceAuthorizedAt = utc; item.ClearanceAuthorizedBy = actor.UserId;
        }
        item.State = target; item.UpdatedAt = utc;
        commands.Audit(context, "StockRequest" + target, id, reason, item.OrganizationId); await db.SaveChangesAsync(ct); return Receipt(item);
    }
}
