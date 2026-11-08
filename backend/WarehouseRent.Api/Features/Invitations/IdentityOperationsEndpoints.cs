using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WarehouseRent.Api.Features.Invitations;

public static class IdentityOperationsEndpoints
{
    public static void AddIdentityOperations(this WebApplicationBuilder builder)
    {
        builder.Services.AddDataProtection(); builder.Services.AddScoped<IdentityOperationsService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("recovery", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
    }
    public static void MapIdentityOperations(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");
        api.MapPost("/invitations", (HttpContext c, InvitationInput input, IdentityOperationsService service, CancellationToken ct) => service.Invite(c, input, ct)).RequireAuthorization();
        api.MapGet("/invitations", (Guid organizationId, int? page, IdentityOperationsService service, CancellationToken ct) => service.Invitations(organizationId, page ?? 1, ct)).RequireAuthorization();
        api.MapPost("/invitations/{id:guid}/revoke", (HttpContext c, Guid id, RevokeInput input, IdentityOperationsService service, CancellationToken ct) => service.Revoke(c, id, input.Reason, ct)).RequireAuthorization();
        api.MapPost("/auth/request-reset", (HttpContext c, ResetInput input, IdentityOperationsService service, CancellationToken ct) => service.RequestReset(c, input, ct)).RequireRateLimiting("recovery");
        api.MapPost("/auth/reset-password", (HttpContext c, CredentialInput input, IdentityOperationsService service, CancellationToken ct) => service.Redeem(c, input, "Reset", ct)).RequireRateLimiting("recovery");
        api.MapPost("/auth/accept-invitation", (HttpContext c, CredentialInput input, IdentityOperationsService service, CancellationToken ct) => service.Redeem(c, input, "Invitation", ct)).RequireRateLimiting("recovery");
        api.MapGet("/staff-directory", (int? page, IdentityOperationsService service, CancellationToken ct) => service.Directory(page ?? 1, ct)).RequireAuthorization();
        api.MapPost("/staff-directory/grants", (HttpContext c, StaffGrantInput input, IdentityOperationsService service, CancellationToken ct) => service.Staff(c, input, null, ct)).RequireAuthorization();
        api.MapPost("/staff-directory/disable", (HttpContext c, DisableInput input, IdentityOperationsService service, CancellationToken ct) => service.Staff(c, null, input, ct)).RequireAuthorization();
    }
    public sealed record RevokeInput(string Reason);
}
