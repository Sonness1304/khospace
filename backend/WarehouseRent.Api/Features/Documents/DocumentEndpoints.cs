using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Documents;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Api.Features.Documents;

public static class DocumentEndpoints
{
    public sealed record Upload(string FileName, string ContentType, string ContentBase64, string Reason);
    public sealed record Revoke(string Reason);
    private static bool Reason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Length <= 500;
    private static async Task<bool> Allowed(AppDbContext db, CurrentActor actor, Lease lease, bool manage, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)) return false;
        var membership = await actor.MembershipAsync(lease.OrganizationId, ct);
        return (membership is not null && (!manage || membership.Role == "CustomerAdmin")) || await actor.HasFacilityAsync(lease.FacilityId, "FacilityManager", ct);
    }
    public static void MapDocuments(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/leases/{leaseId:guid}/documents", async (Guid leaseId, int? page, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == leaseId, ct);
            if (lease is null || !await Allowed(db, actor, lease, false, ct)) return OrganizationEndpoints.Missing();
            var index = page ?? 1; if (index is < 1 or > 10000) return OrganizationEndpoints.Missing();
            var query = db.PrivateDocuments.AsNoTracking().Where(x => x.LeaseId == leaseId);
            return Results.Ok(new { page = index, total = await query.CountAsync(ct), items = await query.OrderByDescending(x => x.UploadedAt).ThenBy(x => x.Id)
                .Skip((index - 1) * 50).Take(50).Select(x => new { x.Id, x.FileName, x.ContentType, x.Sha256, bytes = x.Content.Length, x.UploadedAt, x.RevokedAt }).ToListAsync(ct) });
        });
        api.MapPost("/leases/{leaseId:guid}/documents", async (Guid leaseId, Upload input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var lease = await db.Leases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == leaseId, ct);
            if (lease is null || !await Allowed(db, actor, lease, false, ct)) return OrganizationEndpoints.Missing();
            if (input.ContentBase64 is null || input.ContentBase64.Length > ((DocumentContent.MaxBytes + 2) / 3) * 4) return Results.StatusCode(413);
            byte[] bytes; try { bytes = Convert.FromBase64String(input.ContentBase64); } catch (FormatException) { return Invalid(); }
            if (bytes.Length > DocumentContent.MaxBytes) return Results.StatusCode(413);
            if (!Reason(input.Reason) || !DocumentContent.Valid(input.FileName, input.ContentType, bytes)) return Invalid();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return await commands.ExecuteAsync(context, lease.OrganizationId, "UploadDocument", new { leaseId, input.FileName, input.ContentType, hash, input.Reason }, async token =>
            {
                await IdentityWriteLock.Acquire(db, token);
                if (!await Allowed(db, actor, lease, false, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The requested resource is unavailable.");
                var document = new PrivateDocument { Id = Guid.NewGuid(), LeaseId = lease.Id, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId,
                    StorageKey = Guid.NewGuid().ToString("N") + Path.GetExtension(input.FileName).ToLowerInvariant(), FileName = input.FileName, ContentType = input.ContentType,
                    Sha256 = hash, Content = bytes, UploadedBy = actor.UserId, UploadedAt = DateTimeOffset.UtcNow };
                db.PrivateDocuments.Add(document); commands.Audit(context, "DocumentUploaded", document.Id, input.Reason, lease.OrganizationId, lease.FacilityId);
                return new { document.Id, document.FileName, document.Sha256 };
            }, ct, lease.OrganizationId, lease.FacilityId);
        }).WithMetadata(new RequestSizeLimitAttribute(14 * 1024 * 1024 + 4096));
        api.MapPost("/documents/{id:guid}/revoke", async (Guid id, Revoke input, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var scope = await db.PrivateDocuments.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.LeaseId }).SingleOrDefaultAsync(ct);
            if (scope is null) return OrganizationEndpoints.Missing();
            var lease = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == scope.LeaseId, ct);
            if (!await Allowed(db, actor, lease, true, ct)) return OrganizationEndpoints.Missing();
            if (!Reason(input.Reason)) return Invalid();
            return await commands.ExecuteAsync(context, lease.OrganizationId, "RevokeDocument", new { id, input }, async token =>
            {
                await IdentityWriteLock.Acquire(db, token);
                if (!await Allowed(db, actor, lease, true, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The requested resource is unavailable.");
                var doc = await db.PrivateDocuments.SingleAsync(x => x.Id == id, token); doc.RevokedAt ??= DateTimeOffset.UtcNow;
                commands.Audit(context, "DocumentRevoked", id, input.Reason, lease.OrganizationId, lease.FacilityId); return new { id, revoked = true };
            }, ct, lease.OrganizationId, lease.FacilityId);
        });
        api.MapGet("/documents/{id:guid}/download", async (Guid id, AppDbContext db, CurrentActor actor, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
            var document = await db.PrivateDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.RevokedAt == null, ct);
            if (document is null) return OrganizationEndpoints.Missing();
            var lease = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == document.LeaseId, ct);
            if (!await Allowed(db, actor, lease, false, ct)) return OrganizationEndpoints.Missing();
            if (document.Sha256 != Convert.ToHexString(SHA256.HashData(document.Content))) return ApiFailure.Result(409, "DOCUMENT_DAMAGED", "Document integrity verification failed.");
            commands.Audit(context, "DocumentDownloaded", id, "Authorized private download", lease.OrganizationId, lease.FacilityId);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            context.Response.Headers["X-Content-Type-Options"] = "nosniff"; context.Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
            return Results.File(document.Content, document.ContentType, document.FileName, enableRangeProcessing: false);
        });
    }
    private static IResult Invalid() => ApiFailure.Result(422, "DOCUMENT_INVALID", "Use a safe file name, supported JPEG/PNG/PDF content, size at most 10 MiB and a reason.");
}
