using WarehouseRent.Api.Features.Reports;
namespace WarehouseRent.Tests;
public sealed class SafeCsvTests
{
    [Theory]
    [InlineData("=SUM(A1)")][InlineData("  +cmd")][InlineData("-1+2")][InlineData("@data")][InlineData("\tvalue")][InlineData("line\nvalue")]
    public void Formula_candidates_are_quoted_and_prefixed(string value) => Assert.StartsWith("\"'", SafeCsv.Text(value));
    [Fact]
    public void Quotes_and_numbers_are_invariant() { Assert.Equal("\"one,\"\"two\"\"\"", SafeCsv.Text("one,\"two\"")); Assert.Equal("-1.25", SafeCsv.Number(-1.25m)); }
}
