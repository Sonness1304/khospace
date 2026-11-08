using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Api.Features.StockRequests;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.StockPosting;
public static class StockPostingEndpoints
{
    public static void MapStockPosting(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/stock-requests/{id:guid}/fulfillments", async (Guid id, FulfillStock input, AppDbContext db, CurrentActor actor, StockPostingService postings, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var request = await db.StockRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (request is null || !await StockEndpoints.CanRead(request, actor, ct)) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(request.FacilityId, "Clerk", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return ApiFailure.Result(422, "REASON_REQUIRED", "Record the observation reason.");
            return await commands.ExecuteAsync(context, request.OrganizationId, "FulfillStock", new { id, input }, token => postings.FulfillAsync(id, input, context, token), ct, organizationId: request.OrganizationId, facilityId: request.FacilityId, facilityRole: "Clerk");
        });
        api.MapPost("/stock-requests/{id:guid}/post", async (Guid id, PostStock input, AppDbContext db, CurrentActor actor, StockPostingService postings, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var request = await db.StockRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (request is null || !await StockEndpoints.CanRead(request, actor, ct)) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(request.FacilityId, "Clerk", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return ApiFailure.Result(422, "REASON_REQUIRED", "Record the observation reason.");
            return await commands.ExecuteAsync(context, request.OrganizationId, "PostStock", new { id, input }, token => postings.PostAsync(id, input, context, token), ct, organizationId: request.OrganizationId, facilityId: request.FacilityId, facilityRole: "Clerk");
        });
        api.MapGet("/stock/balances", async (Guid organizationId, Guid leaseUnitId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (!await CanRead(organizationId, leaseUnitId, db, actor, ct)) return OrganizationEndpoints.Missing();
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var query = db.StockBalances.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.LeaseUnitId == leaseUnitId);
            var items = await (from balance in query join item in db.Items on balance.ItemId equals item.Id orderby item.Sku, item.Id select new { itemId = item.Id, item.Sku, item.BaseUom, balance.LeaseUnitId, quantity = CatalogPolicy.Money(balance.Quantity) }).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { items, page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
        api.MapGet("/stock/ledger", async (Guid organizationId, Guid leaseUnitId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (!await CanRead(organizationId, leaseUnitId, db, actor, ct)) return OrganizationEndpoints.Missing();
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var query = db.StockLedger.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.LeaseUnitId == leaseUnitId);
            var items = await (from row in query join item in db.Items on row.ItemId equals item.Id orderby row.PostedAt descending, row.Id select new { row.Id, row.StockPostingId, row.RequestLineId, itemId = item.Id, item.Sku, item.BaseUom, row.LeaseUnitId, delta = CatalogPolicy.Money(row.Delta), row.PostedAt, row.PostedBy }).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { items, page = number, pageSize = 50, total = await query.CountAsync(ct) });
        });
    }
    private static async Task<bool> CanRead(Guid org, Guid grant, AppDbContext db, CurrentActor actor, CancellationToken ct)
    {
        var lease = await (from unit in db.LeaseUnits join rental in db.Leases on unit.LeaseId equals rental.Id where unit.Id == grant && rental.OrganizationId == org select rental).AsNoTracking().SingleOrDefaultAsync(ct);
        return lease is not null && (await actor.MembershipAsync(org, ct) is not null || await actor.HasFacilityAsync(lease.FacilityId, "Clerk", ct) || await actor.HasFacilityAsync(lease.FacilityId, "FacilityManager", ct));
    }
}
