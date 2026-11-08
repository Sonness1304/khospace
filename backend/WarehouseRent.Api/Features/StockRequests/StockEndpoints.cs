using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Api.Features.StockRequests;
public sealed record CreateItem(Guid OrganizationId, string Sku, string Description, string BaseUom, string Reason);
public static class StockEndpoints
{
    public static void MapStockRequests(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/items", async (Guid organizationId, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (await actor.MembershipAsync(organizationId, ct) is null) return OrganizationEndpoints.Missing();
            if (search?.Length > 100) return Invalid();
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var query = db.Items.AsNoTracking().Where(x => x.OrganizationId == organizationId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Sku.Contains(search) || x.Description.Contains(search));
            var items = await query.OrderBy(x => x.Sku).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.Sku, x.Description, x.BaseUom, x.Active, version = Convert.ToBase64String(x.Version) }).ToListAsync(ct);
            return Results.Ok(new { items, page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
        api.MapPost("/items", async (CreateItem input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var member = await actor.MembershipAsync(input.OrganizationId, ct);
            if (member is null) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Sku, 64) || !CatalogPolicy.Text(input.Description, 200) || !CatalogPolicy.Text(input.BaseUom, 24) || !CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, input.OrganizationId, "CreateItem", input, async token => {
                var item = new CustomerItem { Id = Guid.NewGuid(), OrganizationId = input.OrganizationId, Sku = input.Sku.Trim().ToUpperInvariant(), Description = input.Description.Trim(), BaseUom = input.BaseUom.Trim() };
                db.Items.Add(item); commands.Audit(context, "ItemCreated", item.Id, input.Reason, input.OrganizationId); await db.SaveChangesAsync(token);
                return new { resourceId = item.Id, version = Convert.ToBase64String(item.Version) };
            }, ct, organizationId: input.OrganizationId);
        });
        api.MapPost("/stock-requests", async (CreateStockRequest input, AppDbContext db, CurrentActor actor, StockRequestService requests, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            if (await actor.MembershipAsync(input.OrganizationId, ct) is null) return OrganizationEndpoints.Forbidden();
            if (input.Type is not ("Receipt" or "Issue") || !CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.LeaseId && x.OrganizationId == input.OrganizationId, ct);
            if (lease is null || !await db.LeaseUnits.AnyAsync(x => x.Id == input.LeaseUnitId && x.LeaseId == lease.Id, ct)) return OrganizationEndpoints.Missing();
            return await commands.ExecuteAsync(context, input.OrganizationId, "CreateStockRequest", input, token => requests.CreateAsync(input, context, token), ct,
                organizationId: input.OrganizationId, facilityId: lease.FacilityId, facilityRole: "Clerk");
        });
        api.MapGet("/stock-requests/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var item = await db.StockRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || !await CanRead(item, actor, ct)) return OrganizationEndpoints.Missing();
            var lines = await (from line in db.StockRequestLines.AsNoTracking() join sku in db.Items on line.ItemId equals sku.Id where line.StockRequestId == id orderby sku.Sku select new { line.Id, line.ItemId, sku.Sku, sku.BaseUom, quantity = CatalogPolicy.Money(line.Quantity), fulfilledQuantity = CatalogPolicy.Money(line.FulfilledQuantity), remainingQuantity = CatalogPolicy.Money(line.Quantity - line.FulfilledQuantity) }).ToListAsync(ct);
            return Results.Ok(new { item.Id, item.OrganizationId, item.FacilityId, item.LeaseId, item.LeaseUnitId, item.Type, item.State, item.ReplacesRequestId, item.ClearanceAuthorizedAt, item.ClearanceAuthorizedBy, item.CreatedAt, item.UpdatedAt,
                version = Convert.ToBase64String(item.Version), lines });
        });
        api.MapGet("/stock-requests", async (Guid facilityId, Guid? organizationId, int? page, string? search, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var member = organizationId is { } org && await actor.MembershipAsync(org, ct) is not null;
            if (!member && !await actor.HasFacilityAsync(facilityId, "Clerk", ct) && !await actor.HasFacilityAsync(facilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var query = db.StockRequests.AsNoTracking().Where(x => x.FacilityId == facilityId && (organizationId == null || x.OrganizationId == organizationId));
            if (search?.Length > 100) return Invalid();
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.State.Contains(search) || x.Type.Contains(search));
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.OrganizationId, x.LeaseId, x.LeaseUnitId, x.Type, x.State, x.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new { items, page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
        api.MapPost("/stock-requests/{id:guid}/{action}", async (Guid id, string action, StockAction input, AppDbContext db, CurrentActor actor, StockRequestService requests, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            if (action is not ("submit" or "approve" or "reject" or "cancel")) return OrganizationEndpoints.Missing();
            var item = await db.StockRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || !await CanRead(item, actor, ct)) return OrganizationEndpoints.Missing();
            var permitted = action is "submit" or "cancel" ? await actor.MembershipAsync(item.OrganizationId, ct) is not null
                : await actor.HasFacilityAsync(item.FacilityId, "Clerk", ct) || await actor.HasFacilityAsync(item.FacilityId, "FacilityManager", ct);
            if (!permitted) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, item.OrganizationId, "StockRequest" + action, new { id, action, input }, token => requests.ActAsync(id, action, input.Reason, context, token), ct,
                organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: await actor.HasFacilityAsync(item.FacilityId, "Clerk", ct) ? "Clerk" : "FacilityManager");
        });
    }
    public static async Task<bool> CanRead(StockRequest item, CurrentActor actor, CancellationToken ct) => await actor.MembershipAsync(item.OrganizationId, ct) is not null || await actor.HasFacilityAsync(item.FacilityId, "Clerk", ct) || await actor.HasFacilityAsync(item.FacilityId, "FacilityManager", ct);
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_STOCK_REQUEST", "Check item fields, request type and reason.");
}
