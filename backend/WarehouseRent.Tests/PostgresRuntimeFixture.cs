using Microsoft.EntityFrameworkCore;
using Npgsql;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

// Only an agent-owned loopback PostgreSQL cluster and generated synthetic catalogs are allowed.
internal sealed class PostgresRuntimeFixture : IAsyncDisposable
{
    private const string Template = "Host=127.0.0.1;Port=55439;Username=warehouse_fixture;Database=postgres;SSL Mode=Disable;Pooling=false;Timeout=3;Command Timeout=3";
    private readonly string name = "warehouse_fixture_" + Guid.NewGuid().ToString("N");
    public string Connection => new NpgsqlConnectionStringBuilder(Template) { Database = name }.ConnectionString;
    public DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection, o => o.CommandTimeout(3))
        .ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly,FixturePostgresMigrationsAssembly>()
        .ReplaceService<Microsoft.EntityFrameworkCore.Migrations.IMigrator,FixturePostgresMigrator>().Options;
    public AppDbContext Context() => new(Options());

    public static async Task<PostgresRuntimeFixture> Create(bool stockOnly = false, bool empty = false)
    {
        var fixture = new PostgresRuntimeFixture();
        await using var admin = new NpgsqlConnection(Template); await admin.OpenAsync();
        try
        {
            // Cleanup also covers an uncertain CREATE result; the target is this fixture's exact generated catalog.
            // Catalog creation is bounded DDL, not the three-second application query budget.
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{fixture.name}\"", admin) { CommandTimeout = 30 }) await create.ExecuteNonQueryAsync();
            if (empty) return fixture;
            await using var db = new NpgsqlConnection(fixture.Connection); await db.OpenAsync();
            var ddl = stockOnly ? """
                CREATE SCHEMA khospace;
                CREATE SEQUENCE khospace.version_tokens;
                CREATE TABLE khospace."StockBalances" (
                    "OrganizationId" uuid NOT NULL, "ItemId" uuid NOT NULL, "LeaseUnitId" uuid NOT NULL,
                    "Quantity" numeric(18,3) NOT NULL,
                    "Version" bytea NOT NULL DEFAULT int8send(nextval('khospace.version_tokens')),
                    PRIMARY KEY ("OrganizationId","ItemId","LeaseUnitId"));
                CREATE FUNCTION khospace.set_version_token() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN NEW."Version" := int8send(nextval('khospace.version_tokens')); RETURN NEW; END $$;
                CREATE TRIGGER version_fixture BEFORE INSERT OR UPDATE ON khospace."StockBalances"
                FOR EACH ROW EXECUTE FUNCTION khospace.set_version_token();
                """ : PostgresSchemaPreview.GenerateScript();
            await using var command = new NpgsqlCommand(ddl, db) { CommandTimeout = 30 };
            await command.ExecuteNonQueryAsync(); return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^warehouse_fixture_[a-f0-9]{32}$")) throw new InvalidOperationException();
        await using var admin = new NpgsqlConnection(Template); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin) { CommandTimeout = 30 };
        await drop.ExecuteNonQueryAsync();
    }
}

// Fixtures also refuse SQL Server migration discovery/commands; raw Npgsql DDL is owned by PostgresMigrations.
internal sealed class FixturePostgresMigrationsAssembly : Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly
{
    public IReadOnlyDictionary<string,System.Reflection.TypeInfo> Migrations {get;}=new Dictionary<string,System.Reflection.TypeInfo>();
    public Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot? ModelSnapshot=>null;
    public System.Reflection.Assembly Assembly=>typeof(FixturePostgresMigrationsAssembly).Assembly;
    public string? FindMigrationId(string nameOrId)=>null;
    public Microsoft.EntityFrameworkCore.Migrations.Migration CreateMigration(System.Reflection.TypeInfo migrationClass,string activeProvider)=>throw new InvalidOperationException("SQL Server migrations are forbidden in PG fixtures.");
}
internal sealed class FixturePostgresMigrator : Microsoft.EntityFrameworkCore.Migrations.IMigrator
{
    public void Migrate(string? targetMigration=null)=>throw new InvalidOperationException("SQL Server migration/downgrade is forbidden in PG fixtures.");
    public Task MigrateAsync(string? targetMigration=null,CancellationToken cancellationToken=default)=>throw new InvalidOperationException("SQL Server migration/downgrade is forbidden in PG fixtures.");
    public string GenerateScript(string? fromMigration=null,string? toMigration=null,Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions options=Microsoft.EntityFrameworkCore.Migrations.MigrationsSqlGenerationOptions.Default)=>throw new InvalidOperationException("SQL Server scripts are forbidden in PG fixtures.");
    public bool HasPendingModelChanges()=>throw new InvalidOperationException("Use explicit PostgreSQL migrations.");
}
