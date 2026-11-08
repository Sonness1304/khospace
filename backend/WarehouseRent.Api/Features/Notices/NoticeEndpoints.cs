using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Jobs;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.Notices;
public static class NoticeEndpoints
{
    public sealed record JobInput(int Page);
    public static void MapNotices(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/notices", async (Guid organizationId, Guid facilityId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            if (await actor.MembershipAsync(organizationId, ct) is null && !await actor.HasFacilityAsync(facilityId, null, ct)) return OrganizationEndpoints.Missing();
            var number = page ?? 1; if (number is < 1 or > 10000) return OrganizationEndpoints.Missing();
            // Historical notices remain event records; they are not proof that the action is still pending.
            var query = db.InAppNotices.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.FacilityId == facilityId);
            return Results.Ok(new { page = number, total = await query.CountAsync(ct), items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50)
                .Select(x => new { x.Id, x.Kind, x.AggregateId, x.Message, x.CreatedAt }).ToListAsync(ct) });
        });
        foreach (var operation in new[] { "enqueue", "dispatch" })
        {
            api.MapPost($"/facilities/{{facilityId:guid}}/notice-jobs/{operation}", async (Guid facilityId, JobInput input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, TimeProvider time, CancellationToken ct) =>
            {
                if (!await actor.HasFacilityAsync(facilityId, "FacilityManager", ct)) return OrganizationEndpoints.Forbidden();
                if (input.Page is < 1 or > 10000) return ApiFailure.Result(422, "PAGE_INVALID", "Choose a page between 1 and 10000.");
                return await commands.ExecuteAsync(context, facilityId, "NoticeJob-" + operation, input, async token =>
                {
                    await DatabaseDialect.MutexAsync(db, "WarehouseRent.Notice." + facilityId, token, errorNumber: 51013, message: "Notice job busy.");
                    if (!await actor.HasFacilityAsync(facilityId, "FacilityManager", token)) throw new CommandFailure(403, "ACTION_FORBIDDEN", "Your current access does not allow this action.");
                    if (!await db.Facilities.AnyAsync(x => x.Id == facilityId, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The facility is unavailable.");
                    var now = time.GetUtcNow(); var count = 0; var scanned = 0;
                    if (operation == "enqueue")
                    {
                        var today = CatalogPolicy.Today(now); var horizon = today.AddDays(7);
                        var leases = await db.Leases.AsNoTracking().Where(x => x.FacilityId == facilityId && x.State == "Active" && x.TerminatedAt == null && x.EndDate >= today && x.EndDate <= horizon)
                            .OrderBy(x => x.Id).Skip((input.Page - 1) * 100).Take(100).Select(x => new { x.Id, x.OrganizationId, x.EndDate }).ToListAsync(token);
                        var requests = await db.StockRequests.AsNoTracking().Where(x => x.FacilityId == facilityId && (x.State == "Submitted" || x.State == "Approved"))
                            .OrderBy(x => x.Id).Skip((input.Page - 1) * 100).Take(100).Select(x => new { x.Id, x.OrganizationId, x.State }).ToListAsync(token);
                        scanned = leases.Count + requests.Count;
                        foreach (var lease in leases) await Add(lease.Id, lease.OrganizationId, "LeaseEnding", $"lease/{lease.Id}/end/{lease.EndDate:yyyy-MM-dd}", $"Lease {lease.Id} ends on {lease.EndDate:yyyy-MM-dd}. Review renewal or clearance.");
                        foreach (var request in requests) await Add(request.Id, request.OrganizationId, "StockAction", $"stock/{request.Id}/{request.State}", $"Stock request {request.Id} reached {request.State}. Check current status before acting.");
                        async Task Add(Guid id, Guid org, string kind, string businessKey, string message)
                        {
                            if (await db.NoticeOutbox.AnyAsync(x => x.BusinessKey == businessKey, token)) return;
                            db.NoticeOutbox.Add(new NoticeOutbox { Id = Guid.NewGuid(), OrganizationId = org, FacilityId = facilityId, AggregateId = id, Kind = kind, BusinessKey = businessKey, Message = message, CreatedAt = now }); count++;
                        }
                    }
                    else
                    {
                        // Always drain the oldest pending batch: no offset that could skip rows after a retry.
                        var batch = await db.NoticeOutbox.Where(x => x.FacilityId == facilityId && x.DeliveredAt == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(100).ToListAsync(token); scanned = batch.Count;
                        foreach (var item in batch)
                        {
                            if (!await db.InAppNotices.AnyAsync(x => x.OutboxId == item.Id, token))
                                db.InAppNotices.Add(new InAppNotice { Id = Guid.NewGuid(), OutboxId = item.Id, OrganizationId = item.OrganizationId, FacilityId = item.FacilityId,
                                    Kind = item.Kind, AggregateId = item.AggregateId, Message = item.Message, CreatedAt = now });
                            item.DeliveredAt = now; count++;
                        }
                    }
                    commands.Audit(context, "NoticeJob", facilityId, $"{operation}; page={input.Page}; scanned={scanned}; processed={count}", facility: facilityId);
                    return new { operation, scanned, processed = count, input.Page, batchLimit = operation == "dispatch" ? 100 : 200 };
                }, ct, facilityId: facilityId);
            });
        }
    }
}
