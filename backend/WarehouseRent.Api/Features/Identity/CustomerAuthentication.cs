using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Identity;

public sealed class CustomerAuthentication(AppDbContext db, UserManager<AppUser> users)
{
    public sealed record RegistrationInput(string Name, string Email, string Password);
    public sealed class ProvisioningFailure : Exception;

    public static bool ValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Length <= 256
        && !email.Any(char.IsControl) && new EmailAddressAttribute().IsValid(email);

    public async Task<IResult> Register(RegistrationInput input, HttpContext context, CancellationToken ct)
    {
        var name = input.Name?.Trim(); var email = input.Email?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) || !ValidEmail(email)
            || string.IsNullOrEmpty(input.Password) || input.Password.Length > 1024)
            return ApiFailure.Result(400, "INVALID_REGISTRATION", "Enter a workspace name, valid email and password within the supported limits.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await IdentityWriteLock.Acquire(db, ct);
        if (await EmailExists(email!, ct))
            return ApiFailure.Result(409, "REGISTRATION_UNAVAILABLE", "An account cannot be created with those details. Sign in or recover your existing account.");
        var user = new AppUser { UserName = email, Email = email, OperatorAdmin = false };
        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
            return ApiFailure.Result(400, "PASSWORD_POLICY", "Use at least 12 characters with uppercase, lowercase, a digit and a symbol, and a supported email address.");
        var organization = AddWorkspace(user, name!);
        Audit(context, user, "CustomerRegistered", organization.Id, "Public customer registration", organization.Id);
        var session = await AddSession(context, user, "Customer registration", ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await SignIn(context, user, session);
        return Results.Ok(new { authenticated = true });
    }

    public async Task<bool> EmailExists(string email, CancellationToken ct)
    {
        var normalized = users.NormalizeEmail(email);
        return await db.Users.AnyAsync(x => x.NormalizedEmail == normalized || x.NormalizedUserName == users.NormalizeName(email), ct);
    }

    public CustomerOrganization AddWorkspace(AppUser user, string name)
    {
        var organization = new CustomerOrganization { Id = Guid.NewGuid(), Code = $"C-{Guid.NewGuid():N}", Name = name };
        db.CustomerOrganizations.Add(organization);
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), UserId = user.Id, OrganizationId = organization.Id, Role = "CustomerAdmin" });
        return organization;
    }

    public async Task<LoginSession> AddSession(HttpContext context, AppUser user, string reason, CancellationToken ct)
    {
        if (Guid.TryParse(context.User.FindFirstValue(IdentityRegistration.SessionClaim), out var previous))
        {
            var previousUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            await db.LoginSessions.Where(x => x.Id == previous && x.UserId == previousUserId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
        }
        var session = new LoginSession { Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) };
        db.LoginSessions.Add(session);
        Audit(context, user, "Login", session.Id, reason);
        return session;
    }

    public void Audit(HttpContext context, AppUser user, string action, Guid aggregate, string reason, Guid? organization = null) =>
        db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = user.Id, Action = action, AggregateId = aggregate,
            OrganizationId = organization, CorrelationId = context.TraceIdentifier, Reason = reason, At = DateTimeOffset.UtcNow });

    public static Task SignIn(HttpContext context, AppUser user, LoginSession session)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(IdentityRegistration.SessionClaim, session.Id.ToString())], IdentityRegistration.Scheme);
        return context.SignInAsync(IdentityRegistration.Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
    }
}
