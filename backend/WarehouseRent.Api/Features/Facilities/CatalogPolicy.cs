using System.Globalization;
namespace WarehouseRent.Api.Features.Facilities;

public static class CatalogPolicy
{
    public static DateOnly Today(DateTimeOffset utc) => DateOnly.FromDateTime(utc.ToOffset(TimeSpan.FromHours(7)).DateTime);
    public static bool Dates(DateOnly start, DateOnly end, DateOnly today) => start >= today && end > start && end.DayNumber - start.DayNumber <= 365;
    public static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
    public static bool Amount(string? value, int scale, decimal max, out decimal number)
    {
        number = 0;
        return value is { Length: > 0 and <= 32 } && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)
            && number > 0 && number <= max && decimal.Round(number, scale) == number;
    }
    public static string Money(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
