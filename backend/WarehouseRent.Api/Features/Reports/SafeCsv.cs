using System.Globalization;
namespace WarehouseRent.Api.Features.Reports;
public static class SafeCsv
{
    public static string Text(string? value)
    {
        value ??= ""; var trimmed = value.TrimStart();
        if ((trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) || value.Any(c => c is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
