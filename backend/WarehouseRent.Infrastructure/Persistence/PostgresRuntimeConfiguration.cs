using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WarehouseRent.Infrastructure.Persistence;

public static class PostgresRuntimeConfiguration
{
    public static DbContextOptions<AppDbContext> ForSupabase(string connection, SupabaseProjectBinding binding) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresConnectionPolicy.ValidateSupabase(connection,binding), options=>options.CommandTimeout(3))
            .ReplaceService<IMigrationsAssembly,PreviewMigrationsAssembly>()
            .ReplaceService<IMigrator,PostgresEfMigrationBlocker>().Options;
}

// PostgreSQL uses its own frozen, checksummed catalog; EF SQL Server history must not be touched.
internal sealed class PostgresEfMigrationBlocker : IMigrator
{
    private static InvalidOperationException Refused()=>new("Use explicit PostgreSQL initialization, not EF SQL Server migrations.");
    public void Migrate(string? targetMigration=null)=>throw Refused();
    public Task MigrateAsync(string? targetMigration=null,CancellationToken cancellationToken=default)=>throw Refused();
    public string GenerateScript(string? fromMigration=null,string? toMigration=null,MigrationsSqlGenerationOptions options=MigrationsSqlGenerationOptions.Default)=>throw Refused();
    public bool HasPendingModelChanges()=>throw Refused();
}
