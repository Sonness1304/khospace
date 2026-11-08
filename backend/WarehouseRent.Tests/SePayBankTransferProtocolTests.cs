using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WarehouseRent.Api.Features.OnlinePayments;

namespace WarehouseRent.Tests;

public sealed class SePayBankTransferProtocolTests
{
    private const string Secret = "SYNTHETIC_SECRET_0123456789_ABCDEFG";
    private const string Timestamp = "1793836800";
    private const string Body = "{\"id\":42,\"gateway\":\"FixtureBank\",\"transactionDate\":\"2026-11-05 07:00:00\",\"accountNumber\":\"SYNTHETIC001\",\"code\":\"SEVQR012345ABCD\",\"content\":\"SEVQR012345ABCD\",\"transferType\":\"in\",\"transferAmount\":25000,\"referenceCode\":\"FIXTURE-42\"}";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Timestamp, CultureInfo.InvariantCulture));
    private static SePayExpectedTransfer Expected => new("FixtureBank", "SYNTHETIC001", 25000, "SEVQR012345ABCD");
    private static byte[] Bytes(string body) => Encoding.UTF8.GetBytes(body);
    private static string Sign(byte[] body, string timestamp = Timestamp, string secret = Secret)
    {
        var prefix = Bytes(timestamp + ".");
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Bytes(secret), prefix.Concat(body).ToArray())).ToLowerInvariant();
    }
    private static SePayMatchedEvent? Match(string body) => Match(Bytes(body));
    private static SePayMatchedEvent? Match(byte[] body) => SePayBankTransferProtocol.Match(body, Sign(body), Timestamp, Secret, Expected, Now);

    [Fact]
    public void Known_independent_node_crypto_vector_matches_only_an_authenticated_event()
    {
        // Independently computed with Node createHmac('sha256', Secret).update(Timestamp + '.' + Body).
        const string signature = "sha256=4a36795e51450eb2cf75b8d7d3043e52ccae5f831b0e237013ceaaa4e9722160";
        var matched = SePayBankTransferProtocol.Match(Bytes(Body), signature, Timestamp, Secret, Expected, Now);
        Assert.NotNull(matched);
        Assert.Equal(42, matched.EventId);
        Assert.Equal(Expected.Bank, matched.Bank);
        Assert.Equal(Expected.Account, matched.Account);
        Assert.Equal(Expected.Amount, matched.Amount);
        Assert.Equal(Expected.OrderReference, matched.OrderReference);
        Assert.Equal(TimeSpan.FromHours(7), matched.TransactionDate.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.FromHours(7)), matched.TransactionDate);
        Assert.Equal("FIXTURE-42", matched.ReferenceCode);
        Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body + " "), signature, Timestamp, Secret, Expected, Now));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("sha256=xyz")]
    [InlineData("SHA256=4a36795e51450eb2cf75b8d7d3043e52ccae5f831b0e237013ceaaa4e9722160")]
    [InlineData("sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("sha256=0000000000000000000000000000000000000000000000000000000000000000")]
    public void Rejects_missing_malformed_or_wrong_signature(string? signature) =>
        Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body), signature, Timestamp, Secret, Expected, Now));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("-1")] [InlineData(" 1793836800")]
    [InlineData("+1793836800")] [InlineData("1793836800.0")] [InlineData("9999999999999999999999999")]
    [InlineData("01793836800")]
    public void Rejects_noncanonical_timestamp_headers(string? timestamp) =>
        Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body), Sign(Bytes(Body), timestamp ?? ""), timestamp, Secret, Expected, Now));

    [Theory]
    [InlineData(-301, false)] [InlineData(-300, true)] [InlineData(300, true)] [InlineData(301, false)]
    public void Injected_clock_bounds_past_and_future_freshness(int offset, bool accepted)
    {
        var timestamp = Now.AddSeconds(offset).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.Equal(accepted, SePayBankTransferProtocol.Match(Bytes(Body), Sign(Bytes(Body), timestamp), timestamp, Secret, Expected, Now) is not null);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("short")]
    [InlineData("1234567890123456789012345678901")]
    public void Rejects_missing_or_short_secrets_even_when_signed_with_them(string? secret) =>
        Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body), Sign(Bytes(Body), secret: secret ?? ""), Timestamp, secret, Expected, Now));

    [Theory]
    [InlineData("\"id\":42", "\"id\":0")]
    [InlineData("\"id\":42", "\"id\":-1")]
    [InlineData("\"id\":42", "\"id\":42.0")]
    [InlineData("\"id\":42", "\"id\":\"42\"")]
    [InlineData("\"id\":42", "\"id\":9223372036854775808")]
    [InlineData("FixtureBank", "fixturebank")]
    [InlineData("SYNTHETIC001", "OTHER001")]
    [InlineData("\"code\":\"SEVQR012345ABCD\"", "\"code\":null")]
    [InlineData("\"code\":\"SEVQR012345ABCD\"", "\"code\":\"\"")]
    [InlineData("\"code\":\"SEVQR012345ABCD\"", "\"code\":\"SEVQR012345ABCE\"")]
    [InlineData("\"transferType\":\"in\"", "\"transferType\":\"out\"")]
    [InlineData("\"transferType\":\"in\"", "\"transferType\":\"IN\"")]
    [InlineData("\"transferAmount\":25000", "\"transferAmount\":24999")]
    [InlineData("\"transferAmount\":25000", "\"transferAmount\":25001")]
    [InlineData("\"transferAmount\":25000", "\"transferAmount\":25000.0")]
    [InlineData("\"transferAmount\":25000", "\"transferAmount\":25e3")]
    [InlineData("\"transferAmount\":25000", "\"transferAmount\":\"25000\"")]
    [InlineData("\"content\":\"SEVQR012345ABCD\"", "\"content\":true")]
    [InlineData("\"gateway\":\"FixtureBank\"", "\"gateway\":{}")]
    [InlineData("\"accountNumber\":\"SYNTHETIC001\"", "\"accountNumber\":[]")]
    [InlineData("2026-11-05 07:00:00", "")]
    [InlineData("2026-11-05 07:00:00", "2026-02-30 07:00:00")]
    [InlineData("2026-11-05 07:00:00", "2026-11-05T00:00:00Z")]
    public void Signed_but_mismatched_or_malformed_payment_fields_are_rejected(string from, string to) => Assert.Null(Match(Body.Replace(from, to, StringComparison.Ordinal)));

    [Theory]
    [InlineData("[]")] [InlineData("null")] [InlineData("42")] [InlineData("{")]
    [InlineData("{\"id\":42,\"id\":43}")]
    public void Rejects_nonobject_invalid_or_duplicate_json(string body) => Assert.Null(Match(body));

    [Theory]
    [InlineData("\"content\":\"\\uDEAD\"")]
    [InlineData("\"\\uDEAD\":\"fixture\"")]
    public void Signed_escaped_lone_surrogates_in_value_or_property_name_fail_closed(string field)
    {
        var body = Body.Replace("\"content\":\"SEVQR012345ABCD\"", field, StringComparison.Ordinal);
        Assert.Null(Match(body));
    }

    [Theory]
    [InlineData("\"ID\":42")] [InlineData("\"id\":42")]
    [InlineData("\"extra\":{}")] [InlineData("\"extra\":[]")]
    [InlineData("\"referenceCode\":{}")] [InlineData("\"content\":true")]
    public void Rejects_duplicate_case_ambiguous_or_complex_fields(string field) => Assert.Null(Match(Body[..^1] + "," + field + "}"));

    [Fact]
    public void Rejects_missing_required_fields_and_invalid_utf8_even_with_valid_hmac()
    {
        foreach (var field in new[] { "\"id\":42,", "\"gateway\":\"FixtureBank\",", "\"accountNumber\":\"SYNTHETIC001\",", "\"code\":\"SEVQR012345ABCD\",", "\"transactionDate\":\"2026-11-05 07:00:00\",", "\"transferType\":\"in\",", "\"transferAmount\":25000," })
            Assert.Null(Match(Body.Replace(field, "", StringComparison.Ordinal)));
        var invalidUtf8 = Bytes(Body.Replace("FIXTURE-42", "X", StringComparison.Ordinal));
        invalidUtf8[Array.IndexOf(invalidUtf8, (byte)'X')] = 0xff;
        Assert.Null(Match(invalidUtf8));
    }

    [Fact]
    public void Body_limit_is_exactly_16_KiB_and_optional_reference_is_not_a_receipt()
    {
        var exact = Bytes(Body + new string(' ', 16384 - Bytes(Body).Length));
        Assert.NotNull(Match(exact));
        Assert.Null(Match(exact.Concat(new byte[] { 32 }).ToArray()));
        Assert.Null(Match(Array.Empty<byte>()));
        Assert.Null(Match(Body.Replace("\"id\":42", "\"Id\":42", StringComparison.Ordinal)));
        Assert.Null(Match(Body.Replace("\"referenceCode\":\"FIXTURE-42\"", "\"referenceCode\":false", StringComparison.Ordinal)));
        Assert.NotNull(Match(Body.Replace(",\"referenceCode\":\"FIXTURE-42\"", "", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("SEVQR012345ABCD", true)] [InlineData("SEVQR012345abcD", false)]
    [InlineData("SEVQR012345ABC", false)] [InlineData("SEVQR012345ABCDE", false)]
    [InlineData("OTHER012345ABCD", false)] [InlineData("SEVQR012345ABC-", false)]
    public void Reference_requires_fixed_5_character_prefix_and_10_uppercase_alphanumerics(string reference, bool valid) =>
        Assert.Equal(valid, SePayBankTransferProtocol.IsValid(Expected with { OrderReference = reference }));

    [Theory]
    [InlineData(999, false)] [InlineData(1000, true)] [InlineData(50000000, true)] [InlineData(50000001, false)]
    public void Expected_amount_is_bounded_whole_VND(long amount, bool valid) => Assert.Equal(valid, SePayBankTransferProtocol.IsValid(Expected with { Amount = amount }));

    [Fact]
    public void Invalid_expectations_fail_closed_for_both_matching_and_qr_construction()
    {
        Assert.False(SePayBankTransferProtocol.IsValid(null));
        Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body), Sign(Bytes(Body)), Timestamp, Secret, null, Now));
        foreach (var expected in new[] { Expected with { Bank = "" }, Expected with { Bank = "Fixture\r\nBank" }, Expected with { Bank = new string('B', 65) }, Expected with { Account = "" }, Expected with { Account = "SYNTHETIC/001" }, Expected with { Amount = 0 }, Expected with { OrderReference = "invalid" } })
        {
            Assert.False(SePayBankTransferProtocol.IsValid(expected));
            Assert.Null(SePayBankTransferProtocol.Match(Bytes(Body), Sign(Bytes(Body)), Timestamp, Secret, expected, Now));
            Assert.Throws<ArgumentException>(() => SePayBankTransferProtocol.BuildQrImageUri(expected));
        }
    }

    [Fact]
    public void Valid_utf8_unicode_and_documented_optional_fields_are_matched_without_culture_dependence()
    {
        var body = Body.Replace("\"content\":\"SEVQR012345ABCD\"", "\"content\":\"SEVQR012345ABCD chuyển tiền\"", StringComparison.Ordinal);
        body = body[..^1] + ",\"subAccount\":\"\",\"description\":\"\",\"accumulated\":0}";
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            Assert.NotNull(Match(body));
            Assert.Null(Match(body.Replace("\"accumulated\":0", "\"accumulated\":0.0", StringComparison.Ordinal)));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void Qr_uri_has_exact_provider_host_and_escaped_values_without_network_or_paid_state()
    {
        var uri = SePayBankTransferProtocol.BuildQrImageUri(Expected with { Bank = "Fixture &+Bank" });
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("vietqr.app", uri.Host);
        Assert.Equal("/img", uri.AbsolutePath);
        Assert.Equal("", uri.UserInfo);
        Assert.Equal("", uri.Fragment);
        Assert.Equal("https://vietqr.app/img?bank=Fixture%20%26%2BBank&acc=SYNTHETIC001&amount=25000&des=SEVQR012345ABCD", uri.AbsoluteUri);
    }
}
