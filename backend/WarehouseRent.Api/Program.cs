using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using WarehouseRent.Api.Features.Invitations;

var setupRequested = args.Contains("--migrate", StringComparer.Ordinal) || args.Contains("--seed-demo", StringComparer.Ordinal);
var seedRequested = args.Contains("--seed-demo", StringComparer.Ordinal);
var cloudSetupRequested = args.Contains("--initialize-cloud", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--seed-demo" or "--initialize-cloud")).ToArray());
builder.AddCloudHosting();
builder.Services.AddProblemDetails();
builder.Services.AddScoped(_ => new AppDbContext(DatabaseProviderConfiguration.Options(builder.Configuration)));
builder.Services.AddScoped<ReadinessProbe>();
builder.AddWorkspaceIdentity();
builder.AddIdentityOperations();
builder.Services.AddScoped<MembershipService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UnitLockService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Common.CommandExecutor>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Bookings.BookingService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Bookings.IDepositVerification, WarehouseRent.Api.Features.Payments.VerifiedDepositReader>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Bookings.ICancelScheduledLease, WarehouseRent.Api.Features.Leases.CancelScheduledLease>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Leases.LeaseService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.LeaseAmendments.RenewalService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Leases.IInventoryClearance, WarehouseRent.Api.Features.StockPosting.InventoryClearance>();
builder.Services.AddScoped<WarehouseRent.Api.Features.StockRequests.StockRequestService>();
builder.Services.AddScoped<StockLockService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.StockPosting.StockPostingService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.StockCorrections.StockCorrectionService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Payments.FinanceService>();
builder.Services.AddScoped<WarehouseRent.Api.Features.Settlements.SettlementService>();
builder.Services.AddHttpClient<WarehouseRent.Api.Features.OnlinePayments.PaymentGateway>(client => client.Timeout = TimeSpan.FromSeconds(35)).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
var app = builder.Build();

if (cloudSetupRequested)
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        await CloudDatabaseSetup.ApplyAsync(scope.ServiceProvider, builder.Configuration, builder.Environment.EnvironmentName);
        Console.WriteLine("Cloud schema initialized.");
        return 0;
    }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        Console.Error.WriteLine("Cloud initialization refused or unavailable.");
        return 2;
    }
}

if (setupRequested)
{
    try
    {
        var connection = builder.Configuration.GetConnectionString("WarehouseRent") ?? "";
        if (seedRequested) DemoSeedPolicy.Validate(builder.Environment.EnvironmentName, connection);
        else DemoSeedPolicy.ValidateMigration(builder.Environment.EnvironmentName, connection);
        await using var scope = app.Services.CreateAsyncScope();
        await DatabaseSetup.ApplyAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            builder.Environment.EnvironmentName, connection, seedRequested);
        Console.WriteLine(seedRequested ? "Synthetic Demo schema and seed ready." : "Local synthetic schema ready.");
        return 0;
    }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        // Never print connection strings or server/provider exception details.
        Console.Error.WriteLine("Schema/seed setup refused or unavailable. Check environment, synthetic target and local SQL access.");
        return 2;
    }
}

app.UseExceptionHandler();
app.UseCloudHosting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    if (context.Request.Path.StartsWithSegments("/api/v1") && context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            await ApiFailure.Result(400, "CSRF_INVALID", "Refresh the security token before submitting this request.").ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});
WarehouseRent.Api.Features.StockRequests.StockEndpoints.MapStockRequests(app);
WarehouseRent.Api.Features.StockPosting.StockPostingEndpoints.MapStockPosting(app);
WarehouseRent.Api.Features.StockCorrections.StockCorrectionEndpoints.MapStockCorrections(app);
WarehouseRent.Api.Features.Payments.FinanceEndpoints.MapFinance(app);
WarehouseRent.Api.Features.Settlements.SettlementEndpoints.MapSettlements(app);
WarehouseRent.Api.Features.Operations.OperationsEndpoints.MapOperations(app);
app.MapWorkspaceIdentity();
WarehouseRent.Api.Features.OnlinePayments.OnlinePaymentEndpoints.MapOnlinePayments(app);
WarehouseRent.Api.Features.Barcodes.BarcodeEndpoints.MapBarcodes(app);
WarehouseRent.Api.Features.Gates.GateEndpoints.MapGates(app);
app.MapIdentityOperations();
WarehouseRent.Api.Features.Documents.DocumentEndpoints.MapDocuments(app);
WarehouseRent.Api.Features.Reports.ReportEndpoints.MapReports(app);
WarehouseRent.Api.Features.Reports.ReceivablesEndpoints.MapReceivables(app);
WarehouseRent.Api.Features.Notices.NoticeEndpoints.MapNotices(app);
WarehouseRent.Api.Features.Notices.ReceivableReminderEndpoints.MapReceivableReminders(app);
app.MapOrganizations();
WarehouseRent.Api.Features.Facilities.FacilityEndpoints.MapFacilities(app);
WarehouseRent.Api.Features.Quotes.QuoteEndpoints.MapQuotes(app);
WarehouseRent.Api.Features.Bookings.BookingEndpoints.MapBookings(app);
WarehouseRent.Api.Features.Leases.LeaseEndpoints.MapLeases(app);
WarehouseRent.Api.Features.LeaseAmendments.RenewalPreviewEndpoints.MapRenewalPreview(app);
app.MapGet("/health/build", () => Results.Ok(BuildIdentity.Read(builder.Configuration)));
app.MapGet("/health/live", () => Results.Ok(new HealthStatus("live", DateTimeOffset.UtcNow)));
app.MapGet("/health/ready", async (ReadinessProbe probe, CancellationToken cancellationToken) =>
{
    var ready = await probe.CheckAsync(cancellationToken);
    return Results.Json(new HealthStatus(ready ? "ready" : "unavailable", DateTimeOffset.UtcNow), statusCode: ready ? 200 : 503);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallback(async context =>
{
    if (!HttpMethods.IsGet(context.Request.Method) || context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/health") || !File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    {
        context.Response.StatusCode = 404;
        return;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath!, "index.html"));
});
await app.RunAsync();
return 0;

public partial class Program;
