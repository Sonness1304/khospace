namespace WarehouseRent.Api.Features.Quotes;
public sealed record UnitPrice(Guid UnitId, decimal AreaM2, decimal DailyRate, string BillingBasis = "Daily", decimal? MonthlyRate = null);
public sealed record PricedMonth(Guid UnitId, DateOnly StartDate, DateOnly EndDate, int Days, decimal AreaM2, decimal DailyRate, decimal Amount, string BillingBasis = "Daily", decimal? MonthlyRate = null, int? CalendarDays = null);
public sealed record QuotePrice(IReadOnlyList<PricedMonth> Lines, decimal RentalSubtotal, decimal RequiredDeposit);
public static class PriceCalculator
{
    private static bool ValidRate(decimal rate) => rate > 0 && rate <= 99999999999999.9999m && decimal.Round(rate, 4) == rate;
    public static QuotePrice Calculate(IReadOnlyList<UnitPrice> units, DateOnly start, DateOnly end)
    {
        if (end <= start || end.DayNumber - start.DayNumber > 365 || units.Count is 0 or > 20 || units.Select(x => x.UnitId).Distinct().Count() != units.Count
            || units.Any(x => x.UnitId == Guid.Empty || x.AreaM2 <= 0 || x.AreaM2 > 9999999999.99m || decimal.Round(x.AreaM2, 2) != x.AreaM2
                || !(x.BillingBasis == "Daily" && ValidRate(x.DailyRate) && x.MonthlyRate == null || x.BillingBasis == "CalendarMonth" && x.DailyRate == 0 && x.MonthlyRate is decimal monthly && ValidRate(monthly))))
            throw new ArgumentException("Invalid unit prices or rental interval.");
        var lines = new List<PricedMonth>();
        foreach (var unit in units.OrderBy(x => x.UnitId))
        {
            var cursor = start;
            while (cursor < end)
            {
                var boundary = cursor.Year == 9999 && cursor.Month == 12 ? end : new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1);
                var segmentEnd = boundary < end ? boundary : end;
                var days = segmentEnd.DayNumber - cursor.DayNumber;
                int? calendarDays = unit.BillingBasis == "CalendarMonth" ? DateTime.DaysInMonth(cursor.Year, cursor.Month) : null;
                // Multiply before dividing and round only the final unit/month segment.
                var exact = calendarDays.HasValue ? unit.AreaM2 * unit.MonthlyRate!.Value * days / calendarDays.Value : unit.AreaM2 * unit.DailyRate * days;
                var amount = decimal.Round(exact, 0, MidpointRounding.AwayFromZero);
                lines.Add(new(unit.UnitId, cursor, segmentEnd, days, unit.AreaM2, unit.DailyRate, amount, unit.BillingBasis, unit.MonthlyRate, calendarDays));
                cursor = segmentEnd;
            }
        }
        var subtotal = lines.Sum(x => x.Amount);
        if (subtotal > 999999999999999999m) throw new ArgumentException("Quote exceeds supported VND precision.");
        return new(lines, subtotal, decimal.Round(subtotal * .20m, 0, MidpointRounding.AwayFromZero));
    }
}
