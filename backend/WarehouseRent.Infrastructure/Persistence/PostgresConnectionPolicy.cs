using System.Text.RegularExpressions;
using Npgsql;

namespace WarehouseRent.Infrastructure.Persistence;

/// <summary>Explicit owner-supplied project boundary; never inferred from another application's config.</summary>
public sealed record SupabaseProjectBinding(string ProjectRef, string ProtectedProjectRef, string ExpectedHost);

public static class PostgresConnectionPolicy
{
    public const string ApplicationSchema = "khospace";

    // This is validation, not a production provider switch or permission to create a remote project.
    public static string ValidateSupabase(string connectionString, SupabaseProjectBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!IsProjectRef(binding.ProjectRef) || !IsProjectRef(binding.ProtectedProjectRef) ||
            binding.ProjectRef == binding.ProtectedProjectRef)
            throw new InvalidOperationException("Explicit distinct WarehouseRent and protected project bindings are required.");
        var cs = Parse(connectionString);
        var host = cs.Host ?? "";
        var direct = host == $"db.{binding.ProjectRef}.supabase.co";
        var sessionPooler = Regex.IsMatch(host, @"^aws-[0-9]+-[a-z0-9-]+\.pooler\.supabase\.com$", RegexOptions.CultureInvariant);
        if (host != binding.ExpectedHost || (!direct && !sessionPooler) || cs.Port != 5432 ||
            cs.Database != "postgres" || string.IsNullOrWhiteSpace(cs.Password) ||
            (sessionPooler && !Regex.IsMatch(cs.Username ?? "", @"^[a-z_][a-z0-9_]*\." + Regex.Escape(binding.ProjectRef) + "$", RegexOptions.CultureInvariant)) ||
            (direct && !Regex.IsMatch(cs.Username ?? "", @"^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant)))
            throw new InvalidOperationException("Only the explicitly bound Supabase direct/session endpoint on port 5432 is permitted.");
        if (cs.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Supabase requires TLS VerifyFull.");
        return Bound(cs);
    }

    internal static string SyntheticFixture(PostgresPreviewFixtureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.AllowSyntheticLoopback || options.EnvironmentName != "PostgresSchemaPreviewTests")
            throw new InvalidOperationException("Synthetic loopback is restricted to explicit schema-preview test options.");
        var cs = Parse(options.ConnectionString);
        if (cs.Host is not ("127.0.0.1" or "::1" or "localhost") || cs.Database != "warehouse_rent_synthetic")
            throw new InvalidOperationException("The schema-preview fixture must use a synthetic loopback database.");
        return Bound(cs);
    }

    private static NpgsqlConnectionStringBuilder Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("PostgreSQL connection is not configured.");
        NpgsqlConnectionStringBuilder cs;
        try { cs = new NpgsqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid PostgreSQL connection settings."); }
        // Do not allow libpq options/search-path injection, certificate bypasses, proxies or diagnostic secrets.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Host", "Port", "Database", "Username", "Password", "SSL Mode", "Root Certificate",
            "Maximum Pool Size", "Minimum Pool Size", "Pooling", "Timeout", "Command Timeout", "Search Path",
            "Include Error Detail", "Log Parameters"
        };
        foreach (string key in cs.Keys)
            if (cs.ContainsKey(key) && !allowed.Contains(key))
                throw new InvalidOperationException("Unsupported PostgreSQL connection setting.");
        if (cs.IncludeErrorDetail || cs.LogParameters)
            throw new InvalidOperationException("PostgreSQL diagnostic secrets must not be logged.");
        if (!string.IsNullOrEmpty(cs.SearchPath) && cs.SearchPath != ApplicationSchema)
            throw new InvalidOperationException("Only the private application schema is permitted; public is forbidden.");
        return cs;
    }

    private static string Bound(NpgsqlConnectionStringBuilder cs)
    {
        cs.SearchPath = ApplicationSchema;
        cs.Pooling = true;
        cs.MinPoolSize = 0;
        cs.MaxPoolSize = Math.Min(cs.MaxPoolSize, 5);
        cs.Timeout = 3;
        cs.CommandTimeout = 3;
        cs.IncludeErrorDetail = false;
        cs.LogParameters = false;
        return cs.ConnectionString;
    }

    private static bool IsProjectRef(string value) =>
        value is not null && Regex.IsMatch(value, "^[a-z0-9]{20}$", RegexOptions.CultureInvariant);
}

public sealed record PostgresPreviewFixtureOptions(string ConnectionString, string EnvironmentName, bool AllowSyntheticLoopback);
