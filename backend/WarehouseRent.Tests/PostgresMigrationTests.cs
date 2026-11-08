using Microsoft.EntityFrameworkCore;
using Npgsql;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class PostgresMigrationTests
{
    [Fact]
    public async Task Empty_catalog_migrates_atomically_repeat_is_read_only_and_drift_is_refused()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        Assert.False(await PostgresMigrations.IsReadyAsync(db, default));
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.True(await PostgresMigrations.IsReadyAsync(db, default));
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.Facilities.ToListAsync());
        Assert.Empty(await db.FoundationMarkers.ToListAsync());
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE khospace.\"Facilities\" DROP CONSTRAINT \"CK_Facilities_Version_8\"");
        Assert.False(await PostgresMigrations.IsReadyAsync(db, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresMigrations.ApplyAsync(db, default));
    }

    [Fact]
    public async Task Existing_unversioned_objects_are_not_adopted_or_overwritten()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA khospace; CREATE TABLE khospace.owner_data (value text); INSERT INTO khospace.owner_data VALUES ('preserve')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresMigrations.ApplyAsync(db, default));
        Assert.Equal("preserve", await db.Database.SqlQuery<string>($"SELECT value AS \"Value\" FROM khospace.owner_data").SingleAsync());
    }

    [Fact]
    public async Task Migration_failure_leaves_no_history_or_partial_schema_and_can_be_retried_explicitly()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PostgresMigrations.ApplyAsync(db, cancellation.Token));
        Assert.False(await PostgresMigrations.IsReadyAsync(db, default));
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.True(await PostgresMigrations.IsReadyAsync(db, default));
    }

    [Fact]
    public void Migration_is_frozen_private_and_cannot_import_sql_server_history()
    {
        Assert.Equal(64, PostgresMigrations.Checksum.Length);
        Assert.Contains("CREATE TABLE khospace.\"DataProtectionKeys\"", PostgresMigrations.Script);
        Assert.DoesNotContain("dbo.", PostgresMigrations.Script);
        Assert.DoesNotContain("__EFMigrationsHistory", PostgresMigrations.Script);
        Assert.Contains("REVOKE ALL ON SCHEMA khospace FROM PUBLIC", PostgresMigrations.Script);
    }

    [Fact]
    public async Task Failure_after_partial_ddl_rolls_back_every_application_object_and_allows_explicit_retry()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var db=fixture.Context();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.fixture_reject_leases() RETURNS event_trigger LANGUAGE plpgsql AS $$
            BEGIN IF EXISTS (SELECT 1 FROM pg_event_trigger_ddl_commands() WHERE object_identity='khospace."Leases"')
            THEN RAISE EXCEPTION 'Synthetic migration failure'; END IF; END $$;
            CREATE EVENT TRIGGER fixture_reject_leases ON ddl_command_end EXECUTE FUNCTION public.fixture_reject_leases();
            """);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>PostgresMigrations.ApplyAsync(db,default));
        Assert.Equal("P0001",error.SqlState);
        Assert.False(await db.Database.SqlQuery<bool>($"SELECT (to_regnamespace('khospace') IS NOT NULL) AS \"Value\"").SingleAsync());
        await db.Database.ExecuteSqlRawAsync("DROP EVENT TRIGGER fixture_reject_leases; DROP FUNCTION public.fixture_reject_leases()");
        await PostgresMigrations.ApplyAsync(db,default);
        Assert.True(await PostgresMigrations.IsReadyAsync(db,default));
    }

    [Theory]
    [InlineData("UPDATE khospace.\"MigrationHistory\" SET \"Checksum\"=repeat('0',64)")]
    [InlineData("UPDATE khospace.\"MigrationHistory\" SET \"Id\"='999_unknown' WHERE \"Id\"='001_initial'")]
    [InlineData("ALTER TABLE khospace.\"Facilities\" DISABLE ROW LEVEL SECURITY")]
    [InlineData("ALTER TABLE khospace.\"Facilities\" DISABLE TRIGGER \"TR_Version\"")]
    [InlineData("CREATE POLICY unsafe_public ON khospace.\"Facilities\" FOR SELECT TO PUBLIC USING (true)")]
    [InlineData("ALTER SEQUENCE khospace.version_tokens MAXVALUE 100 CYCLE")]
    [InlineData("GRANT SELECT ON khospace.\"Facilities\" TO PUBLIC")]
    [InlineData("GRANT USAGE ON SCHEMA khospace TO PUBLIC")]
    public async Task Tampered_history_or_disabled_guards_refuse_readiness_and_reinitialization(string change)
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var db=fixture.Context();await PostgresMigrations.ApplyAsync(db,default);
        await db.Database.ExecuteSqlRawAsync(change);
        Assert.False(await PostgresMigrations.IsReadyAsync(db,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PostgresMigrations.ApplyAsync(db,default));
    }

    // Fixture role identifiers are GUID-generated and regex-allowlisted, never user input.
#pragma warning disable EF1003
    private static async Task CreateVersionOne(AppDbContext db, string? runtimeRole = null)
    {
        await db.Database.ExecuteSqlRawAsync(PostgresMigrations.Script);
        if (runtimeRole is not null)
        {
            Assert.Matches("^warehouse_fixture_role_[a-f0-9]{32}$", runtimeRole);
            var role = '"' + runtimeRole + '"';
            await db.Database.ExecuteSqlRawAsync("CREATE ROLE " + role +
                " NOLOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; " +
                "GRANT USAGE ON SCHEMA khospace TO " + role + "; " +
                "GRANT SELECT ON khospace.\"PaymentIntents\" TO " + role + "; " +
                "CREATE POLICY fixture_existing_runtime ON khospace.\"PaymentIntents\" FOR SELECT TO " + role + " USING (true)");
        }
        var fingerprintSql = (string)typeof(PostgresMigrations).GetField("Fingerprint",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetRawConstantValue()!;
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using (var searchPath = new NpgsqlCommand("SET search_path=khospace,pg_catalog", connection))
            await searchPath.ExecuteNonQueryAsync();
        await using var fingerprint = new NpgsqlCommand(fingerprintSql, connection);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes((string)(await fingerprint.ExecuteScalarAsync())!)));
        await using var history = new NpgsqlCommand("""
            INSERT INTO khospace."MigrationHistory" ("Id","Checksum","SchemaHash")
            VALUES ('001_initial',@checksum,@hash)
            """, connection);
        history.Parameters.AddWithValue("checksum", PostgresMigrations.Checksum);
        history.Parameters.AddWithValue("hash", hash);
        await history.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Version_one_upgrades_without_replacing_data_and_requires_complete_history_for_readiness()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        await CreateVersionOne(db);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO khospace."FoundationMarkers" ("Id","Description","CreatedAt")
            VALUES ('migration-preserved','Synthetic upgrade preservation',now())
            """);
        Assert.False(await PostgresMigrations.IsReadyAsync(db, default));
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.True(await PostgresMigrations.IsReadyAsync(db, default));
        Assert.Equal("Synthetic upgrade preservation", (await db.FoundationMarkers.SingleAsync()).Description);
        Assert.Equal(2, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
        var columns = await db.Database.SqlQuery<string>(
            $"SELECT column_name::text AS \"Value\" FROM information_schema.columns WHERE table_schema='khospace' AND table_name='PaymentIntents'").ToListAsync();
        Assert.Contains("BankTransferReference", columns);
        Assert.Contains("ExpiresAt", columns);
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.Equal(2, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
    }

    [Fact]
    public async Task Version_one_drift_is_refused_before_any_pending_ddl()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        await CreateVersionOne(db);
        await db.Database.ExecuteSqlRawAsync("GRANT SELECT ON khospace.\"PaymentIntents\" TO PUBLIC");
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresMigrations.ApplyAsync(db, default));
        Assert.False(await db.Database.SqlQuery<bool>(
            $"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='khospace' AND table_name='PaymentIntents' AND column_name='BankTransferReference') AS \"Value\"").SingleAsync());
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
    }

    [Fact]
    public async Task Version_one_runtime_grants_and_row_policy_survive_upgrade_without_privilege_expansion()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        var role = "warehouse_fixture_role_" + Guid.NewGuid().ToString("N");
        try
        {
            await CreateVersionOne(db, role);
            await PostgresMigrations.ApplyAsync(db, default);
            Assert.True(await PostgresMigrations.IsReadyAsync(db, default));
            await using var runtime = fixture.Context();
            await runtime.Database.OpenConnectionAsync();
            await runtime.Database.ExecuteSqlRawAsync("SET ROLE \"" + role + '"');
            Assert.Empty(await runtime.PaymentIntents.AsNoTracking().ToListAsync());
            Assert.False(await runtime.Database.SqlQuery<bool>(
                $"SELECT has_table_privilege(current_user,'khospace.\"PaymentIntents\"','INSERT,UPDATE,DELETE') AS \"Value\"").SingleAsync());
            Assert.False(await runtime.Database.SqlQuery<bool>(
                $"SELECT has_schema_privilege(current_user,'khospace','CREATE') AS \"Value\"").SingleAsync());
            await runtime.Database.ExecuteSqlRawAsync("RESET ROLE");
            Assert.Equal(1, await db.Database.SqlQuery<int>(
                $"SELECT count(*)::integer AS \"Value\" FROM pg_policies WHERE schemaname='khospace' AND tablename='PaymentIntents' AND policyname='fixture_existing_runtime'").SingleAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP OWNED BY \"" + role + "\"; DROP ROLE IF EXISTS \"" + role + '"');
        }
    }
