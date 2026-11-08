using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Identity;

public static class IdentityRegistration
{
    public const string SessionClaim = "warehouse_session";
    public static readonly string Scheme = IdentityConstants.ApplicationScheme;

    public static void AddWorkspaceIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<AppUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.User.RequireUniqueEmail = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddSignInManager();
        var authentication = builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "WarehouseRent.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.EnvironmentName is "Demo" or "Development" or "Testing"
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
            options.EventsType = typeof(SessionValidation);
        });
        builder.AddCustomerGoogle(authentication);
        builder.Services.AddScoped<CustomerAuthentication>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy("customer-registration", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0, AutoReplenishment = true }));
            options.AddPolicy("customer-google", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "WarehouseRent.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = builder.Environment.EnvironmentName is "Demo" or "Development" or "Testing"
                ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<SessionValidation>();
        builder.Services.AddScoped<CurrentActor>();
    }
}

public sealed class SessionValidation(IServiceProvider services) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        // Public probes remain independent of authentication/database configuration.
        if (context.HttpContext.Request.Path.StartsWithSegments("/health")) { context.RejectPrincipal(); return; }
        var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(context.Principal?.FindFirst(IdentityRegistration.SessionClaim)?.Value, out var sessionId))
        { context.RejectPrincipal(); return; }
        var db = services.GetRequiredService<AppDbContext>();
        var session = await db.LoginSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId && x.UserId == userId, context.HttpContext.RequestAborted);
        var user = userId is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, context.HttpContext.RequestAborted);
        if (session is null || user is null || !user.Active || session.RevokedAt is not null || session.ExpiresAt <= DateTimeOffset.UtcNow
            || session.SecurityStamp != user.SecurityStamp || user.LockoutEnd > DateTimeOffset.UtcNow)
            context.RejectPrincipal();
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 401; return Task.CompletedTask; }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    { context.Response.StatusCode = 403; return Task.CompletedTask; }
}
