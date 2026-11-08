using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace WarehouseRent.Infrastructure.Persistence;

public static class DemoSeedPolicy
{
    public static void Validate(string environment, string connectionString)
    {
        if (!string.Equals(environment, "Demo", StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic seed requires the explicit Demo environment.");
        ValidateTarget(connectionString, demoOnly: true);
    }

    public static void ValidateMigration(string environment, string connectionString)
    {
        if (environment is not ("Demo" or "Development" or "Testing"))
            throw new InvalidOperationException("Local schema setup is unavailable in this environment.");
        ValidateTarget(connectionString, demoOnly: false);
    }

    private static void ValidateTarget(string connectionString, bool demoOnly)
    {
        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var pattern = demoOnly ? @"^WarehouseRent_Demo(?:_[A-Za-z0-9]+)*$" : @"^WarehouseRent_(?:Demo|Test)(?:_[A-Za-z0-9]+)*$";
        if (!Regex.IsMatch(database, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Schema setup requires an explicitly named WarehouseRent Demo/Test database.");
    }
}
