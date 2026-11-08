using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Api.Features.Payments;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Api.Features.Settlements;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.OnlinePayments;

public static class OnlinePaymentEndpoints
{
    public sealed record CreatePayment(Guid RentalAccountId, string Method, string Amount, string Reason);
    public sealed record PaymentAction(string Reason);
    private static bool Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 500;
    private static object View(PaymentIntent i, SePaySettings? bank = null) => new { id = i.Id, resourceId = i.Id, i.RentalAccountId, i.OrganizationId, i.FacilityId, i.Provider, i.Method, i.Mode, i.State,
        amount = i.Amount.ToString("0", CultureInfo.InvariantCulture), orderId = i.Id.ToString("N"), checkoutUrl = i.State == "Ready" ? i.CheckoutUrl : null, i.PaymentReceiptId, i.TransactionId, i.CreatedAt, i.PaidAt, bankTransfer = bank?.View(i), version = Convert.ToBase64String(i.Version) };
    private static async Task<bool> Access(AppDbContext db, CurrentActor actor, Guid org, Guid facility, bool customerOnly, CancellationToken ct)
        => await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, ct) && (await actor.MembershipAsync(org, ct) is not null || !customerOnly && await actor.HasFacilityAsync(facility, "Finance", ct));
    private static IResult Unavailable() => ApiFailure.Result(503, "PAYMENTS_NOT_CONFIGURED", "Online payment is unavailable. Use a manual payment method or contact the operator.");
    private static IResult Invalid() => ApiFailure.Result(422, "PAYMENT_INVALID", "Select a supported method, a whole-VND amount from1000 to50000000 and a reason.");
    public static void MapOnlinePayments(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/payment-methods", (IConfiguration config, IHostEnvironment env) => Results.Ok(new { items = new[] { "VnpayCard", "VnpayBank", "VnpayQr", "MomoWallet", "SePayQr" }.Select(method => {
            if (method == "SePayQr") { var bank = SePaySettings.Read(config, env); return new { method, available = bank != null, mode = bank?.Mode ?? "Disabled" }; }
            var p = method.StartsWith("Vnpay", StringComparison.Ordinal) ? "Vnpay" : "Momo"; var s = GatewaySettings.Read(config, env, p); return new { method, available = s != null, mode = s?.Mode ?? "Disabled" }; }) }));
        api.MapPost("/payment-intents", async (CreatePayment input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext ctx, IConfiguration config, IHostEnvironment env, CancellationToken ct) => {
            var account = await db.RentalAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.RentalAccountId, ct); if (account == null) return OrganizationEndpoints.Missing();
            var facility = await db.Bookings.Where(x => x.Id == account.BookingId).Select(x => x.FacilityId).SingleAsync(ct);
            if (!await Access(db, actor, account.OrganizationId, facility, true, ct)) return OrganizationEndpoints.Missing();
            if (input.Method is not ("VnpayCard" or "VnpayBank" or "VnpayQr" or "MomoWallet" or "SePayQr") || !Reason(input.Reason) || input.Amount is null || !decimal.TryParse(input.Amount, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 1000 || amount > 50000000) return Invalid();
            var provider = input.Method == "SePayQr" ? "SePay" : input.Method.StartsWith("Vnpay", StringComparison.Ordinal) ? "Vnpay" : "Momo"; var settings = GatewaySettings.Read(config, env, provider); var bank = provider == "SePay" ? SePaySettings.Read(config, env) : null; if (settings == null && bank == null) return Unavailable();
            return await commands.ExecuteAsync(ctx, account.OrganizationId, "CreateOnlinePayment", input, async token => {
                var locked = await finance.LockAccountAsync(account.Id, token); await IdentityWriteLock.Acquire(db, token);
                if (!await Access(db, actor, account.OrganizationId, facility, true, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Payment account unavailable.");
                SettlementService.RequireOpen(locked.Account);
                if (bank != null && (locked.Account.State != "Open" || BookingService.EffectiveState(locked.Booking, locked.Utc) is not ("Held" or "Confirmed")))
                    throw new CommandFailure(409, "PAYMENT_ACCOUNT_NOT_OPEN", "The payment account is not open for new transfers.");
                var intent = new PaymentIntent { Id = Guid.NewGuid(), RentalAccountId = account.Id, OrganizationId = account.OrganizationId, FacilityId = facility, Provider = provider, Mode = bank?.Mode ?? settings!.Mode, MerchantId = bank?.MerchantId ?? settings!.MerchantId, Method = input.Method, Amount = amount, CreatedBy = actor.UserId, CreatedAt = locked.Utc, UpdatedAt = locked.Utc };
                if (bank != null) { intent.BankTransferReference = SePaySettings.NewReference(); intent.ExpiresAt = locked.Utc.AddMinutes(15); intent.State = "Ready"; }
                db.PaymentIntents.Add(intent); locked.Account.UpdatedAt = locked.Utc; commands.Audit(ctx, "PaymentIntentCreated", intent.Id, input.Reason, intent.OrganizationId, facility); await db.SaveChangesAsync(token); return View(intent, bank);
            }, ct, organizationId: account.OrganizationId, facilityId: facility, facilityRole: "Finance",
                authorize: bank == null ? null : async token => {
                    await finance.LockAccountAsync(account.Id, token); await IdentityWriteLock.Acquire(db, token);
                    if (!await Access(db, actor, account.OrganizationId, facility, true, token))
                        throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Payment account unavailable.");
                });
        });
        api.MapGet("/payment-intents/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, IConfiguration config, IHostEnvironment env, CancellationToken ct) => {
            var i = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (i == null || !await Access(db, actor, i.OrganizationId, i.FacilityId, false, ct)) return OrganizationEndpoints.Missing(); return Results.Ok(View(i, SePaySettings.Read(config, env)));
        });
        api.MapGet("/accounts/{id:guid}/payment-intents", async (Guid id, int? page, AppDbContext db, CurrentActor actor, IConfiguration config, IHostEnvironment env, CancellationToken ct) => {
            var account = await db.RentalAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (account == null) return OrganizationEndpoints.Missing(); var facility = await db.Bookings.Where(x => x.Id == account.BookingId).Select(x => x.FacilityId).SingleAsync(ct);
            if (!await Access(db, actor, account.OrganizationId, facility, false, ct)) return OrganizationEndpoints.Missing(); var number = page ?? 1; if (number is < 1 or > 10000) return Invalid();
            var query = db.PaymentIntents.AsNoTracking().Where(x => x.RentalAccountId == id); var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * 50).Take(50).ToListAsync(ct);
            return Results.Ok(new { organizationId = account.OrganizationId, facilityId = facility, page = number, total = await query.CountAsync(ct), items = rows.Select(i => View(i, SePaySettings.Read(config, env))) });
        });
        api.MapPost("/payment-intents/{id:guid}/checkout", Checkout);
        api.MapPost("/payment-intents/{id:guid}/abandon", async (Guid id, PaymentAction input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, HttpContext ctx, CancellationToken ct) => {
            var known = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (known == null || !await Access(db, actor, known.OrganizationId, known.FacilityId, true, ct)) return OrganizationEndpoints.Missing(); if (!Reason(input.Reason)) return Invalid();
            return await commands.ExecuteAsync(ctx, known.OrganizationId, "AbandonUnsentPayment", new { id, input }, async token => {
                var locked = await finance.LockAccountAsync(known.RentalAccountId, token); await IdentityWriteLock.Acquire(db, token); if (!await Access(db, actor, known.OrganizationId, known.FacilityId, true, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Payment unavailable.");
                var current = await LockIntent(db, id, token); CommandExecutor.Version(ctx, current.Version); if (current.State != "Created" && !(current.Provider == "SePay" && current.State == "Ready")) throw new CommandFailure(409, "PAYMENT_ALREADY_DISPATCHED", "Provider outcome must be reconciled; an attempted checkout cannot be abandoned locally.");
                current.State = "Failed"; current.UpdatedAt = locked.Utc; commands.Audit(ctx, current.Provider == "SePay" ? "SePayPaymentCancelled" : "UnsentPaymentAbandoned", id, input.Reason, known.OrganizationId, known.FacilityId); await db.SaveChangesAsync(token); return View(current);
            }, ct, organizationId: known.OrganizationId, facilityId: known.FacilityId, facilityRole: "Finance");
        });
        // Exact server-to-server endpoints live outside the session API/CSRF namespace. Signed payloads are their only authority.
        app.MapGet("/api/payment-notifications/vnpay", async (HttpContext ctx, AppDbContext db, FinanceService finance, IConfiguration config, IHostEnvironment env, CancellationToken ct) => {
            IResult Reply(string code) => Results.Json(new Dictionary<string, string> { ["RspCode"] = code, ["Message"] = code == "00" ? "Confirm Success" : "Not confirmed" });
            var settings = GatewaySettings.Read(config, env, "Vnpay"); if (settings == null) return Reply("99");
            if (ctx.Request.QueryString.Value?.Length > 16384 || ctx.Request.Query.Any(x => x.Value.Count != 1 || !x.Key.StartsWith("vnp_", StringComparison.Ordinal)) || ctx.Request.Query.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ctx.Request.Query.Count) return Reply("97");
            var values = ctx.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
            // Checkout links use the same signing key. Their valid signature alone is not a payment-result signature.
            if (values.GetValueOrDefault("vnp_ResponseCode") is not { Length: 2 } responseCode || !responseCode.All(char.IsAsciiDigit) || values.GetValueOrDefault("vnp_TransactionStatus") is not { Length: 2 } transactionStatus || !transactionStatus.All(char.IsAsciiDigit)) return Reply("97");
            if (!PaymentGateway.Signature(PaymentGateway.VnpayData(values), settings.Secret, values.GetValueOrDefault("vnp_SecureHash"), true)) return Reply("97");
            if (values.GetValueOrDefault("vnp_TmnCode") != settings.MerchantId || !Guid.TryParseExact(values.GetValueOrDefault("vnp_TxnRef"), "N", out var id)) return Reply("01");
            if (!decimal.TryParse(values.GetValueOrDefault("vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var cents) || cents % 100 != 0) return Reply("04");
            var outcome = responseCode == "00" && transactionStatus == "00" ? NotificationOutcome.Paid
                : transactionStatus == "02" && responseCode is "05" or "09" or "10" or "11" or "12" or "13" or "24" or "51" or "65" or "75" or "79" ? NotificationOutcome.Failed : NotificationOutcome.Unresolved;
            try { return Reply(await Notify(id, cents / 100, values.GetValueOrDefault("vnp_TransactionNo"), outcome, $"response={responseCode};status={transactionStatus}", settings, db, finance, ctx, ct)); }
            catch (DbUpdateException) { return Reply("99"); }
        }).AllowAnonymous();
        app.MapPost("/api/payment-notifications/momo", MomoNotification).AllowAnonymous();
        app.MapPost("/api/payment-notifications/sepay", SePayNotification.Receive).AllowAnonymous();
        app.MapGet("/payments/return", () => Results.Content("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Payment result</title><h1>Check your payment status</h1><p>This return page does not confirm payment. Return to WarehouseRent Finance and read the payment intent. Only verified provider notifications update its status.</p><a href=\"/#finance\">Return to Finance</a></html>", "text/html"));
    }
    private static async Task<IResult> Checkout(Guid id, PaymentAction input, AppDbContext db, CurrentActor actor, FinanceService finance, CommandExecutor commands, PaymentGateway gateway, IConfiguration config, IHostEnvironment env, HttpContext ctx, CancellationToken ct)
    {
        var known = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (known == null || !await Access(db, actor, known.OrganizationId, known.FacilityId, true, ct)) return OrganizationEndpoints.Missing();
        if (!Reason(input.Reason)) return Invalid(); var settings = GatewaySettings.Read(config, env, known.Provider); if (settings == null || !settings.Matches(known)) return Unavailable();
        var dispatch = false; var committed = false; PaymentIntent? original = null;
        var receipt = await commands.ExecuteAsync(ctx, known.OrganizationId, "OpenOnlineCheckout", new { id, input }, async token => {
            var locked = await finance.LockAccountAsync(known.RentalAccountId, token); await IdentityWriteLock.Acquire(db, token);
            if (!await Access(db, actor, known.OrganizationId, known.FacilityId, true, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "Payment unavailable.");
            SettlementService.RequireOpen(locked.Account); var intent = await LockIntent(db, id, token);
            if (intent.State == "Created") { intent.State = "Creating"; intent.UpdatedAt = locked.Utc; original = intent; dispatch = true; commands.Audit(ctx, "OnlineCheckoutStarted", id, input.Reason, intent.OrganizationId, intent.FacilityId); }
            await db.SaveChangesAsync(token); return View(intent);
        }, ct, organizationId: known.OrganizationId, facilityId: known.FacilityId, facilityRole: "Finance", onCommitted: () => committed = true);
        // Delegate state alone does not prove that the durable dispatch claim committed.
        if (!committed || !dispatch || original == null) return receipt;
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        string? url = null;
        try { url = await gateway.Checkout(settings, original, ctx.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", completion.Token); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or FormatException or KeyNotFoundException or JsonException) { /* Unknown external outcome: never repeat create. */ }
        // Finalization is separate from the committed dispatch claim. A dropped caller can leave Creating; IPN still reconciles it.
        if (!completion.IsCancellationRequested)
        {
            db.ChangeTracker.Clear(); await using var tx = await db.Database.BeginTransactionAsync(completion.Token); var locked = await finance.LockAccountAsync(known.RentalAccountId, completion.Token); var current = await LockIntent(db, id, completion.Token);
            if (current.State == "Creating") { current.CheckoutUrl = settings.SafeCheckout(url) ? url : null; current.State = current.CheckoutUrl == null ? "Unknown" : "Ready"; current.UpdatedAt = locked.Utc; await db.SaveChangesAsync(completion.Token); }
            await tx.CommitAsync(completion.Token);
        }
        return receipt;
    }
    private static Task<PaymentIntent> LockIntent(AppDbContext db, Guid id, CancellationToken ct) => DatabaseDialect.Rows<PaymentIntent>(db, $"SELECT * FROM [PaymentIntents] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
    private static async Task<IResult> MomoNotification(HttpContext ctx, AppDbContext db, FinanceService finance, IConfiguration config, IHostEnvironment env, CancellationToken ct)
    {
        var settings = GatewaySettings.Read(config, env, "Momo"); if (settings == null) return Unavailable();
        if (ctx.Request.ContentLength > 16384) return Results.StatusCode(413);
        try
        {
            var bytes = new byte[16385]; var length = 0; while (length < bytes.Length) { var n = await ctx.Request.Body.ReadAsync(bytes.AsMemory(length), ct); if (n == 0) break; length += n; } if (length > 16384) return Results.StatusCode(413);
            using var json = JsonDocument.Parse(bytes.AsMemory(0, length)); var values = PaymentGateway.ParseObject(json.RootElement);
            if (!PaymentGateway.Signature(PaymentGateway.MomoData(values, settings.AccessKey), settings.Secret, values.GetValueOrDefault("signature"))) return Results.Unauthorized();
            if (values["partnerCode"] != settings.MerchantId || values["requestId"] != values["orderId"] || !Guid.TryParseExact(values["orderId"], "N", out var id) || values["extraData"] != "" || values["orderInfo"] != "WarehouseRent " + values["orderId"] || values["orderType"] != "momo_wallet" || !decimal.TryParse(values["amount"], NumberStyles.None, CultureInfo.InvariantCulture, out var amount)) return Invalid();
            var code = values["resultCode"];
            if (code.Length is < 1 or > 8 || !code.All(char.IsAsciiDigit)) return Invalid();
            var outcome = code == "0" ? NotificationOutcome.Paid
                : code is "1001" or "1002" or "1003" or "1004" or "1005" or "1006" or "1007" or "1017" ? NotificationOutcome.Failed : NotificationOutcome.Unresolved;
            var result = await Notify(id, amount, values["transId"], outcome, $"result={code}", settings, db, finance, ctx, ct);
            return result is "00" or "02" ? Results.NoContent() : Results.Conflict();
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException) { return Invalid(); }
        catch (DbUpdateException) { return Results.Conflict(); }
    }
    private enum NotificationOutcome { Paid, Failed, Unresolved }
    private static async Task<string> Notify(Guid id, decimal amount, string? transaction, NotificationOutcome outcome, string evidence, GatewaySettings settings, AppDbContext db, FinanceService finance, HttpContext ctx, CancellationToken ct)
    {
        var known = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct); if (known == null || !settings.Matches(known)) return "01";
        if (known.Amount != amount) return "04";
        var success = outcome == NotificationOutcome.Paid;
        if (transaction is { Length: > 100 } || transaction is { Length: > 0 } && !transaction.All(char.IsAsciiDigit)) return "99";
        if (string.IsNullOrEmpty(transaction) || transaction.All(x => x == '0')) transaction = null;
        if (success && transaction == null) return "99";
        await using var tx = await db.Database.BeginTransactionAsync(ct); var locked = await finance.LockAccountAsync(known.RentalAccountId, ct); var intent = await LockIntent(db, id, ct);
        if (intent.TransactionId != null && transaction != null && intent.TransactionId != transaction) return "99";
        if (intent.State is "Paid" or "ReviewRequired") return intent.TransactionId == transaction ? "02" : "99";
        if (intent.State == "Created") return "99";
        if (transaction != null)
        {
            if (await db.PaymentIntents.AnyAsync(x => x.Id != id && x.Provider == settings.Provider && x.Mode == settings.Mode && x.MerchantId == settings.MerchantId && x.TransactionId == transaction, ct)) return "99";
            intent.TransactionId = transaction;
        }
        if (success)
        {
            intent.PaidAt = locked.Utc;
            intent.State = locked.Account.State == "Closed" ? "ReviewRequired" : "Paid";
            if (settings.Mode == "Live" && intent.State == "Paid")
            {
                var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = intent.RentalAccountId, OrganizationId = intent.OrganizationId, FacilityId = intent.FacilityId, Method = settings.Provider,
                    Reference = transaction!, EvidenceReference = $"Signed {settings.Provider} IPN; payment {id:N}", Amount = intent.Amount, State = "Recorded", ReceivedAt = locked.Utc, UpdatedAt = locked.Utc };
                db.PaymentReceipts.Add(receipt); intent.PaymentReceiptId = receipt.Id; locked.Account.UpdatedAt = locked.Utc;
            }
        }
        else intent.State = outcome == NotificationOutcome.Failed ? "Failed" : "Unknown";
        intent.UpdatedAt = locked.Utc;
        db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = "gateway:" + settings.Provider, OrganizationId = intent.OrganizationId, FacilityId = intent.FacilityId, Action = "PaymentNotification", AggregateId = id,
            Reason = $"{settings.Mode}:{intent.State};{evidence};transaction={transaction ?? "none"}", CorrelationId = ctx.TraceIdentifier, At = locked.Utc });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return "00";
    }
}
