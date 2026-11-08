using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarehouseRent.Api.Features.OnlinePayments;

/// <summary>Caller-supplied, privately approved recipient and order expectation; no account defaults.</summary>
public sealed record SePayExpectedTransfer(string Bank, string Account, long Amount, string OrderReference);

/// <summary>
/// An authenticated, exactly matched bank event only, NOT a payment, receipt or durable acceptance.
/// A future integration must atomically deduplicate EventId and validate/update its own pending order
/// and financial journals in durable storage before acknowledging or reporting payment success.
/// </summary>
public sealed record SePayMatchedEvent(long EventId, string Bank, string Account, long Amount,
    string OrderReference, DateTimeOffset TransactionDate, string? ReferenceCode);

/// <summary>
/// Offline protocol foundation: no HTTP, endpoint registration, configuration reads, DB or state changes.
/// Based on developer.sepay.vn/vi/sepay-webhooks/{xac-thuc,tich-hop-webhook} (2026-11-05).
/// Only bounded JSON and documented HMAC headers are supported; never fall back to unauthenticated mode.
/// Freshness limits replay age but does NOT provide durable deduplication or order ownership checks.
/// </summary>
public static class SePayBankTransferProtocol
{
    public const int MaximumBodyBytes = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool IsValid(SePayExpectedTransfer? expected) => expected is not null &&
        expected.Bank is { Length: > 0 and <= 64 } && !string.IsNullOrWhiteSpace(expected.Bank) &&
        expected.Bank == expected.Bank.Trim() && expected.Bank.All(c => c is >= ' ' and <= '~') &&
        expected.Account is { Length: > 0 and <= 64 } && expected.Account.All(char.IsAsciiLetterOrDigit) &&
        expected.Amount is >= 1000 and <= 50_000_000 &&
        expected.OrderReference is { Length: 15 } && expected.OrderReference.StartsWith("SEVQR", StringComparison.Ordinal) &&
        expected.OrderReference.AsSpan(5).ContainsAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") == false;

    /// <summary>
    /// Verifies X-SePay-Signature=sha256={hex} over exact X-SePay-Timestamp + '.' + raw bytes,
    /// using a >=32-character secret and an injected clock with a fixed +/-300-second tolerance.
    /// Null means untrusted, malformed or not exactly matching; this function has no side effects.
    /// </summary>
    public static SePayMatchedEvent? Match(ReadOnlyMemory<byte> rawBody, string? signature, string? timestampHeader,
        string? secret, SePayExpectedTransfer? expected, DateTimeOffset now)
    {
        if (!IsValid(expected) || rawBody.Length is 0 or > MaximumBodyBytes || string.IsNullOrWhiteSpace(secret) ||
            secret.Length < 32 || timestampHeader is null || timestampHeader.Length > 19 ||
            !long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp) ||
            timestamp <= 0 || timestamp.ToString(CultureInfo.InvariantCulture) != timestampHeader ||
            Math.Abs((decimal)now.ToUnixTimeSeconds() - timestamp) > 300 ||
            signature is not { Length: 71 } || !signature.StartsWith("sha256=", StringComparison.Ordinal)) return null;

        try
        {
            // Validate UTF-8 before parsing; a replacement character must never mask invalid raw bytes.
            StrictUtf8.GetCharCount(rawBody.Span);
            var prefix = Encoding.ASCII.GetBytes(timestampHeader + ".");
            var signedBytes = new byte[prefix.Length + rawBody.Length];
            prefix.CopyTo(signedBytes, 0);
            rawBody.Span.CopyTo(signedBytes.AsSpan(prefix.Length));
            var digest = HMACSHA256.HashData(StrictUtf8.GetBytes(secret), signedBytes);
            if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(signature[7..]))) return null;

            using var document = JsonDocument.Parse(rawBody, new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (!names.Add(field.Name) || field.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) return null;
                fields.Add(field.Name, field.Value);
            }

            string? Text(string key) => fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            bool Integer(string key, out long number)
            {
                number = 0;
                return fields.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number &&
                    value.GetRawText().All(char.IsAsciiDigit) && value.TryGetInt64(out number);
            }
            if (!Integer("id", out var id) || id <= 0 || Text("gateway") != expected!.Bank ||
                Text("accountNumber") != expected.Account || Text("transferType") != "in" ||
                Text("code") != expected.OrderReference || !Integer("transferAmount", out var amount) || amount != expected.Amount ||
                Text("content") is null ||
                !DateTime.TryParseExact(Text("transactionDate"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var localDate)) return null;

            foreach (var optional in new[] { "subAccount", "description", "referenceCode" })
                if (fields.ContainsKey(optional) && Text(optional) is null) return null;
            if (fields.ContainsKey("accumulated") && !Integer("accumulated", out _)) return null;

            // SePay documents this timestamp as Vietnamese local wall time (GMT+7), not host local time.
            var date = new DateTimeOffset(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), TimeSpan.FromHours(7));
            return new(id, expected.Bank, expected.Account, amount, expected.OrderReference, date, Text("referenceCode"));
        }
        // JsonDocument defers escaped UTF-16 validation until Name/GetString access and throws
        // InvalidOperationException for lone surrogates, even when the raw bytes are valid UTF-8.
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Constructs ONLY the documented vietqr.app image URI; never fetches or renders it.
    /// Future rendering discloses recipient, amount and order reference to that QR provider and requires
    /// approved private recipient configuration and an explicit privacy/zero-spend activation decision.
    /// https://developer.sepay.vn/vi/sepay-webhooks/tao-qr-va-form-thanh-toan
    /// </summary>
    public static Uri BuildQrImageUri(SePayExpectedTransfer expected)
    {
        if (!IsValid(expected)) throw new ArgumentException("A valid explicit transfer expectation is required.", nameof(expected));
        return new Uri("https://vietqr.app/img?bank=" + Uri.EscapeDataString(expected.Bank) +
            "&acc=" + Uri.EscapeDataString(expected.Account) +
            "&amount=" + expected.Amount.ToString(CultureInfo.InvariantCulture) +
            "&des=" + Uri.EscapeDataString(expected.OrderReference));
    }
}
