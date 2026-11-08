using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Identity;

public static class IdentityEndpoints
{
    private sealed record LoginRequest(string UserName, string Password);
    // Precomputed synthetic hash makes an unknown username do a password verification too.
    private static readonly AppUser DummyUser = new() { Id = "synthetic-login-timing" };
    private static readonly string DummyHash = new PasswordHasher<AppUser>().HashPassword(DummyUser, "Synthetic_Not_A_Login!42");

    public static void MapWorkspaceIdentity(this WebApplication app)
    {
        app.MapCustomerAuthentication();
        var api = app.MapGroup("/api/v1");
        api.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        api.MapPost("/auth/login", async (LoginRequest input, UserManager<AppUser> users, SignInManager<AppUser> signIn,
            AppDbContext db, HttpContext context, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(input.UserName) || input.UserName.Length > 256 || string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024)
                return InvalidLogin();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await WarehouseRent.Api.Features.Invitations.IdentityWriteLock.Acquire(db, ct);
            var user = await users.FindByNameAsync(input.UserName.Trim());
            if (user is null)
            {
                _ = new PasswordHasher<AppUser>().VerifyHashedPassword(DummyUser, DummyHash, input.Password);
                return InvalidLogin();
            }
            var check = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
            if (!check.Succeeded || !user.Active) { await transaction.CommitAsync(ct); return InvalidLogin(); }
            // Retire the current cookie's prior session on login replacement; no session fixation.
            if (Guid.TryParse(context.User.FindFirstValue(IdentityRegistration.SessionClaim), out var previous))
            {
                var previousUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                await db.LoginSessions.Where(x => x.Id == previous && x.UserId == previousUserId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
            }
            var session = new LoginSession { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) };
            db.LoginSessions.Add(session);
            db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = user.Id, Action = "Login", AggregateId = session.Id,
                CorrelationId = context.TraceIdentifier, Reason = "Local password authentication", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.UserName!),
                new Claim(IdentityRegistration.SessionClaim, session.Id.ToString())], IdentityRegistration.Scheme);
            await context.SignInAsync(IdentityRegistration.Scheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
            return Results.Ok(new { authenticated = true });
        });
        api.MapPost("/auth/logout", async (HttpContext context, CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            var sessionId = Guid.Parse(context.User.FindFirstValue(IdentityRegistration.SessionClaim)!);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.LoginSessions.Where(x => x.Id == sessionId && x.UserId == actor.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
            db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = actor.UserId, Action = "Logout", AggregateId = sessionId,
                CorrelationId = context.TraceIdentifier, Reason = "Explicit sign out", At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            await context.SignOutAsync(IdentityRegistration.Scheme);
            return Results.NoContent();
        }).RequireAuthorization();
        api.MapGet("/auth/me", async (CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId, ct);
            var organizations = await (from m in db.Memberships.AsNoTracking()
                join org in db.CustomerOrganizations on m.OrganizationId equals org.Id
                where m.UserId == actor.UserId && m.Active && org.Active
                orderby org.Code select new { id = org.Id, org.Name, m.Role }).ToListAsync(ct);
            var facilities = await (from grant in db.StaffFacilityScopes.AsNoTracking()
                join facility in db.Facilities on grant.FacilityId equals facility.Id into matches
                from facility in matches.DefaultIfEmpty()
                where grant.UserId == actor.UserId
                orderby grant.FacilityId
                select new { id = grant.FacilityId, role = grant.StaffRole, name = facility == null ? null : facility.Name }).ToListAsync(ct);
            return Results.Ok(new { userId = user.Id, userName = user.UserName, operatorAdmin = user.OperatorAdmin, organizations, facilities });
        }).RequireAuthorization();
        api.MapGet("/scope/facilities/{id:guid}", async (Guid id, CurrentActor actor, CancellationToken ct) =>
            await actor.HasFacilityAsync(id, null, ct) ? Results.Ok(new { facilityId = id, scope = "Granted" })
                : ApiFailure.Result(403, "FACILITY_FORBIDDEN", "This action is outside your assigned facility scope.")).RequireAuthorization();
    }

    private static IResult InvalidLogin() => ApiFailure.Result(401, "INVALID_LOGIN", "Unable to sign in with those credentials.");
}
