using System.Globalization;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Bookings;
using WarehouseRent.Api.Features.Payments;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.OnlinePayments;

public static class SePayNotification
{
    // This preliminary authentication deliberately does not parse or route untrusted JSON.
    public static bool Authenticate(ReadOnlyMemory<byte> body, string? signature, string? timestamp,
        string secret, DateTimeOffset now)
    {
        if (body.Length is 0 or > SePayBankTransferProtocol.MaximumBodyBytes || secret.Length < 32 ||
            timestamp is null || timestamp.Length > 19 ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds <= 0 || seconds.ToString(CultureInfo.InvariantCulture) != timestamp ||
            Math.Abs((decimal)now.ToUnixTimeSeconds() - seconds) > 300 ||
            signature is not { Length: 71 } || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        try
        {
            var prefix = Encoding.ASCII.GetBytes(timestamp + ".");
            var signed = new byte[prefix.Length + body.Length];
            prefix.CopyTo(signed, 0); body.Span.CopyTo(signed.AsSpan(prefix.Length));
            return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed),
                Convert.FromHexString(signature[7..]));
        }
        catch (FormatException) { return false; }
    }

    public static async Task<IResult> Receive(HttpContext ctx, AppDbContext db, FinanceService finance,
        IConfiguration config, IHostEnvironment env, CancellationToken ct)
    {
        var settings = SePaySettings.Read(config, env);
        if (settings == null) return Results.StatusCode(503);
        if (ctx.Request.ContentLength > SePayBankTransferProtocol.MaximumBodyBytes) return Results.StatusCode(413);
        if (!ctx.Request.Headers.TryGetValue("X-SePay-Signature", out var signatures) || signatures.Count != 1 ||
            !ctx.Request.Headers.TryGetValue("X-SePay-Timestamp", out var timestamps) || timestamps.Count != 1) return Results.Unauthorized();
        try
        {
            var buffer = new byte[SePayBankTransferProtocol.MaximumBodyBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await ctx.Request.Body.ReadAsync(buffer.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > SePayBankTransferProtocol.MaximumBodyBytes) return Results.StatusCode(413);
            var body = buffer.AsMemory(0, count);
            if (!Authenticate(body, signatures[0], timestamps[0], settings.Secret, DateTimeOffset.UtcNow)) return Results.Unauthorized();
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String ||
                code.GetString() is not { Length: 15 } reference) return Results.UnprocessableEntity();
            var known = await db.PaymentIntents.AsNoTracking().SingleOrDefaultAsync(x => x.BankTransferReference == reference, ct);
            if (known == null || !settings.Matches(known)) return Results.UnprocessableEntity();

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var locked = await finance.LockAccountAsync(known.RentalAccountId, ct);
            var intent = await DatabaseDialect.Rows<PaymentIntent>(db,
                $"SELECT * FROM [PaymentIntents] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={known.Id}").SingleAsync(ct);
            if (!settings.Matches(intent)) return Results.UnprocessableEntity();
            var matched = SePayBankTransferProtocol.Match(body, signatures[0], timestamps[0], settings.Secret,
                settings.Expected(intent), locked.Utc);
            if (matched == null) return Results.UnprocessableEntity();
            var transaction = matched.EventId.ToString(CultureInfo.InvariantCulture);
            var digest = Convert.ToHexString(SHA256.HashData(body.Span));
            if (intent.TransactionId != null)
            {
                var evidence = await db.SecurityAudits.AsNoTracking().Where(x => x.AggregateId == intent.Id &&
                    x.Action == "SePayPaymentNotification").Select(x => x.Reason).SingleOrDefaultAsync(ct);
                return intent.TransactionId == transaction && evidence?.EndsWith(";sha256=" + digest, StringComparison.Ordinal) == true
                    ? Results.NoContent() : Results.Conflict();
            }
            if (await db.PaymentIntents.AnyAsync(x => x.Provider == "SePay" && x.Mode == settings.Mode &&
                x.MerchantId == settings.MerchantId && x.TransactionId == transaction, ct)) return Results.Conflict();
            // Provider time has second precision. Earlier events cannot fund a newly created intent.
            var creationSecond = DateTimeOffset.FromUnixTimeSeconds(intent.CreatedAt.ToUnixTimeSeconds());
            if (matched.TransactionDate < creationSecond || matched.TransactionDate > locked.Utc.AddSeconds(1))
                return Results.UnprocessableEntity();
            var accepted = intent.State == "Ready" && locked.Account.State == "Open" &&
                BookingService.EffectiveState(locked.Booking, locked.Utc) is "Held" or "Confirmed" &&
                locked.Utc < intent.ExpiresAt && matched.TransactionDate < intent.ExpiresAt;
            intent.TransactionId = transaction;
            intent.State = accepted ? "Paid" : "ReviewRequired";
            intent.PaidAt = matched.TransactionDate.ToUniversalTime();
            intent.UpdatedAt = locked.Utc;
            if (accepted && settings.Mode == "Live")
            {
                var receipt = new PaymentReceipt { Id = Guid.NewGuid(), RentalAccountId = intent.RentalAccountId,
                    OrganizationId = intent.OrganizationId, FacilityId = intent.FacilityId, Method = "SePay",
                    Reference = settings.MerchantId + ":" + transaction, EvidenceReference = $"Signed SePay IPN; payment {intent.Id:N}",
                    Amount = intent.Amount, State = "Verified", ReceivedAt = matched.TransactionDate.ToUniversalTime(), VerifiedAt = locked.Utc,
                    VerifiedBy = "gateway:SePay", UpdatedAt = locked.Utc };
                db.PaymentReceipts.Add(receipt); intent.PaymentReceiptId = receipt.Id;
                locked.Account.UpdatedAt = locked.Utc;
            }
            db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = "gateway:SePay",
                OrganizationId = intent.OrganizationId, FacilityId = intent.FacilityId, AggregateId = intent.Id,
                Action = "SePayPaymentNotification", Reason = $"{settings.Mode}:{intent.State};event={transaction};sha256={digest}",
                CorrelationId = ctx.TraceIdentifier, At = locked.Utc });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException or InvalidOperationException)
        { return Results.UnprocessableEntity(); }
        catch (DbUpdateException) { return Results.Conflict(); }
        catch (DbException) { return Results.StatusCode(503); }
        catch (IOException) { return Results.BadRequest(); }
    }
}
