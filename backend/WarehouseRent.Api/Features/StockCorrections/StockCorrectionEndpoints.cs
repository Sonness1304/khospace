using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.StockCorrections;

public static class StockCorrectionEndpoints
{
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_CORRECTION", "Supply the reason, valid UUIDs, quantities and required confirmation.");
    private static async Task<bool> CanRead(Guid org, Guid facility, CurrentActor actor, CancellationToken ct) => await actor.MembershipAsync(org, ct) is not null || await actor.HasFacilityAsync(facility, "Clerk", ct) || await actor.HasFacilityAsync(facility, "FacilityManager", ct);
    public static void MapStockCorrections(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/stock").RequireAuthorization();
        api.MapPost("/transfers", async (TransferStock input, AppDbContext db, CurrentActor actor, StockCorrectionService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var lease = await (from grant in db.LeaseUnits join rental in db.Leases on grant.LeaseId equals rental.Id where grant.Id == input.SourceLeaseUnitId && rental.OrganizationId == input.OrganizationId select rental).AsNoTracking().SingleOrDefaultAsync(ct);
            if (lease is null || !await CanRead(lease.OrganizationId, lease.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(lease.FacilityId, "Clerk", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, input.OrganizationId, "TransferStock", input, token => service.TransferAsync(input, lease.FacilityId, context, token), ct, organizationId: input.OrganizationId, facilityId: lease.FacilityId, facilityRole: "Clerk");
        });
        api.MapPost("/postings/{id:guid}/reverse", async (Guid id, ReverseStock input, AppDbContext db, CurrentActor actor, StockCorrectionService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var posting = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (posting is null || !await CanRead(posting.OrganizationId, posting.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            if (!await actor.HasFacilityAsync(posting.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500) || !input.ReversalConfirmed) return Invalid();
            return await commands.ExecuteAsync(context, posting.OrganizationId, "ReverseStock", new { id, input }, token => service.ReverseAsync(id, context, input.Reason, token), ct, organizationId: posting.OrganizationId, facilityId: posting.FacilityId, facilityRole: "FacilityManager");
        });
        api.MapPost("/counts", async (RecordCount input, AppDbContext db, CurrentActor actor, StockCorrectionService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var lease = await (from grant in db.LeaseUnits join rental in db.Leases on grant.LeaseId equals rental.Id where grant.Id == input.LeaseUnitId && rental.OrganizationId == input.OrganizationId select rental).AsNoTracking().SingleOrDefaultAsync(ct);
            if (lease is null || !await CanRead(lease.OrganizationId, lease.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            var clerk = await actor.HasFacilityAsync(lease.FacilityId, "Clerk", ct);
            if (!clerk && !await actor.HasFacilityAsync(lease.FacilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, input.OrganizationId, "RecordStockCount", input, token => service.RecordAsync(input, lease.FacilityId, context, token), ct, organizationId: input.OrganizationId, facilityId: lease.FacilityId, facilityRole: clerk ? "Clerk" : "FacilityManager");
        });
        api.MapPost("/counts/{id:guid}/{action}", async (Guid id, string action, CorrectionAction input, AppDbContext db, CurrentActor actor, StockCorrectionService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            if (action is not ("approve" or "cancel")) return OrganizationEndpoints.Missing();
            var count = await db.StockCounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (count is null || !await CanRead(count.OrganizationId, count.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            var manager = await actor.HasFacilityAsync(count.FacilityId, "FacilityManager", ct);
            if (!manager && (action == "approve" || !await actor.HasFacilityAsync(count.FacilityId, "Clerk", ct))) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, count.OrganizationId, "StockCount" + action, new { id, input }, token => service.CountActionAsync(id, action == "approve", input.Reason, context, token), ct, organizationId: count.OrganizationId, facilityId: count.FacilityId, facilityRole: manager ? "FacilityManager" : "Clerk");
        });
        api.MapGet("/counts/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var count = await db.StockCounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (count is null || !await CanRead(count.OrganizationId, count.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            var postingId = await db.StockPostings.Where(x => x.StockCountId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            return Results.Ok(new { count.Id, count.OrganizationId, count.FacilityId, count.LeaseUnitId, count.ItemId, count.State, count.CreatedBy, count.CreatedAt, count.ApprovedBy, count.ApprovedAt, count.Reason,
                expectedQuantity = CatalogPolicy.Money(count.ExpectedQuantity), countedQuantity = CatalogPolicy.Money(count.CountedQuantity), version = Convert.ToBase64String(count.Version), postingId });
        });
        api.MapGet("/postings/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var posting = await db.StockPostings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (posting is null || !await CanRead(posting.OrganizationId, posting.FacilityId, actor, ct)) return OrganizationEndpoints.Missing();
            var entries = await db.StockLedger.AsNoTracking().Where(x => x.StockPostingId == id).OrderBy(x => x.LeaseUnitId).ThenBy(x => x.ItemId).Select(x => new { x.Id, x.LeaseUnitId, x.ItemId, delta = CatalogPolicy.Money(x.Delta) }).ToListAsync(ct);
            var reversedBy = await db.StockPostings.Where(x => x.ReversesPostingId == id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            return Results.Ok(new { posting.Id, posting.OrganizationId, posting.FacilityId, posting.Type, posting.StockRequestId, posting.FulfillmentId, posting.ReversesPostingId, posting.StockCountId, posting.PostedAt, posting.PostedBy, posting.Reason, reversedBy, entries });
        });
        api.MapGet("/counts", async (Guid organizationId, Guid facilityId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            if (!await CanRead(organizationId, facilityId, actor, ct)) return OrganizationEndpoints.Missing();
            var number = Math.Clamp(page ?? 1, 1, 10000); var query = db.StockCounts.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.FacilityId == facilityId);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.LeaseUnitId, x.ItemId, x.State, x.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new { items, total = await query.CountAsync(ct), page = number, pageSize = 50 });
        });
    }
}
