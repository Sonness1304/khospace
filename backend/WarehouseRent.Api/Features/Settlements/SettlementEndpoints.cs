using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Settlements;

public static class SettlementEndpoints
{
    private static IResult Invalid() => ApiFailure.Result(422, "INVALID_SETTLEMENT_INPUT", "Supply the required reason, evidence, reference and explicit confirmation.");
    private static async Task<(Guid Org, Guid Facility)?> Scope(Guid account, AppDbContext db, CancellationToken ct) => await (from a in db.RentalAccounts join b in db.Bookings on a.BookingId equals b.Id where a.Id == account select new { a.OrganizationId, b.FacilityId }).SingleOrDefaultAsync(ct) is { } row ? (row.OrganizationId, row.FacilityId) : null;
    private static async Task<IResult?> Denied(Guid org, Guid facility, CurrentActor actor, bool write, CancellationToken ct)
    {
        if (await actor.HasFacilityAsync(facility, "Finance", ct)) return null;
        var member = await actor.MembershipAsync(org, ct) is not null;
        return !write && member ? null : member || await actor.HasFacilityAsync(facility, null, ct) ? OrganizationEndpoints.Forbidden() : OrganizationEndpoints.Missing();
    }
    public static void MapSettlements(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/statements/{id:guid}/adjustments", async (Guid id, AdjustStatement input, AppDbContext db, CurrentActor actor, SettlementService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var statement = await db.ChargeStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (statement is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(statement.OrganizationId, statement.FacilityId, actor, true, ct); if (denied is not null) return denied;
            if (!input.Approved || !CatalogPolicy.Text(input.Reason, 500) || !CatalogPolicy.Text(input.EvidenceReference, 240)) return Invalid();
            return await commands.ExecuteAsync(context, statement.OrganizationId, "AdjustStatement", new { id, input }, token => service.AdjustAsync(id, input, context, token), ct, organizationId: statement.OrganizationId, facilityId: statement.FacilityId, facilityRole: "Finance");
        });
        api.MapPost("/statements/{id:guid}/supplement-rent", async (Guid id, SupplementRent input, AppDbContext db, CurrentActor actor, SettlementService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var statement = await db.ChargeStatements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (statement is null) return OrganizationEndpoints.Missing();
            var denied = await Denied(statement.OrganizationId, statement.FacilityId, actor, true, ct); if (denied is not null) return denied;
            if (!input.Approved || !CatalogPolicy.Text(input.Reason, 500) || !CatalogPolicy.Text(input.EvidenceReference, 240)) return Invalid();
            return await commands.ExecuteAsync(context, statement.OrganizationId, "SupplementRent", new { id, input }, token => service.SupplementAsync(id, input, context, token), ct, organizationId: statement.OrganizationId, facilityId: statement.FacilityId, facilityRole: "Finance");
        });
        api.MapPost("/rental-accounts/{id:guid}/settlement-entries", async (Guid id, RecordSettlement input, AppDbContext db, CurrentActor actor, SettlementService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var scope = await Scope(id, db, ct); if (scope is null) return OrganizationEndpoints.Missing(); var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, true, ct); if (denied is not null) return denied;
            if (!input.Confirmed || !CatalogPolicy.Text(input.Reason, 500) || !CatalogPolicy.Text(input.EvidenceReference, 240) || !CatalogPolicy.Text(input.Reference, 120)) return Invalid();
            return await commands.ExecuteAsync(context, scope.Value.Org, "RecordSettlement", new { id, input }, token => service.EntryAsync(id, input, context, token), ct, organizationId: scope.Value.Org, facilityId: scope.Value.Facility, facilityRole: "Finance");
        });
        api.MapPost("/rental-accounts/{id:guid}/close", async (Guid id, CloseAccount input, AppDbContext db, CurrentActor actor, SettlementService service, CommandExecutor commands, HttpContext context, CancellationToken ct) => {
            var scope = await Scope(id, db, ct); if (scope is null) return OrganizationEndpoints.Missing(); var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, true, ct); if (denied is not null) return denied;
            if (!input.ClosureConfirmed || !CatalogPolicy.Text(input.Reason, 500)) return Invalid();
            return await commands.ExecuteAsync(context, scope.Value.Org, "CloseRentalAccount", new { id, input }, token => service.CloseAsync(id, input, context, token), ct, organizationId: scope.Value.Org, facilityId: scope.Value.Facility, facilityRole: "Finance");
        });
        api.MapGet("/rental-accounts/{id:guid}/settlement", async (Guid id, AppDbContext db, CurrentActor actor, SettlementService service, CancellationToken ct) => {
            var scope = await Scope(id, db, ct); if (scope is null) return OrganizationEndpoints.Missing(); var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, false, ct); if (denied is not null) return denied;
            await using var tx = await db.Database.BeginTransactionAsync(ct); var result = await service.ReadAsync(id, ct); await tx.CommitAsync(ct); return Results.Ok(result);
        });
        api.MapGet("/rental-accounts/{id:guid}/settlement-history", async (Guid id, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) => {
            var scope = await Scope(id, db, ct); if (scope is null) return OrganizationEndpoints.Missing(); var denied = await Denied(scope.Value.Org, scope.Value.Facility, actor, false, ct); if (denied is not null) return denied;
            var number = Math.Clamp(page ?? 1, 1, 10000);
            var query = db.SettlementEntries.AsNoTracking().Where(x => x.RentalAccountId == id); var adjustments = db.StatementAdjustments.AsNoTracking().Where(x => x.RentalAccountId == id);
            var items = await query.OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.Kind, x.ChargeStatementId, x.PaymentReceiptId, x.Reference, x.EvidenceReference, x.Reason, x.ActorId, x.At, x.PerformedAt, amount = CatalogPolicy.Money(x.Amount) }).ToListAsync(ct);
            var changes = await adjustments.OrderByDescending(x => x.At).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).Select(x => new { x.Id, x.Kind, x.ChargeStatementId, x.SourceLeaseVersionLineId, x.EvidenceReference, x.Reason, x.ActorId, x.At, amount = CatalogPolicy.Money(x.Amount), quantity = x.Quantity == null ? null : CatalogPolicy.Money(x.Quantity.Value), unitPrice = x.UnitPrice == null ? null : CatalogPolicy.Money(x.UnitPrice.Value) }).ToListAsync(ct);
            return Results.Ok(new { items, adjustments = changes, total = await query.CountAsync(ct), adjustmentTotal = await adjustments.CountAsync(ct), page = number, pageSize = 50 });
        });
    }
}
