using WarehouseRent.Api.Features.Quotes;
namespace WarehouseRent.Tests;
public sealed class PriceCalculatorTests
{
    [Theory]
    [InlineData("2028-01-30", "2028-02-03", 20, 5000, 400000, 80000, 2)]
    [InlineData("2028-01-31", "2028-02-02", 10.5, 101, 2122, 424, 2)]
    [InlineData("2028-02-01", "2028-02-03", 10.5, 101, 2121, 424, 1)]
    [InlineData("2028-02-28", "2028-03-02", 10.5, 101, 3182, 636, 2)]
    public void Prices_by_calendar_month_round_once_and_sum_exactly(string start, string end, decimal area, decimal rate, decimal total, decimal deposit, int months)
    {
        var quote = PriceCalculator.Calculate([new(Guid.NewGuid(), area, rate)], DateOnly.Parse(start), DateOnly.Parse(end));
        Assert.Equal(total, quote.RentalSubtotal); Assert.Equal(deposit, quote.RequiredDeposit);
        Assert.Equal(months, quote.Lines.Count); Assert.Equal(total, quote.Lines.Sum(x => x.Amount));
        Assert.Equal(DateOnly.Parse(end).DayNumber - DateOnly.Parse(start).DayNumber, quote.Lines.Sum(x => x.Days));
        Assert.Equal(DateOnly.Parse(start), quote.Lines[0].StartDate); Assert.Equal(DateOnly.Parse(end), quote.Lines[^1].EndDate);
    }
    [Fact]
    public void Leap_month_preserves_exact_monthly_lines_and_does_not_round_each_day()
    {
        var quote = PriceCalculator.Calculate([new(Guid.NewGuid(), 10.5m, 101m)], new(2028, 2, 28), new(2028, 3, 2));
        Assert.Equal(new decimal[] { 2121m, 1061m }, quote.Lines.Select(x => x.Amount));
        Assert.Equal(new[] { 2, 1 }, quote.Lines.Select(x => x.Days));
    }
    [Fact]
    public void Invalid_intervals_quantities_duplicate_units_and_out_of_range_prices_are_refused()
    {
        var unit = new UnitPrice(Guid.NewGuid(), 20, 5000); var start = new DateOnly(2028, 1, 1);
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit], start, start));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit], start, start.AddDays(366)));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit, unit], start, start.AddDays(1)));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit with { AreaM2 = 0 }], start, start.AddDays(1)));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit with { DailyRate = -1 }], start, start.AddDays(1)));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit with { AreaM2 = 1.234m }], start, start.AddDays(1)));
        Assert.Throws<ArgumentException>(() => PriceCalculator.Calculate([unit with { AreaM2 = 9999999999.99m, DailyRate = 99999999999999.9999m }], start, start.AddDays(365)));
    }
}
