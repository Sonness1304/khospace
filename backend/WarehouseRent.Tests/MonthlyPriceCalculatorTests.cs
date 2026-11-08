using WarehouseRent.Api.Features.Quotes;
namespace WarehouseRent.Tests;
public sealed class MonthlyPriceCalculatorTests
{
    [Theory]
    [InlineData("2027-02-01","2027-03-01",31000,28)]
    [InlineData("2028-02-01","2028-03-01",31000,29)]
    [InlineData("2028-04-01","2028-05-01",31000,30)]
    [InlineData("2028-01-01","2028-02-01",31000,31)]
    [InlineData("2028-02-28","2028-03-01",2138,29)]
    public void Actual_calendar_days_prorate_once(string start,string end,decimal total,int denominator)
    {
        var price=PriceCalculator.Calculate([new(Guid.NewGuid(),10,0,"CalendarMonth",3100)],DateOnly.Parse(start),DateOnly.Parse(end));
        Assert.Equal(total,price.RentalSubtotal);Assert.Equal(denominator,Assert.Single(price.Lines).CalendarDays);
        Assert.Equal(decimal.Round(total*.2m,0,MidpointRounding.AwayFromZero),price.RequiredDeposit);
    }
    [Fact]
    public void Mixed_year_boundary_rounding_and_invalid_basis_are_explicit()
    {
        var unit=new UnitPrice(Guid.NewGuid(),1,0,"CalendarMonth",15.5m);
        var price=PriceCalculator.Calculate([unit,new(Guid.NewGuid(),1,1)],new(2028,12,31),new(2029,1,2));
        Assert.Equal(4,price.RentalSubtotal);Assert.Equal(1,price.RequiredDeposit);Assert.Equal(4,price.Lines.Count);
        Assert.Throws<ArgumentException>(()=>PriceCalculator.Calculate([unit with {DailyRate=1}],new(2028,1,1),new(2028,2,1)));
        Assert.Throws<ArgumentException>(()=>PriceCalculator.Calculate([unit with {MonthlyRate=0}],new(2028,1,1),new(2028,2,1)));
        Assert.Throws<ArgumentException>(()=>PriceCalculator.Calculate([unit with {MonthlyRate=1.00001m}],new(2028,1,1),new(2028,2,1)));
        Assert.Throws<ArgumentException>(()=>PriceCalculator.Calculate([unit with {BillingBasis="Monthly"}],new(2028,1,1),new(2028,2,1)));
        Assert.Throws<ArgumentException>(()=>PriceCalculator.Calculate([unit with {AreaM2=9999999999.99m,MonthlyRate=99999999999999.9999m}],new(2028,1,1),new(2028,2,1)));
    }
}
