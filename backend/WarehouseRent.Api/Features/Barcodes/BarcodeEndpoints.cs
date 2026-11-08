using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Api.Features.Barcodes;
public static class BarcodeEndpoints
{
    public sealed record RegisterBarcode(Guid OrganizationId, string Kind, string Value, string Reason);
    public sealed record ChangeBarcode(Guid OrganizationId, bool Active, string Reason);
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 500;
    private static IResult Invalid() => ApiFailure.Result(422, "BARCODE_INVALID", "Select GTIN (8/12/13/14 digits with valid check digit) or an Internal label (1-64 ASCII letters/digits, dot, slash, underscore or hyphen), and supply a reason.");
    private static async Task<IResult?> Membership(AppDbContext db, CurrentActor actor, Guid org, bool write, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)) return OrganizationEndpoints.Missing();
        var member = await actor.MembershipAsync(org, ct);
        return member is null ? OrganizationEndpoints.Missing() : write && member.Role != "CustomerAdmin" ? OrganizationEndpoints.Forbidden() : null;
    }
    public static void MapBarcodes(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/items/{itemId:guid}/barcodes", async (Guid itemId, Guid organizationId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var denied = await Membership(db, actor, organizationId, false, ct); if (denied is not null) return denied;
            if (!await db.Items.AnyAsync(x => x.Id == itemId && x.OrganizationId == organizationId, ct)) return OrganizationEndpoints.Missing();
            var number = page ?? 1; if (number is < 1 or > 10000) return Invalid();
            var query = db.ItemBarcodes.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.ItemId == itemId);
            return Results.Ok(new { page = number, total = await query.CountAsync(ct), items = await query.OrderBy(x => x.Kind).ThenBy(x => x.CanonicalValue).ThenBy(x => x.Id)
                .Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.ItemId, x.Kind, x.CanonicalValue, x.Active, x.CreatedAt, x.UpdatedAt, version = Convert.ToBase64String(x.Version) }).ToListAsync(ct) });
        });
        api.MapPost("/items/{itemId:guid}/barcodes", async (Guid itemId, RegisterBarcode input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, TimeProvider time, CancellationToken ct) =>
        {
            var denied = await Membership(db, actor, input.OrganizationId, true, ct); if (denied is not null) return denied;
            var canonical = BarcodeValue.Canonical(input.Kind, input.Value); if (canonical is null || !Reason(input.Reason)) return Invalid();
            return await commands.ExecuteAsync(context, input.OrganizationId, "RegisterBarcode", new { itemId, input }, async token =>
            {
                await IdentityWriteLock.Acquire(db, token);
                var current = await Membership(db, actor, input.OrganizationId, true, token); if (current is not null) throw new CommandFailure(403, "ACTION_FORBIDDEN", "Current customer administrator access is required.");
                if (!await db.Items.AnyAsync(x => x.Id == itemId && x.OrganizationId == input.OrganizationId && x.Active, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The item is unavailable.");
                if (await db.ItemBarcodes.AnyAsync(x => x.OrganizationId == input.OrganizationId && x.Kind == input.Kind && x.CanonicalValue == canonical, token))
                    throw new CommandFailure(409, "BARCODE_EXISTS", "This label already belongs to an item. Use the original mapping; labels cannot be reassigned.");
                var now = time.GetUtcNow(); var barcode = new ItemBarcode { Id = Guid.NewGuid(), OrganizationId = input.OrganizationId, ItemId = itemId, Kind = input.Kind,
                    CanonicalValue = canonical, CreatedBy = actor.UserId, CreatedAt = now, UpdatedAt = now };
                db.ItemBarcodes.Add(barcode); commands.Audit(context, "BarcodeRegistered", barcode.Id, input.Reason, input.OrganizationId);
                await db.SaveChangesAsync(token); return new { resourceId = barcode.Id, barcode.ItemId, barcode.Kind, barcode.CanonicalValue, barcode.Active, version = Convert.ToBase64String(barcode.Version) };
            }, ct, organizationId: input.OrganizationId);
        });
        api.MapPost("/barcodes/{id:guid}/status", async (Guid id, ChangeBarcode input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, TimeProvider time, CancellationToken ct) =>
        {
            var denied = await Membership(db, actor, input.OrganizationId, true, ct); if (denied is not null) return denied;
            if (!Reason(input.Reason)) return Invalid();
            if (!await db.ItemBarcodes.AnyAsync(x => x.Id == id && x.OrganizationId == input.OrganizationId, ct)) return OrganizationEndpoints.Missing();
            return await commands.ExecuteAsync(context, input.OrganizationId, "BarcodeStatus", new { id, input }, async token =>
            {
                await IdentityWriteLock.Acquire(db, token);
                if (await Membership(db, actor, input.OrganizationId, true, token) is not null) throw new CommandFailure(403, "ACTION_FORBIDDEN", "Current customer administrator access is required.");
                var barcode = await db.ItemBarcodes.SingleAsync(x => x.Id == id && x.OrganizationId == input.OrganizationId, token); CommandExecutor.Version(context, barcode.Version);
                if (input.Active && !await db.Items.AnyAsync(x => x.Id == barcode.ItemId && x.OrganizationId == input.OrganizationId && x.Active, token)) throw new CommandFailure(409, "ITEM_INACTIVE", "The original item must be active before enabling its label.");
                barcode.Active = input.Active; barcode.UpdatedAt = time.GetUtcNow(); commands.Audit(context, "BarcodeStatusChanged", barcode.Id, input.Reason, input.OrganizationId);
                await db.SaveChangesAsync(token); return new { resourceId = barcode.Id, barcode.ItemId, barcode.Active, version = Convert.ToBase64String(barcode.Version) };
            }, ct, organizationId: input.OrganizationId);
        });
        api.MapGet("/leases/{leaseId:guid}/barcode-lookup", async (Guid leaseId, string kind, string value, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
            if (!await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)) return OrganizationEndpoints.Missing();
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == leaseId, ct);
            if (lease is null || !await db.CustomerOrganizations.AnyAsync(x => x.Id == lease.OrganizationId && x.Active, ct)) return OrganizationEndpoints.Missing();
            if (await actor.MembershipAsync(lease.OrganizationId, ct) is null && !await actor.HasFacilityAsync(lease.FacilityId, "Clerk", ct) && !await actor.HasFacilityAsync(lease.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Missing();
            var canonical = BarcodeValue.Canonical(kind, value); if (canonical is null) return Invalid();
            var found = await (from barcode in db.ItemBarcodes.AsNoTracking() join item in db.Items on new { barcode.OrganizationId, Id = barcode.ItemId } equals new { item.OrganizationId, item.Id }
                where barcode.OrganizationId == lease.OrganizationId && barcode.Kind == kind && barcode.CanonicalValue == canonical && barcode.Active && item.Active
                select new { barcodeId = barcode.Id, itemId = item.Id, item.Sku, item.Description, item.BaseUom, barcode.CanonicalValue, barcode.Kind, itemVersion = Convert.ToBase64String(item.Version), barcodeVersion = Convert.ToBase64String(barcode.Version) }).SingleOrDefaultAsync(ct);
            if (found is null) return OrganizationEndpoints.Missing();
            await tx.CommitAsync(ct);
            return Results.Ok(new { leaseId, lease.OrganizationId, lease.FacilityId, found.barcodeId, found.itemId, found.Sku, found.Description, found.BaseUom, found.CanonicalValue, found.Kind, found.itemVersion, found.barcodeVersion, selectionOnly = true });
        });
    }
}
