using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WarehouseRent.Infrastructure.Persistence;

/// <summary>Offline model/query/DDL inspection only. Never register these options in the API host.</summary>
public static class PostgresPreviewConfiguration
{
    public static DbContextOptions<AppDbContext> ForSupabase(string connectionString, SupabaseProjectBinding binding) =>
        Create(PostgresConnectionPolicy.ValidateSupabase(connectionString, binding));

    public static DbContextOptions<AppDbContext> ForSyntheticFixture(PostgresPreviewFixtureOptions options) =>
        Create(PostgresConnectionPolicy.SyntheticFixture(options));

    private static DbContextOptions<AppDbContext> Create(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, pg => pg.CommandTimeout(3))
            .ReplaceService<IMigrationsAssembly, PreviewMigrationsAssembly>()
            .AddInterceptors(OfflineOnlyConnectionInterceptor.Instance).Options;

    private sealed class OfflineOnlyConnectionInterceptor : DbConnectionInterceptor
    {
        internal static readonly OfflineOnlyConnectionInterceptor Instance = new();
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new InvalidOperationException("PostgreSQL foundation is offline preview only; runtime guards and migration parity are incomplete.");
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("PostgreSQL foundation is offline preview only; runtime guards and migration parity are incomplete.");
    }
}

// Never discover or apply the historical SQL Server migrations against PostgreSQL.
internal sealed class PreviewMigrationsAssembly : IMigrationsAssembly
{
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();
    public ModelSnapshot? ModelSnapshot => null;
    public Assembly Assembly => typeof(PreviewMigrationsAssembly).Assembly;
    public string? FindMigrationId(string nameOrId) => null;
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
        throw new InvalidOperationException("PostgreSQL preview cannot execute SQL Server migrations.");
}