#pragma warning restore EF1003

    [Fact]
    public async Task Pending_migration_failure_rolls_back_ddl_and_preserves_version_one_for_retry()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        await CreateVersionOne(db);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.fixture_reject_payment_upgrade() RETURNS event_trigger LANGUAGE plpgsql AS $$
            BEGIN IF EXISTS (SELECT 1 FROM pg_event_trigger_ddl_commands()
              WHERE object_identity='khospace."PaymentIntents"' AND command_tag='ALTER TABLE')
            THEN RAISE EXCEPTION 'Synthetic upgrade failure'; END IF; END $$;
            CREATE EVENT TRIGGER fixture_reject_payment_upgrade ON ddl_command_end
            EXECUTE FUNCTION public.fixture_reject_payment_upgrade();
            """);
        var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresMigrations.ApplyAsync(db, default));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"SELECT count(*)::integer AS \"Value\" FROM khospace.\"MigrationHistory\"").SingleAsync());
        Assert.False(await db.Database.SqlQuery<bool>(
            $"SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='khospace' AND table_name='PaymentIntents' AND column_name='BankTransferReference') AS \"Value\"").SingleAsync());
        await db.Database.ExecuteSqlRawAsync(
            "DROP EVENT TRIGGER fixture_reject_payment_upgrade; DROP FUNCTION public.fixture_reject_payment_upgrade()");
        await PostgresMigrations.ApplyAsync(db, default);
        Assert.True(await PostgresMigrations.IsReadyAsync(db, default));
    }

    [Theory]
    [InlineData("DELETE FROM khospace.\"MigrationHistory\" WHERE \"Id\"='001_initial'")]
    [InlineData("UPDATE khospace.\"MigrationHistory\" SET \"Checksum\"=repeat('0',64) WHERE \"Id\"='001_initial'")]
    [InlineData("UPDATE khospace.\"MigrationHistory\" SET \"Id\"='000_unknown' WHERE \"Id\"='001_initial'")]
    public async Task Incomplete_or_tampered_history_prefix_is_not_adopted(string change)
    {
        await using var fixture = await PostgresRuntimeFixture.Create(empty: true);
        await using var db = fixture.Context();
        await PostgresMigrations.ApplyAsync(db, default);
        await db.Database.ExecuteSqlRawAsync(change);
        Assert.False(await PostgresMigrations.IsReadyAsync(db, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostgresMigrations.ApplyAsync(db, default));
    }
}
