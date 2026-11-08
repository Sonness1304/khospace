using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class PostgresBootstrapTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string,string?> {
            ["Database:Provider"]="PostgreSQL", ["CloudHosting:Enabled"]="true",
            ["CloudHosting:DataProtectionStore"]="PostgreSQL", ["Database:SupabaseProjectRef"]="jhemdameyrcxqxsffugw",
            ["Database:ProtectedProjectRef"]="uwjglyucmyrjxzzryhth", ["Database:ExpectedHost"]="db.jhemdameyrcxqxsffugw.supabase.co",
            ["ConnectionStrings:WarehouseRent"]="Host=db.jhemdameyrcxqxsffugw.supabase.co;Database=postgres;Username=khospace_api;Password=synthetic;SSL Mode=VerifyFull",
        };
        foreach(var pair in overrides)values[pair.Key]=pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public async Task Explicit_postgres_selection_is_bound_and_ef_cannot_apply_sql_server_migrations()
    {
        using var db = new AppDbContext(DatabaseProviderConfiguration.Options(Config()));
        Assert.True(db.Database.IsNpgsql());
        Assert.Empty(db.Database.GetMigrations());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.Database.MigrateAsync());
        Assert.Throws<InvalidOperationException>(()=>db.Database.Migrate());
    }

    [Theory]
    [InlineData("Database:Provider","PostgresTypo")]
    [InlineData("CloudHosting:Enabled","false")]
    [InlineData("CloudHosting:DataProtectionStore","FileSystem")]
    [InlineData("Database:SupabaseProjectRef","uwjglyucmyrjxzzryhth")]
    [InlineData("Database:ProtectedProjectRef","bbbbbbbbbbbbbbbbbbbb")]
    [InlineData("Database:ExpectedHost","db.uwjglyucmyrjxzzryhth.supabase.co")]
    [InlineData("ConnectionStrings:WarehouseRent","Host=db.jhemdameyrcxqxsffugw.supabase.co;Database=postgres;Username=postgres;Password=synthetic;SSL Mode=VerifyFull")]
    public void Invalid_selection_never_falls_back_to_another_provider(string key,string value)
        => Assert.Throws<InvalidOperationException>(()=>DatabaseProviderConfiguration.Options(Config((key,value))));

    [Fact]
    public void Default_provider_remains_sql_server()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:WarehouseRent"]="Server=localhost;Database=Synthetic;Integrated Security=true;TrustServerCertificate=true" }).Build();
        using var db=new AppDbContext(DatabaseProviderConfiguration.Options(configuration));
        Assert.True(db.Database.IsSqlServer());
    }

    [Fact]
    public async Task Transitive_browser_membership_is_refused_but_trusted_creator_administration_is_allowed()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var owner=fixture.Context();await PostgresMigrations.ApplyAsync(owner,default);
        var role="warehouse_fixture_role_"+Guid.NewGuid().ToString("N");
        var group="warehouse_fixture_group_"+Guid.NewGuid().ToString("N");
        var browser="warehouse_fixture_browser_"+Guid.NewGuid().ToString("N");
        try {
            await PostgresRuntimePrivileges.ProvisionAsync(owner,role,default);
            var trusted="GRANT \""+role+"\" TO warehouse_fixture WITH ADMIN OPTION";
            await owner.Database.ExecuteSqlRawAsync(trusted);
            await using var runtime=fixture.Context();await runtime.Database.OpenConnectionAsync();
            var selectRole="SET ROLE \""+role+"\"";await runtime.Database.ExecuteSqlRawAsync(selectRole);
            Assert.True(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(runtime,default));
            await PostgresRuntimePrivileges.ProvisionAsync(owner,role,default);
            var unsafeMembership="CREATE ROLE \""+group+"\" NOLOGIN; CREATE ROLE \""+browser+"\" NOLOGIN; GRANT \""+role+"\" TO \""+group+"\"; GRANT \""+group+"\" TO \""+browser+"\"";
            await owner.Database.ExecuteSqlRawAsync(unsafeMembership);
            Assert.True(await owner.Database.SqlQuery<bool>($"SELECT pg_has_role({browser},{role},'MEMBER') AS \"Value\"").SingleAsync());
            Assert.False(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(runtime,default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>PostgresRuntimePrivileges.ProvisionAsync(owner,role,default));
            await runtime.Database.ExecuteSqlRawAsync("RESET ROLE");
        } finally {
            var cleanup="DROP OWNED BY \""+role+"\"; DROP ROLE IF EXISTS \""+browser+"\",\""+group+"\",\""+role+"\"";
            await owner.Database.ExecuteSqlRawAsync(cleanup);
        }
    }

    [Fact]
    public async Task Concurrent_migrators_create_one_history_row_and_runtime_role_cannot_mutate_ddl_or_history()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var first=fixture.Context();await using var second=fixture.Context();
        await Task.WhenAll(PostgresMigrations.ApplyAsync(first,default),PostgresMigrations.ApplyAsync(second,default));
        Assert.True(await PostgresMigrations.IsReadyAsync(first,default));
        var role="warehouse_fixture_role_"+Guid.NewGuid().ToString("N");
        var browser="warehouse_fixture_browser_"+Guid.NewGuid().ToString("N");
        try {
            await PostgresRuntimePrivileges.ProvisionAsync(first,role,default);
            await using var db=fixture.Context();
            await db.Database.OpenConnectionAsync();
            var setRole="SET ROLE \""+role+"\"";
            await db.Database.ExecuteSqlRawAsync(setRole);
            Assert.Equal(2,await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
            await Assert.ThrowsAnyAsync<Exception>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM khospace.\"MigrationHistory\""));
            await Assert.ThrowsAnyAsync<Exception>(()=>db.Database.ExecuteSqlRawAsync("CREATE TABLE khospace.forbidden (id integer)"));
            await Assert.ThrowsAnyAsync<Exception>(()=>db.Database.ExecuteSqlRawAsync("TRUNCATE khospace.\"Facilities\" CASCADE"));
            Assert.True(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,default));
            var inherit="CREATE ROLE \""+browser+"\" NOLOGIN; GRANT \""+role+"\" TO \""+browser+"\"";
            await first.Database.ExecuteSqlRawAsync(inherit);
            Assert.False(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,default));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>PostgresRuntimePrivileges.ProvisionAsync(first,role,default));
            await db.Database.ExecuteSqlRawAsync("RESET ROLE");
        } finally {
            var cleanup="DROP OWNED BY \""+role+"\"; DROP ROLE \""+role+"\"; DROP ROLE IF EXISTS \""+browser+"\"";
            await first.Database.ExecuteSqlRawAsync(cleanup);
        }
    }
}
