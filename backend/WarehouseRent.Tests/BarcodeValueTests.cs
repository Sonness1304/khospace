using WarehouseRent.Api.Features.Barcodes;
namespace WarehouseRent.Tests;
public sealed class BarcodeValueTests
{
    [Theory]
    [InlineData("96385074", "00000096385074")]
    [InlineData("036000291452", "00036000291452")]
    [InlineData("4006381333931", "04006381333931")]
    [InlineData("04006381333931", "04006381333931")]
    public void Gtin_preserves_leading_zeros_and_validates_check_digits(string input, string expected) => Assert.Equal(expected, BarcodeValue.Canonical("GTIN", input));
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("4006381333932")][InlineData("123456789")][InlineData(" 96385074")][InlineData("96385074\r")][InlineData("00000000")]
    public void Invalid_gtin_is_not_coerced(string? input) => Assert.Null(BarcodeValue.Canonical("GTIN", input));
    [Fact]
    public void Internal_is_explicit_ascii_uppercase() { Assert.Equal("WAREHOUSE/A-01.2_3", BarcodeValue.Canonical("Internal", "warehouse/a-01.2_3")); Assert.Null(BarcodeValue.Canonical("Internal", "one label")); Assert.Null(BarcodeValue.Canonical("Auto", "96385074")); Assert.Null(BarcodeValue.Canonical("Internal", new string('A',65))); }
}
