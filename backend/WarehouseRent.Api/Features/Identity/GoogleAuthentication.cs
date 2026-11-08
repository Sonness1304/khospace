using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Identity;

public sealed record GoogleAvailability(bool Enabled);

public static class GoogleAuthentication
{
    public const string CompletePath = "/api/v1/auth/google/complete";
    private const string VerifiedClaim = "warehouse_google_verified";
    private const string TicketClaim = "warehouse_google_ticket";
    private const string PreviousSession = "warehouse_google_previous_session";
    private const string PreviousUser = "warehouse_google_previous_user";
    public static string ErrorPath(string code) => $"/?authError={code}#account";

    public static void AddCustomerGoogle(this WebApplicationBuilder builder, AuthenticationBuilder authentication)
    {
        var clientId = builder.Configuration["Authentication:Google:ClientId"];
        var secret = builder.Configuration["Authentication:Google:ClientSecret"];
        var available = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(secret);
        builder.Services.AddSingleton(new GoogleAvailability(available));
        if (!available) return;
        authentication.AddCookie(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.Name = "WarehouseRent.External";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        }).AddGoogle(options =>
        {
            options.ClientId = clientId!; options.ClientSecret = secret!;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-google";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.RemoteAuthenticationTimeout = TimeSpan.FromMinutes(5);
            options.Scope.Clear(); options.Scope.Add("openid"); options.Scope.Add("profile"); options.Scope.Add("email");
            options.Events.OnCreatingTicket = context =>
            {
                if (!context.User.TryGetProperty("sub", out var subject) || subject.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(subject.GetString()) || subject.GetString()!.Length > 256
                    || !context.User.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String
                    || !CustomerAuthentication.ValidEmail(email.GetString()?.Trim())
                    || !context.User.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True)
                { return Task.CompletedTask; }
                context.Identity!.AddClaim(new Claim(VerifiedClaim, "true"));
                context.Identity.AddClaim(new Claim(TicketClaim, Guid.NewGuid().ToString()));
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = context =>
            {
                if (context.Principal?.FindFirstValue(VerifiedClaim) != "true")
                { context.HandleResponse(); context.Response.Redirect(ErrorPath("google-unverified")); }
                return Task.CompletedTask;
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect(ErrorPath(context.Failure?.Message == "google-unverified" ? "google-unverified" : "google-failed"));
                return Task.CompletedTask;
            };
            options.Events.OnAccessDenied = context =>
            {
                context.HandleResponse(); context.Response.Redirect(ErrorPath("google-cancelled"));
                return Task.CompletedTask;
            };
        });
    }

    public static void MapCustomerAuthentication(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/auth");
        api.MapPost("/register", (CustomerAuthentication.RegistrationInput input, CustomerAuthentication auth, HttpContext context, CancellationToken ct) =>
            auth.Register(input, context, ct)).RequireRateLimiting("customer-registration");
        api.MapGet("/providers", (GoogleAvailability availability) => Results.Ok(new { google = availability.Enabled }));
        api.MapGet("/google", Challenge).RequireRateLimiting("customer-google");
        api.MapGet("/google/complete", Complete).RequireRateLimiting("customer-google");
    }

    private static async Task<IResult> Challenge(HttpContext context, GoogleAvailability availability)
    {
        if (!availability.Enabled) return Results.Redirect(ErrorPath("google-unavailable"));
        if (!context.Request.IsHttps) return Results.Redirect(ErrorPath("google-https-required"));
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = new AuthenticationProperties { RedirectUri = CompletePath };
        // Strict application cookies can be absent on the cross-site callback. Carry the validated prior session in protected OAuth state.
        if (context.User.Identity?.IsAuthenticated == true)
        {
            properties.Items[PreviousSession] = context.User.FindFirstValue(IdentityRegistration.SessionClaim);
            properties.Items[PreviousUser] = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        }
        return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
    }
    private static async Task<IResult> Complete(HttpContext context, GoogleAvailability availability, AppDbContext db,
        UserManager<AppUser> users, CustomerAuthentication auth, CancellationToken ct)
    {
        if (!availability.Enabled) return Results.Redirect(ErrorPath("google-unavailable"));
        try
        {
            var external = await context.AuthenticateAsync(IdentityConstants.ExternalScheme);
            var principal = external.Principal;
            var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = principal?.FindFirstValue(ClaimTypes.Email)?.Trim();
            if (!external.Succeeded || principal?.FindFirstValue(VerifiedClaim) != "true" || string.IsNullOrWhiteSpace(subject)
                || subject.Length > 256 || !CustomerAuthentication.ValidEmail(email)
                || !Guid.TryParse(principal.FindFirstValue(TicketClaim), out var ticket))
                return Results.Redirect(ErrorPath("google-failed"));
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await IdentityWriteLock.Acquire(db, ct);
            // The encrypted external ticket is short lived; the consumed audit makes replay fail across app instances.
            if (await db.SecurityAudits.AnyAsync(x => x.Action == "GoogleTicketConsumed" && x.AggregateId == ticket, ct))
                return Results.Redirect(ErrorPath("google-failed"));
            var user = await users.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, subject);
            if (user is not null && (!user.Active || await users.IsLockedOutAsync(user)))
                return Results.Redirect(ErrorPath("account-disabled"));
            if (user is null)
            {
                if (await auth.EmailExists(email!, ct)) return Results.Redirect(ErrorPath("google-link-required"));
                user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, OperatorAdmin = false };
                if (!(await users.CreateAsync(user)).Succeeded) throw new CustomerAuthentication.ProvisioningFailure();
                if (!(await users.AddLoginAsync(user, new UserLoginInfo(GoogleDefaults.AuthenticationScheme, subject, "Google"))).Succeeded)
                    throw new CustomerAuthentication.ProvisioningFailure();
                var name = principal.FindFirstValue(ClaimTypes.Name)?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) name = "Customer workspace";
                if (name.Length > 200) name = name[..200];
                var organization = auth.AddWorkspace(user, name);
                auth.Audit(context, user, "CustomerRegistered", organization.Id, "Verified Google customer registration", organization.Id);
            }
            auth.Audit(context, user, "GoogleTicketConsumed", ticket, "Single-use external authentication completion");
            if (external.Properties?.Items.TryGetValue(PreviousSession, out var priorSession) == true
                && Guid.TryParse(priorSession, out var previous) && external.Properties.Items.TryGetValue(PreviousUser, out var priorUser))
                await db.LoginSessions.Where(x => x.Id == previous && x.UserId == priorUser && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), ct);
            var session = await auth.AddSession(context, user, "Verified Google authentication", ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await CustomerAuthentication.SignIn(context, user, session);
            return Results.Redirect("/#account");
        }
        catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
        {
            // Provider/database details are deliberately excluded from the browser redirect.
            return Results.Redirect(ErrorPath("google-failed"));
        }
        finally { await context.SignOutAsync(IdentityConstants.ExternalScheme); }
    }
}
