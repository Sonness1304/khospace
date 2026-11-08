using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api;
public static class BuildIdentity
{
    public static object Read(IConfiguration? configuration=null)
    {
        var version = typeof(BuildIdentity).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var candidate = version.Split('+').Last();
        var revision = candidate.Length == 40 && candidate.All(Uri.IsHexDigit) ? candidate.ToLowerInvariant() : null;
        var latest = typeof(AppDbContext).Assembly.GetTypes().Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Id)
            .Where(x => x is not null).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault();
        var postgres=configuration is not null && DatabaseProviderConfiguration.IsPostgres(configuration);
        return new { application = "WarehouseRent", informationalVersion = version, sourceRevision = revision,
            databaseProvider=postgres?"PostgreSQL":"SqlServer", migrationChecksum=postgres?PostgresMigrations.LatestChecksum:null,
            sourceRevisionKnown = revision is not null, latestExpectedMigration = postgres?PostgresMigrations.Latest:latest, runtimeAcceptanceVerified = false };
    }
}
