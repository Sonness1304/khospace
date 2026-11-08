using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Organizations;

public sealed class MembershipService(AppDbContext db, CurrentActor actor)
{
    public async Task<IResult> WriteAsync(HttpContext context, Guid organizationId, Guid? membershipId, string? userName,
        string? role, bool active, string? reason, string operation, CancellationToken ct)
    {
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var commandId) || commandId == Guid.Empty)
            return ApiFailure.Result(422, "COMMAND_KEY_REQUIRED", "Supply a UUID Idempotency-Key.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500 || (operation != "Deactivate" && role is not ("CustomerAdmin" or "Member"))
            || (operation == "Add" && (string.IsNullOrWhiteSpace(userName) || userName.Length > 256)))
            return ApiFailure.Result(422, "INVALID_MEMBERSHIP", "Supply a customer role, existing user and a reason of at most 500 characters.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await WarehouseRent.Api.Features.Invitations.IdentityWriteLock.Acquire(db, ct);
        // All membership writers serialize on the stable organization row, including creation.
        var organization = await DatabaseDialect.Rows<CustomerOrganization>(db, $"SELECT * FROM [CustomerOrganizations] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={organizationId}")
            .SingleOrDefaultAsync(ct);
        if (organization is null || !organization.Active) return OrganizationEndpoints.Missing();
        var current = await actor.MembershipAsync(organizationId, ct);
        if (current is null || !await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct)) return OrganizationEndpoints.Missing();
        if (current.Role != "CustomerAdmin") return OrganizationEndpoints.Forbidden();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            { operation, organizationId, membershipId, userName, role, active, reason, version = context.Request.Headers.IfMatch.ToString() }))));
        var existing = await db.MembershipCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == commandId && x.ActorId == actor.UserId && x.OrganizationId == organizationId, ct);
        if (existing is not null)
            return existing.PayloadHash == hash ? Results.Content(existing.ResultJson, "application/json")
                : ApiFailure.Result(409, "COMMAND_CONFLICT", "That command identity was already used for a different request.");

        Membership target;
        if (operation == "Add")
        {
            var normalized = userName!.ToUpperInvariant();
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedUserName == normalized && x.Active, ct);
            if (user is null) return ApiFailure.Result(422, "INVALID_MEMBERSHIP", "The membership request cannot be applied.");
            if (await db.Memberships.AnyAsync(x => x.UserId == user.Id && x.OrganizationId == organizationId, ct))
                return ApiFailure.Result(409, "MEMBERSHIP_EXISTS", "That membership already exists; use its versioned change action.");
            target = new Membership { Id = Guid.NewGuid(), OrganizationId = organizationId, UserId = user.Id, Role = role!, Active = true };
            db.Memberships.Add(target);
        }
        else
        {
            var found = await db.Memberships.SingleOrDefaultAsync(x => x.Id == membershipId && x.OrganizationId == organizationId, ct);
            if (found is null) return OrganizationEndpoints.Missing();
            target = found;
            if (!context.Request.Headers.TryGetValue("If-Match", out var expected) || string.IsNullOrWhiteSpace(expected))
                return ApiFailure.Result(428, "VERSION_REQUIRED", "Read the membership version before changing it.");
            if (expected.ToString() != $"\"{Convert.ToBase64String(target.Version)}\"")
                return ApiFailure.Result(412, "VERSION_CONFLICT", "The membership changed. Refresh it before submitting again.");
            if (operation == "Deactivate") role = target.Role;
            if (target.Role == "CustomerAdmin" && target.Active && (!active || role != "CustomerAdmin"))
            {
                var otherAdmin = await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id
                    where m.OrganizationId == organizationId && m.Id != target.Id && m.Active && m.Role == "CustomerAdmin" && u.Active
                    select m.Id).AnyAsync(ct);
                if (!otherAdmin) return ApiFailure.Result(409, "LAST_ADMIN", "Keep at least one active customer administrator.");
            }
            target.Role = role!; target.Active = active;
        }
        db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = actor.UserId, OrganizationId = organizationId,
            Action = operation == "Add" ? "MembershipAdded" : operation == "Deactivate" ? "MembershipDeactivated" : "MembershipChanged",
            AggregateId = target.Id, CorrelationId = context.TraceIdentifier, Reason = reason, At = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        var result = JsonSerializer.Serialize(new { commandId, status = "Completed", resourceType = "Membership", resourceId = target.Id,
            organizationId, role = target.Role, active = target.Active, version = Convert.ToBase64String(target.Version) });
        db.MembershipCommands.Add(new MembershipCommand { Id = commandId, ActorId = actor.UserId, OrganizationId = organizationId,
            MembershipId = target.Id, Operation = operation, PayloadHash = hash, ResultJson = result });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Content(result, "application/json");
    }
}
