namespace WarehouseRent.Api.Features.Barcodes;
public static class BarcodeValue
{
    public static string? Canonical(string? kind, string? value)
    {
        if (value is null || value.Length is < 1 or > 64) return null;
        if (kind == "Internal")
            return char.IsAsciiLetterOrDigit(value[0]) && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '/' or '-') ? value.ToUpperInvariant() : null;
        if (kind != "GTIN" || value.Length is not (8 or 12 or 13 or 14) || value.Any(c => c is < '0' or > '9') || value.All(c => c == '0')) return null;
        var sum = 0; var weight = 3;
        for (var i = value.Length - 2; i >= 0; i--) { sum += (value[i] - '0') * weight; weight = weight == 3 ? 1 : 3; }
        return (10 - sum % 10) % 10 == value[^1] - '0' ? value.PadLeft(14, '0') : null;
    }
}
