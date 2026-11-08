using System.Security.Cryptography;
using System.Text;
using WarehouseRent.Infrastructure.Finance;

namespace WarehouseRent.Api.Features.OnlinePayments;

public sealed record SePaySettings(string Mode, string Bank, string AccountNumber, string Secret)
{
    public string MerchantId => RecipientFingerprint(Bank, AccountNumber);
    public static string RecipientFingerprint(string bank, string account) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bank + "\0" + account)));
    public static string NewReference() =>
        "SEVQR" + RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 10);
    public SePayExpectedTransfer Expected(PaymentIntent intent) =>
        new(Bank, AccountNumber, checked((long)intent.Amount), intent.BankTransferReference!);
    public bool Matches(PaymentIntent intent) => intent.Provider == "SePay" && intent.Method == "SePayQr" &&
        intent.Mode == Mode && intent.MerchantId == MerchantId && intent.ExpiresAt is not null &&
        SePayBankTransferProtocol.IsValid(Expected(intent));
    public static SePaySettings? Read(IConfiguration config, IHostEnvironment env)
    {
        string Get(string key) => config["Payments:SePay:" + key] ?? "";
        var mode = Get("Mode");
        if (Get("Enabled") != "true" || Get("Secret").Length is < 32 or > 4096 ||
            mode is not ("Sandbox" or "Live")) return null;
        var budgetApproved = Get("ZeroFeeCapVerified") == "true" || Get("OverageFeesApproved") == "true";
        if (mode == "Live" && (!env.IsProduction() || Get("AllowLive") != "true" || !budgetApproved)) return null;
        // Sandbox recipients are deliberately non-bank fixtures; even then no QR URL is exposed.
        if (mode == "Sandbox" && (!(env.IsDevelopment() || env.IsEnvironment("Demo") || env.IsEnvironment("Testing")) ||
            Get("Bank") != "TESTBANK" || !Get("AccountNumber").StartsWith("TEST", StringComparison.Ordinal))) return null;
        if (!SePayBankTransferProtocol.IsValid(new(Get("Bank"), Get("AccountNumber"), 1000, "SEVQR0000000000"))) return null;
        return new(mode, Get("Bank"), Get("AccountNumber"), Get("Secret"));
    }
    public object? View(PaymentIntent intent) => intent.State != "Ready" || !Matches(intent) || intent.ExpiresAt <= DateTimeOffset.UtcNow ? null :
        new { bank = Bank, accountNumber = AccountNumber, reference = intent.BankTransferReference,
            expiresAt = intent.ExpiresAt, qrUrl = Mode == "Live" ? SePayBankTransferProtocol.BuildQrImageUri(Expected(intent)).AbsoluteUri : null };
}
