using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarehouseRent.Infrastructure.Finance;
namespace WarehouseRent.Api.Features.OnlinePayments;

public sealed record GatewaySettings(string Provider, string Mode, string MerchantId, string Secret, string AccessKey, string PublicOrigin)
{
    public string ReturnUrl => PublicOrigin + "/payments/return";
    public string NotifyUrl => PublicOrigin + "/api/payment-notifications/" + Provider.ToLowerInvariant();
    public string GatewayUrl => Provider == "Vnpay" ? Mode == "Live" ? "https://pay.vnpay.vn/vpcpay.html" : "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html" : (Mode == "Live" ? "https://payment.momo.vn" : "https://test-payment.momo.vn") + "/v2/gateway/api/create";
    public static GatewaySettings? Read(IConfiguration config, IHostEnvironment env, string provider)
    {
        if (provider is not ("Vnpay" or "Momo")) return null;
        string Get(string key) => config[$"Payments:{provider}:{key}"] ?? "";
        var mode = Get("Mode"); var origin = config["Payments:PublicOrigin"] ?? "";
        if (mode is not ("Sandbox" or "Live") || mode == "Live" && (!env.IsProduction() || config["Payments:AllowLive"] != "true") ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath != "/" || uri.Query != "" || uri.Fragment != "" || !uri.IsDefaultPort ||
            Get("MerchantId").Length is < 1 or > 64 || !Get("MerchantId").All(c => char.IsAsciiLetterOrDigit(c) || provider == "Momo" && c is '_' or '-') || Get("Secret").Length < 16 || provider == "Momo" && string.IsNullOrEmpty(Get("AccessKey"))) return null;
        return new(provider, mode, Get("MerchantId"), Get("Secret"), Get("AccessKey"), uri.GetLeftPart(UriPartial.Authority));
    }
    public bool Matches(PaymentIntent intent) => intent.Provider == Provider && intent.Mode == Mode && intent.MerchantId == MerchantId;
    public bool SafeCheckout(string? value)
    {
        if (value?.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Fragment != "") return false;
        var target = new Uri(GatewayUrl);
        return uri.Host == target.Host && uri.AbsolutePath == (Provider == "Momo" ? "/v2/gateway/pay" : target.AbsolutePath);
    }
}
public sealed class PaymentGateway(HttpClient http)
{
    public static string Hmac(string data, string secret, bool sha512 = false) => Convert.ToHexString(sha512 ? HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data)) : HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    public static bool Signature(string raw, string secret, string? signature, bool sha512 = false)
    {
        if (signature?.Length != (sha512 ? 128 : 64)) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hmac(raw, secret, sha512)), Convert.FromHexString(signature)); } catch (FormatException) { return false; }
    }
    public static string VnpayData(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Where(x => x.Value != "" && x.Key is not ("vnp_SecureHash" or "vnp_SecureHashType")).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => WebUtility.UrlEncode(x.Key) + "=" + WebUtility.UrlEncode(x.Value)));
    public static string MomoData(IReadOnlyDictionary<string, string> data, string accessKey, bool response = false)
    {
        var keys = response ? new[] { "amount", "message", "orderId", "partnerCode", "payUrl", "requestId", "responseTime", "resultCode" } : new[] { "amount", "extraData", "message", "orderId", "orderInfo", "orderType", "partnerCode", "payType", "requestId", "responseTime", "resultCode", "transId" };
        return "accessKey=" + accessKey + "&" + string.Join("&", keys.Select(k => k + "=" + data[k]));
    }
    public static Dictionary<string, string> ParseObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException(); var result = new Dictionary<string, string>(StringComparer.Ordinal); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in root.EnumerateObject()) { if (!names.Add(field.Name) || field.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null)) throw new FormatException(); var value = field.Value.ValueKind == JsonValueKind.String ? field.Value.GetString()! : field.Value.GetRawText(); if (value.Length > 4096) throw new FormatException(); result.Add(field.Name, value); }
        return result;
    }
    public async Task<string> Checkout(GatewaySettings settings, PaymentIntent intent, string ip, CancellationToken ct)
    {
        var order = intent.Id.ToString("N"); var amount = intent.Amount.ToString("0", CultureInfo.InvariantCulture); var info = "WarehouseRent " + order;
        if (settings.Provider == "Vnpay")
        {
            var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
            var data = new Dictionary<string, string> { ["vnp_Version"] = "2.1.0", ["vnp_Command"] = "pay", ["vnp_TmnCode"] = settings.MerchantId, ["vnp_Amount"] = (intent.Amount * 100).ToString("0", CultureInfo.InvariantCulture), ["vnp_CurrCode"] = "VND", ["vnp_TxnRef"] = order, ["vnp_OrderInfo"] = info, ["vnp_OrderType"] = "other", ["vnp_ReturnUrl"] = settings.ReturnUrl, ["vnp_IpAddr"] = ip, ["vnp_Locale"] = "vn", ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), ["vnp_ExpireDate"] = now.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), ["vnp_BankCode"] = intent.Method == "VnpayCard" ? "INTCARD" : intent.Method == "VnpayBank" ? "VNBANK" : "VNPAYQR" };
            var raw = VnpayData(data); return settings.GatewayUrl + "?" + raw + "&vnp_SecureHash=" + Hmac(raw, settings.Secret, true);
        }
        var request = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["accessKey"] = settings.AccessKey, ["amount"] = amount, ["extraData"] = "", ["ipnUrl"] = settings.NotifyUrl, ["orderId"] = order, ["orderInfo"] = info, ["partnerCode"] = settings.MerchantId, ["redirectUrl"] = settings.ReturnUrl, ["requestId"] = order, ["requestType"] = "captureWallet" };
        var signature = Hmac(string.Join("&", request.Select(x => x.Key + "=" + x.Value)), settings.Secret);
        using var response = await http.PostAsJsonAsync(settings.GatewayUrl, new { partnerCode = settings.MerchantId, requestId = order, amount = (long)intent.Amount, orderId = order, orderInfo = info, redirectUrl = settings.ReturnUrl, ipnUrl = settings.NotifyUrl, requestType = "captureWallet", extraData = "", autoCapture = true, lang = "en", signature }, ct);
        response.EnsureSuccessStatusCode(); await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[16385]; var length = 0; while (length < bytes.Length) { var n = await stream.ReadAsync(bytes.AsMemory(length), ct); if (n == 0) break; length += n; } if (length > 16384) throw new FormatException();
        using var document = JsonDocument.Parse(bytes.AsMemory(0, length)); var values = ParseObject(document.RootElement);
        if (!Signature(MomoData(values, settings.AccessKey, true), settings.Secret, values.GetValueOrDefault("signature")) || values["partnerCode"] != settings.MerchantId || values["orderId"] != order || values["requestId"] != order || values["amount"] != amount || values["resultCode"] != "0" || !settings.SafeCheckout(values["payUrl"])) throw new FormatException();
        return values["payUrl"];
    }
}
