using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class PostgresFoundationTests
{
    private const string Project = "aaaaaaaaaaaaaaaaaaaa";
    private const string Protected = "bbbbbbbbbbbbbbbbbbbb";
    private static readonly SupabaseProjectBinding Binding = new(Project, Protected, $"db.{Project}.supabase.co");
    private static string Connection => $"Host={Binding.ExpectedHost};Port=5432;Database=postgres;Username=warehouse;Password=synthetic;SSL Mode=VerifyFull";
    private static DbContextOptions<AppDbContext> Fixture() => PostgresPreviewConfiguration.ForSyntheticFixture(new(
        "Host=127.0.0.1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture;SSL Mode=Disable", "PostgresSchemaPreviewTests", true));

    [Fact]
    public void Preview_foundation_is_available_without_switching_sql_server_default()
    {
        using var sql = new AppDbContext(SqlConfiguration.Options("Server=(local);Database=Fixture;Integrated Security=true;TrustServerCertificate=true"));
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", sql.Database.ProviderName);
        Assert.NotNull(typeof(AppDbContext).Assembly.GetType("WarehouseRent.Infrastructure.Persistence.PostgresSchemaPreview"));
    }

    [Fact]
    public void Production_policy_requires_explicit_distinct_project_binding_and_verified_tls()
    {
        var cs = new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.ValidateSupabase(Connection, Binding));
        Assert.Equal(SslMode.VerifyFull, cs.SslMode);
        Assert.Equal("khospace", cs.SearchPath);
        Assert.Equal(5, cs.MaxPoolSize);
        Assert.Equal(0, cs.MinPoolSize);
        Assert.Equal(3, cs.Timeout);
        Assert.Equal(3, cs.CommandTimeout);
        Assert.False(cs.IncludeErrorDetail);
        Assert.False(cs.LogParameters);
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(Connection, Binding with { ProtectedProjectRef = Project }));
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(Connection, Binding with { ProtectedProjectRef = "" }));
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(Connection, Binding with { ExpectedHost = "other.pooler.supabase.com" }));
        var poolBinding = Binding with { ExpectedHost = "aws-0-ap-southeast-1.pooler.supabase.com" };
        var pool = $"Host={poolBinding.ExpectedHost};Port=5432;Database=postgres;Username=warehouse.{Project};Password=synthetic;SSL Mode=VerifyFull;Maximum Pool Size=2";
        Assert.Equal(2, new NpgsqlConnectionStringBuilder(PostgresConnectionPolicy.ValidateSupabase(pool, poolBinding)).MaxPoolSize);
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(pool.Replace(Project, Protected), poolBinding));
    }

    [Theory]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=Require")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=Disable")]
    [InlineData("Port=5432", "Port=6543")]
    [InlineData("Database=postgres", "Database=public")]
    [InlineData("Password=synthetic", "Password=")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;Search Path=public")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;Search Path=khospace,public")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;Options=-c search_path=public")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;Trust Server Certificate=true")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;Include Error Detail=true")]
    [InlineData("SSL Mode=VerifyFull", "SSL Mode=VerifyFull;SSL Negotiation=direct")]
    public void Production_policy_rejects_insecure_or_cross_boundary_settings(string from, string to) =>
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(Connection.Replace(from, to), Binding));

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("db.aaaaaaaaaaaaaaaaaaaa.supabase.co.attacker.test")]
    [InlineData("aws-0-ap-southeast-1.pooler.supabase.com.attacker.test")]
    [InlineData("db.bbbbbbbbbbbbbbbbbbbb.supabase.co")]
    public void Production_policy_rejects_loopback_spoofs_and_protected_project(string host) =>
        Assert.Throws<InvalidOperationException>(() => PostgresConnectionPolicy.ValidateSupabase(Connection.Replace(Binding.ExpectedHost, host), Binding with { ExpectedHost = host }));

    [Fact]
    public void Synthetic_options_are_explicit_and_separate_from_production()
    {
        const string cs = "Host=127.0.0.1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture;SSL Mode=Disable";
        Assert.Throws<InvalidOperationException>(() => PostgresPreviewConfiguration.ForSyntheticFixture(new(cs, "Production", true)));
        Assert.Throws<InvalidOperationException>(() => PostgresPreviewConfiguration.ForSyntheticFixture(new(cs, "PostgresSchemaPreviewTests", false)));
        Assert.Throws<InvalidOperationException>(() => PostgresPreviewConfiguration.ForSyntheticFixture(new(cs.Replace("127.0.0.1", "remote.example"), "PostgresSchemaPreviewTests", true)));
    }

    [Fact]
    public void Offline_model_translates_all_mappings_without_polluting_sql_model_cache()
    {
        using var pg = new AppDbContext(Fixture());
        using var sql = new AppDbContext(SqlConfiguration.Options("Server=(local);Database=Fixture;Integrated Security=true;TrustServerCertificate=true"));
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", pg.Database.ProviderName);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", sql.Database.ProviderName);
        Assert.Equal("khospace", pg.Model.GetDefaultSchema());
        Assert.Null(sql.Model.GetDefaultSchema());
        var pgModel = pg.GetService<IDesignTimeModel>().Model;
        var sqlModel = sql.GetService<IDesignTimeModel>().Model;
        Assert.Equal(sqlModel.GetEntityTypes().Count(), pgModel.GetEntityTypes().Count());
        foreach (var entity in pgModel.GetEntityTypes())
        {
            var original = sqlModel.FindEntityType(entity.Name)!;
            Assert.Equal(original.GetCheckConstraints().Count(), entity.GetCheckConstraints().Count(c => !c.ModelName.EndsWith("_8", StringComparison.Ordinal)));
            // Regex character classes inside SQL string literals are not SQL Server identifiers.
            Assert.All(entity.GetCheckConstraints(), c => Assert.DoesNotContain("[",
                System.Text.RegularExpressions.Regex.Replace(c.Sql, "'(?:[^']|'')*'", "")));
            Assert.All(entity.GetIndexes().Where(i => i.GetFilter() != null), i => Assert.DoesNotContain("[", i.GetFilter()!));
            foreach (var token in entity.GetProperties().Where(p => p.ClrType == typeof(byte[]) && (p.Name == "Version" || p.GetMaxLength() == 8)))
            {
                Assert.Equal("bytea", token.GetColumnType());
                Assert.Equal(8, token.GetMaxLength());
                Assert.Contains(entity.GetCheckConstraints(), c => c.Sql == $"octet_length(\"{token.Name}\") = 8");
                if (token.Name == "Version") Assert.True(token.IsConcurrencyToken);
            }
        }
        var query = pg.Facilities.Where(f => f.Code == "SYNTHETIC").ToQueryString();
        Assert.Contains("khospace.\"Facilities\"", query);
        Assert.Contains("[Facilities]", sql.Facilities.ToQueryString());
        Assert.Contains(pgModel.GetEntityTypes().SelectMany(e => e.GetIndexes()), i => i.GetFilter() == "\"StockRequestId\" IS NOT NULL AND \"IsFullRequest\" = TRUE");
        Assert.Contains(sqlModel.GetEntityTypes().SelectMany(e => e.GetIndexes()), i => i.GetFilter() == "[StockRequestId] IS NOT NULL AND [IsFullRequest] = 1");
    }

    [Fact]
    public void Generated_script_is_explicitly_incomplete_and_contains_proposed_eight_byte_tokens()
    {
        var script = PostgresSchemaPreview.GenerateScript();
        Assert.Contains("NOT MIGRATION READY", script);
        Assert.NotEmpty(PostgresSchemaPreview.MissingRuntimeGuards);
        Assert.Contains("CREATE SEQUENCE khospace.version_tokens", script);
        Assert.Contains("int8send(nextval", script);
        Assert.Contains("BEFORE INSERT OR UPDATE", script);
        Assert.Contains("octet_length(\"Content\")", script);
        Assert.Contains("char_length(rtrim(\"CanonicalValue\", ' '))", script);
        Assert.Contains("floor(extract(epoch", script);
        Assert.Contains("COLLATE \"C\"", script);
        Assert.DoesNotContain("DATEDIFF", script);
        Assert.DoesNotContain("DATEPART", script);
        Assert.DoesNotContain("Latin1_General", script);
        Assert.DoesNotContain("rowversion", script, StringComparison.OrdinalIgnoreCase);
        // Optional local evidence export; path is explicit and never triggers database I/O.
        if (Environment.GetEnvironmentVariable("WAREHOUSERENT_POSTGRES_PREVIEW_OUTPUT") is { Length: > 0 } path)
            File.WriteAllText(Path.GetFullPath(path), script);
    }

    [Fact]
    public async Task Preview_cannot_open_a_database_or_discover_historical_sql_migrations()
    {
        using var pg = new AppDbContext(Fixture());
        Assert.Empty(pg.Database.GetMigrations());
        Assert.Throws<InvalidOperationException>(() => pg.Database.OpenConnection());
        await Assert.ThrowsAsync<InvalidOperationException>(() => pg.Database.OpenConnectionAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => pg.Database.MigrateAsync());
    }
}
