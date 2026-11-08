using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Invitations;

public sealed record InvitationInput(Guid OrganizationId, string Email, string Role, string Reason);
public sealed record CredentialInput(string Token, string Password);
public sealed record ResetInput(string Email);
public sealed record StaffGrantInput(string UserId, Guid FacilityId, string Role, bool Granted, string Reason);
public sealed record DisableInput(string UserId, string Reason);

public sealed class IdentityOperationsService(AppDbContext db, CurrentActor actor, UserManager<AppUser> users,
    IDataProtectionProvider protection, TimeProvider time)
{
    private static readonly string[] StaffRoles = ["Clerk", "Sales", "Finance", "FacilityManager"];
    private DateTimeOffset Now => time.GetUtcNow();
    private static IResult Invalid() => ApiFailure.Result(400, "CREDENTIAL_INVALID", "The credential is invalid, expired or unavailable.");
    private static IResult Missing() => ApiFailure.Result(404, "RESOURCE_NOT_FOUND", "The requested resource is unavailable.");
    private static IResult Forbidden() => ApiFailure.Result(403, "ACTION_FORBIDDEN", "Your current access does not allow this action.");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string? Email(string? input)
    {
        if (input is null || input.Length > 256) return null;
        var value = input.Trim();
        return MailAddress.TryCreate(value, out var address) && address.Address.Equals(value, StringComparison.OrdinalIgnoreCase)
            ? value.ToLowerInvariant() : null;
    }
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 500;
    private void Audit(HttpContext context, string by, string action, Guid id, string reason, Guid? org = null) =>
        db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = by, Action = action, AggregateId = id,
            OrganizationId = org, CorrelationId = context.TraceIdentifier, Reason = reason, At = Now });
    private async Task<bool> CanInvite(Guid organizationId, string userId, CancellationToken ct) =>
        await db.Users.AnyAsync(x => x.Id == userId && x.Active, ct) &&
        await db.Memberships.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == userId && x.Active && x.Role == "CustomerAdmin", ct) &&
        await db.CustomerOrganizations.AnyAsync(x => x.Id == organizationId && x.Active, ct);
    private void Enqueue(AccessCredential credential)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); credential.TokenHash = Hash(token);
        db.Set<AccessCredential>().Add(credential);
        db.Set<CredentialDelivery>().Add(new CredentialDelivery { Id = Guid.NewGuid(), CredentialId = credential.Id,
            Recipient = credential.Email, Kind = credential.Kind, CreatedAt = credential.CreatedAt,
            ProtectedToken = protection.CreateProtector("WarehouseRent.CredentialDelivery.v1").Protect(token) });
    }
    private async Task Touch(CancellationToken ct)
    { (await db.Set<IdentityDirectoryState>().SingleAsync(ct)).ChangeId = Guid.NewGuid(); }
    private async Task<IResult?> Replay(HttpContext context, object input, string operation, CancellationToken ct)
    {
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var id) || id == Guid.Empty)
            return ApiFailure.Result(400, "COMMAND_ID_REQUIRED", "Use a UUID command identity.");
        var hash = Hash(JsonSerializer.Serialize(new { operation, input, version = context.Request.Headers.IfMatch.ToString() }));
        var existing = await db.Set<IdentityOperationCommand>().AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.Id == id, ct);
        return existing is null ? null : existing.PayloadHash == hash ? Results.Content(existing.ResultJson, "application/json")
            : ApiFailure.Result(409, "COMMAND_CONFLICT", "The identity was already used for another request.");
    }
    private async Task<IResult> Complete(HttpContext context, object input, string operation, object result, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        db.Set<IdentityOperationCommand>().Add(new IdentityOperationCommand { Id = Guid.Parse(context.Request.Headers["Idempotency-Key"]!), ActorId = actor.UserId,
            PayloadHash = Hash(JsonSerializer.Serialize(new { operation, input, version = context.Request.Headers.IfMatch.ToString() })), ResultJson = json });
        await db.SaveChangesAsync(ct); return Results.Content(json, "application/json");
    }
    public async Task<IResult> Invite(HttpContext context, InvitationInput input, CancellationToken ct)
    {
        var email = Email(input.Email);
        if (email is null || (!string.IsNullOrEmpty(users.Options.User.AllowedUserNameCharacters) && email.Any(c => !users.Options.User.AllowedUserNameCharacters.Contains(c))) || input.Role is not ("Member" or "CustomerAdmin") || !Reason(input.Reason))
            return ApiFailure.Result(422, "INVITATION_INVALID", "Provide an email, allowed role and reason.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
        if (!await CanInvite(input.OrganizationId, actor.UserId, ct)) return Missing();
        var replay = await Replay(context, input, "Invite", ct); if (replay is not null) return replay;
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant() || x.NormalizedUserName == email.ToUpperInvariant(), ct))
            return ApiFailure.Result(409, "EXISTING_IDENTITY", "Use the versioned existing-user membership workflow.");
        var now = Now;
        if (await db.Set<AccessCredential>().AnyAsync(x => x.Email == email && x.OrganizationId == input.OrganizationId && x.Kind == "Invitation"
            && x.ConsumedAt == null && x.RevokedAt == null && x.ExpiresAt > now, ct))
            return ApiFailure.Result(409, "INVITATION_PENDING", "An unexpired invitation already exists.");
        var credential = new AccessCredential { Id = Guid.NewGuid(), Kind = "Invitation", TokenHash = "", Email = email,
            OrganizationId = input.OrganizationId, Role = input.Role, IssuedBy = actor.UserId, CreatedAt = now, ExpiresAt = now.AddHours(48) };
        Enqueue(credential); Audit(context, actor.UserId, "InvitationQueued", credential.Id, input.Reason, input.OrganizationId);
        var result = await Complete(context, input, "Invite", new { id = credential.Id, expiresAt = credential.ExpiresAt, delivery = "PendingTransport" }, ct);
        await tx.CommitAsync(ct); return result;
    }
    public async Task<IResult> Invitations(Guid org, int page, CancellationToken ct)
    {
        if (!await CanInvite(org, actor.UserId, ct)) return Missing();
        page = Math.Max(1, page); if (page > 10000) return Missing();
        var query = db.Set<AccessCredential>().AsNoTracking().Where(x => x.Kind == "Invitation" && x.OrganizationId == org);
        return Results.Ok(new { page, total = await query.CountAsync(ct), items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * 50).Take(50).Select(x => new { x.Id, x.Email, x.Role, x.ExpiresAt, x.ConsumedAt, x.RevokedAt }).ToListAsync(ct) });
    }
    public async Task<IResult> Revoke(HttpContext context, Guid id, string reason, CancellationToken ct)
    {
        if (!Reason(reason)) return Invalid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
        var credential = await db.Set<AccessCredential>().SingleOrDefaultAsync(x => x.Id == id && x.Kind == "Invitation", ct);
        if (credential?.OrganizationId is not Guid org || !await CanInvite(org, actor.UserId, ct)) return Missing();
        var input = new { id, reason }; var replay = await Replay(context, input, "RevokeInvite", ct); if (replay is not null) return replay;
        if (credential.ConsumedAt is not null) return ApiFailure.Result(409, "INVITATION_USED", "The invitation was already accepted.");
        credential.RevokedAt ??= Now;
        await db.Set<CredentialDelivery>().Where(x => x.CredentialId == id).ExecuteDeleteAsync(ct);
        Audit(context, actor.UserId, "InvitationRevoked", id, reason, org);
        var result = await Complete(context, input, "RevokeInvite", new { id, revoked = true }, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<IResult> RequestReset(HttpContext context, ResetInput input, CancellationToken ct)
    {
        var accepted = Results.Accepted(value: new { message = "If the account is eligible, recovery instructions will be queued." });
        var email = Email(input.Email); if (email is null) return accepted;
        await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
        var now = Now; var addressHash = Hash(email);
        var cooldown = await db.Set<RecoveryCooldown>().SingleOrDefaultAsync(x => x.AddressHash == addressHash, ct);
        if (cooldown is not null && cooldown.RequestedAt > now.AddSeconds(-60)) return accepted;
        if (cooldown is null) db.Set<RecoveryCooldown>().Add(new RecoveryCooldown { AddressHash = addressHash, RequestedAt = now });
        else cooldown.RequestedAt = now;
        var candidates = await db.Users.Where(x => x.NormalizedEmail == email.ToUpperInvariant() && x.Active && x.EmailConfirmed).Take(2).ToListAsync(ct);
        var user = candidates.Count == 1 ? candidates[0] : null;
        if (user is not null)
        {
            await db.Set<AccessCredential>().Where(x => x.UserId == user.Id && x.Kind == "Reset" && x.ConsumedAt == null && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
            // Old envelopes cannot remain a source of stale usable recovery secrets.
            await db.Set<CredentialDelivery>().Where(x => x.Kind == "Reset" && x.Recipient == email).ExecuteDeleteAsync(ct);
            var credential = new AccessCredential { Id = Guid.NewGuid(), Kind = "Reset", TokenHash = "", Email = email,
                UserId = user.Id, IssuedBy = user.Id, CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
            Enqueue(credential); Audit(context, user.Id, "PasswordResetQueued", credential.Id, "Anonymous recovery request");
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return accepted;
    }
    public async Task<IResult> Redeem(HttpContext context, CredentialInput input, string kind, CancellationToken ct)
    {
        if (input.Token is null || input.Token.Length != 64 || !input.Token.All(Uri.IsHexDigit) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024) return Invalid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
        var hash = Hash(input.Token); var credential = await db.Set<AccessCredential>().SingleOrDefaultAsync(x => x.TokenHash == hash && x.Kind == kind, ct);
        var now = Now;
        if (credential is null || credential.ExpiresAt <= now || credential.ConsumedAt is not null || credential.RevokedAt is not null) return Invalid();
        AppUser user;
        if (kind == "Invitation")
        {
            if (credential.OrganizationId is not Guid org || !await CanInvite(org, credential.IssuedBy, ct) ||
                await db.Users.AnyAsync(x => x.NormalizedEmail == credential.Email.ToUpperInvariant() || x.NormalizedUserName == credential.Email.ToUpperInvariant(), ct)) return Invalid();
            user = new AppUser { Id = Guid.NewGuid().ToString(), UserName = credential.Email, Email = credential.Email,
                EmailConfirmed = true, Active = true, LockoutEnabled = true };
        }
        else
        {
            var existing = await db.Users.SingleOrDefaultAsync(x => x.Id == credential.UserId && x.Active && x.EmailConfirmed, ct);
            if (existing is null || !string.Equals(existing.NormalizedEmail, credential.Email.ToUpperInvariant(), StringComparison.Ordinal)) return Invalid();
            user = existing;
        }
        foreach (var validator in users.PasswordValidators)
            if (!(await validator.ValidateAsync(users, user, input.Password)).Succeeded)
                return ApiFailure.Result(400, "PASSWORD_INVALID", "Use at least 12 characters including uppercase, lowercase, digit and symbol.");
        if (kind == "Invitation")
        {
            if (!(await users.CreateAsync(user, input.Password)).Succeeded) return Invalid();
            db.Memberships.Add(new Membership { Id = Guid.NewGuid(), OrganizationId = credential.OrganizationId!.Value,
                UserId = user.Id, Role = credential.Role!, Active = true });
        }
        else
        {
            user.PasswordHash = users.PasswordHasher.HashPassword(user, input.Password);
            user.SecurityStamp = Guid.NewGuid().ToString("N");
            user.AccessFailedCount = 0; user.LockoutEnd = null;
            if (!(await users.UpdateAsync(user)).Succeeded) return Invalid();
            await db.LoginSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
        }
        credential.ConsumedAt = now;
        await db.Set<CredentialDelivery>().Where(x => x.CredentialId == credential.Id).ExecuteDeleteAsync(ct);
        await Touch(ct); Audit(context, user.Id, kind == "Invitation" ? "InvitationAccepted" : "PasswordReset", credential.Id, "Single-use credential accepted", credential.OrganizationId);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(new { completed = true });
    }
    public async Task<IResult> Directory(int page, CancellationToken ct)
    {
        if (!await actor.IsOperatorAdminAsync(ct)) return Forbidden();
        page = Math.Max(1, page); if (page > 10000) return Missing();
        var state = await db.Set<IdentityDirectoryState>().AsNoTracking().SingleAsync(ct);
        var query = db.Users.AsNoTracking().OrderBy(x => x.Id);
        var items = await query.Skip((page - 1) * 50).Take(50).Select(x => new { x.Id, x.UserName, x.Active, x.OperatorAdmin }).ToListAsync(ct);
        var ids = items.Select(x => x.Id).ToArray();
        var grants = await db.StaffFacilityScopes.AsNoTracking().Where(x => ids.Contains(x.UserId)).OrderBy(x => x.UserId).ThenBy(x => x.FacilityId).ThenBy(x => x.StaffRole).ToListAsync(ct);
        return Results.Ok(new { version = Convert.ToBase64String(state.Version), page, total = await db.Users.CountAsync(ct), items, grants });
    }
    public async Task<IResult> Staff(HttpContext context, StaffGrantInput? grant, DisableInput? disable, CancellationToken ct)
    {
        var userId = grant?.UserId ?? disable?.UserId; var reason = grant?.Reason ?? disable?.Reason;
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 450 || !Reason(reason) || (grant is not null && !StaffRoles.Contains(grant.Role))) return Invalid();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await IdentityWriteLock.Acquire(db, ct);
        if (!await actor.IsOperatorAdminAsync(ct)) return Forbidden();
        var operation = grant is null ? "Disable" : "StaffGrant"; object input = (object?)grant ?? disable!;
        var replay = await Replay(context, input, operation, ct); if (replay is not null) return replay;
        var state = await db.Set<IdentityDirectoryState>().SingleAsync(ct);
        var expected = context.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(expected)) return ApiFailure.Result(428, "VERSION_REQUIRED", "Read the directory before changing access.");
        if (expected != $"\"{Convert.ToBase64String(state.Version)}\"") return ApiFailure.Result(412, "VERSION_CONFLICT", "Refresh the directory before changing access.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct); if (user is null) return Missing();
        if (user.OperatorAdmin || user.Id == actor.UserId) return ApiFailure.Result(409, "PROTECTED_ADMIN", "Operator administrators are protected from this workflow.");
        if (grant is not null)
        {
            if (!user.Active || !await db.Facilities.AnyAsync(x => x.Id == grant.FacilityId, ct)) return Missing();
            var current = await db.StaffFacilityScopes.SingleOrDefaultAsync(x => x.UserId == userId && x.FacilityId == grant.FacilityId && x.StaffRole == grant.Role, ct);
            if (grant.Granted && current is null) db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = userId, FacilityId = grant.FacilityId, StaffRole = grant.Role });
            if (!grant.Granted && current is not null) db.StaffFacilityScopes.Remove(current);
        }
        else
        {
            // Do not strand an organization with no active customer administrator.
            var organizations = await db.Memberships.Where(x => x.UserId == userId && x.Active && x.Role == "CustomerAdmin").Select(x => x.OrganizationId).ToArrayAsync(ct);
            foreach (var org in organizations)
                if (!await db.Memberships.AnyAsync(x => x.OrganizationId == org && x.UserId != userId && x.Active && x.Role == "CustomerAdmin" && db.Users.Any(u => u.Id == x.UserId && u.Active), ct))
                    return ApiFailure.Result(409, "LAST_ADMIN", "Keep at least one active customer administrator.");
            user.Active = false; user.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.LoginSessions.Where(x => x.UserId == userId && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
            await db.Set<AccessCredential>().Where(x => (x.UserId == userId || x.IssuedBy == userId) && x.ConsumedAt == null && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, Now), ct);
        }
        state.ChangeId = Guid.NewGuid(); Audit(context, actor.UserId, operation, Guid.Parse(context.Request.Headers["Idempotency-Key"]!), reason!);
        await db.SaveChangesAsync(ct);
        var result = await Complete(context, input, operation, new { completed = true, userId, facilityId = grant?.FacilityId, role = grant?.Role, granted = grant?.Granted, version = Convert.ToBase64String(state.Version) }, ct);
        await tx.CommitAsync(ct); return result;
    }
}
