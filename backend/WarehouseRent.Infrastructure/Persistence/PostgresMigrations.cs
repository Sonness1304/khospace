using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.EntityFrameworkCore.Storage;

namespace WarehouseRent.Infrastructure.Persistence;

/// <summary>Explicit versioned PostgreSQL DDL. Never discovers or applies SQL Server EF migrations.</summary>
public static class PostgresMigrations
{
    public const string Latest = "002_sepay";
    // These two properties retain the immutable baseline API for upgrade tooling.
    public static string Script { get; } = ReadScript("001_initial");
    public static string Checksum { get; } = Hash(Script);
    private sealed record Migration(string Id, string Sql, string Checksum);
    private static readonly Migration[] Migrations = [
        new("001_initial", Script, Checksum),
        CreateMigration(Latest)
    ];
    public static string LatestChecksum => Migrations[^1].Checksum;
    private static Migration CreateMigration(string id)
    {
        var sql = ReadScript(id);
        return new(id, sql, Hash(sql));
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string ReadScript(string id)
    {
        using var stream = typeof(PostgresMigrations).Assembly.GetManifestResourceStream(
            $"WarehouseRent.Infrastructure.Persistence.PostgresMigrations.{id}.sql")
            ?? throw new InvalidOperationException("PostgreSQL migration resource is missing.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd().Replace("\r\n","\n",StringComparison.Ordinal);
    }

    // Includes ACLs, RLS policies, sequence parameters and structural guards, but not row data/sequence current value.
    // Only explicit owner provisioning can refresh it after validated grants; runtime has read-only history access.
    private const string Fingerprint = """
        SELECT coalesce(string_agg(definition, E'\n' ORDER BY definition),'') FROM (
          SELECT 'relation:'||c.relname||':'||c.relkind::text||':'||c.relrowsecurity||':'||c.relforcerowsecurity AS definition
          FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='khospace'
          UNION ALL SELECT 'column:'||c.relname||':'||a.attname||':'||format_type(a.atttypid,a.atttypmod)||':'||a.attnotnull||':'||coalesce(pg_get_expr(d.adbin,d.adrelid),'')
          FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
          LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
          WHERE n.nspname='khospace' AND a.attnum>0 AND NOT a.attisdropped
          UNION ALL SELECT 'constraint:'||c.relname||':'||p.conname||':'||pg_get_constraintdef(p.oid)
          FROM pg_constraint p JOIN pg_class c ON c.oid=p.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='khospace'
          UNION ALL SELECT 'index:'||indexname||':'||indexdef FROM pg_indexes WHERE schemaname='khospace'
          UNION ALL SELECT 'trigger:'||c.relname||':'||t.tgenabled::text||':'||pg_get_triggerdef(t.oid)
          FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='khospace' AND NOT t.tgisinternal
          UNION ALL SELECT 'function:'||p.proname||':'||p.prosecdef||':'||p.prosrc
          FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='khospace'
          UNION ALL SELECT 'sequence:'||c.relname||':'||s.seqstart||':'||s.seqincrement||':'||s.seqmax||':'||s.seqmin||':'||s.seqcache||':'||s.seqcycle
          FROM pg_sequence s JOIN pg_class c ON c.oid=s.seqrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='khospace'
          UNION ALL SELECT 'policy:'||tablename||':'||policyname||':'||permissive||':'||roles::text||':'||cmd||':'||coalesce(qual,'')||':'||coalesce(with_check,'')
          FROM pg_policies WHERE schemaname='khospace'
          UNION ALL SELECT 'acl:'||c.relname||':'||CASE WHEN a.grantee=c.relowner THEN 'OWNER' WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee)::text END||':'||a.privilege_type||':'||a.is_grantable
          FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
          CROSS JOIN LATERAL aclexplode(coalesce(c.relacl,acldefault(CASE WHEN c.relkind='S' THEN 'S'::"char" ELSE 'r'::"char" END,c.relowner))) a
          WHERE n.nspname='khospace' AND c.relkind IN ('r','S','v','m')
          UNION ALL SELECT 'schemaacl:'||CASE WHEN a.grantee=n.nspowner THEN 'OWNER' WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee)::text END||':'||a.privilege_type||':'||a.is_grantable
          FROM pg_namespace n CROSS JOIN LATERAL aclexplode(coalesce(n.nspacl,acldefault('n',n.nspowner))) a WHERE n.nspname='khospace'
          UNION ALL SELECT 'functionacl:'||p.proname||':'||CASE WHEN a.grantee=p.proowner THEN 'OWNER' WHEN a.grantee=0 THEN 'PUBLIC' ELSE pg_get_userbyid(a.grantee)::text END||':'||a.privilege_type||':'||a.is_grantable
          FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace CROSS JOIN LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a WHERE n.nspname='khospace'
        ) objects
        """;

    private static async Task<object?> Scalar(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        return await command.ExecuteScalarAsync(ct);
    }

    public static async Task ApplyAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) throw new InvalidOperationException("PostgreSQL migrations require the PostgreSQL provider.");
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await Scalar(connection, transaction, "SET LOCAL lock_timeout='10s'; SET LOCAL search_path=khospace,pg_catalog; SELECT pg_advisory_xact_lock(628043657916231001)", ct);
        var applied = 0;
        if ((bool)(await Scalar(connection, transaction, "SELECT to_regclass('khospace.\"MigrationHistory\"') IS NOT NULL", ct))!)
        {
            applied = await ValidateHistory(connection, transaction, ct)
                ?? throw new InvalidOperationException("PostgreSQL migration history or schema drift was detected.");
        }
        else
        {
            var existing = (long)(await Scalar(connection, transaction, "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='khospace'", ct))!;
            var routines = (long)(await Scalar(connection, transaction, "SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='khospace'", ct))!;
            if (existing != 0 || routines != 0) throw new InvalidOperationException("An unversioned application schema cannot be adopted.");
        }
        for (var index = applied; index < Migrations.Length; index++)
        {
            var migration = Migrations[index];
            await using (var apply = new NpgsqlCommand(migration.Sql, connection, transaction) { CommandTimeout = 60 }) await apply.ExecuteNonQueryAsync(ct);
            var hash = Hash((string)(await Scalar(connection, transaction, Fingerprint, ct))!);
            await using var receipt = new NpgsqlCommand("INSERT INTO khospace.\"MigrationHistory\" (\"Id\",\"Checksum\",\"SchemaHash\") VALUES (@id,@checksum,@hash)", connection, transaction);
            receipt.Parameters.AddWithValue("id", migration.Id); receipt.Parameters.AddWithValue("checksum", migration.Checksum); receipt.Parameters.AddWithValue("hash", hash);
            await receipt.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    private static async Task<int?> ValidateHistory(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        var applied = 0;
        string? lastHash = null;
        await using (var query = new NpgsqlCommand("SELECT \"Id\",\"Checksum\",\"SchemaHash\" FROM khospace.\"MigrationHistory\" ORDER BY \"Id\" LIMIT @limit", connection, transaction))
        {
            query.Parameters.AddWithValue("limit", Migrations.Length + 1);
            await using (var reader = await query.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    if (applied >= Migrations.Length || reader.GetString(0) != Migrations[applied].Id ||
                        reader.GetString(1) != Migrations[applied].Checksum) return null;
                    lastHash = reader.GetString(2);
                    if (lastHash.Length != 64 || !lastHash.All(Uri.IsHexDigit)) return null;
                    applied++;
                }
            }
        }
        return applied > 0 && lastHash == Hash((string)(await Scalar(connection, transaction, Fingerprint, ct))!) ? applied : null;
    }

    private static async Task<bool> Matches(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
        => await ValidateHistory(connection, transaction, ct) == Migrations.Length;

    internal static async Task AssertProvisioningBaselineAsync(AppDbContext db,CancellationToken ct)
    {
        var connection=(NpgsqlConnection)db.Database.GetDbConnection();var transaction=(NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction();
        await Scalar(connection,transaction,"SET LOCAL lock_timeout='10s'; SET LOCAL search_path=khospace,pg_catalog; SELECT pg_advisory_xact_lock(628043657916231001)",ct);
        if(!await Matches(connection,transaction,ct))throw new InvalidOperationException("Refusing to provision over schema or privilege drift.");
    }

    internal static async Task RecordProvisionedFingerprintAsync(AppDbContext db,CancellationToken ct)
    {
        var connection=(NpgsqlConnection)db.Database.GetDbConnection();var transaction=(NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction();
        var hash=Hash((string)(await Scalar(connection,transaction,Fingerprint,ct))!);
        await using var command=new NpgsqlCommand("UPDATE khospace.\"MigrationHistory\" SET \"SchemaHash\"=@hash WHERE \"Id\"=@id AND \"Checksum\"=@checksum",connection,transaction);
        command.Parameters.AddWithValue("hash",hash);command.Parameters.AddWithValue("id",Latest);command.Parameters.AddWithValue("checksum",LatestChecksum);
        if(await command.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("Migration provenance changed during provisioning.");
    }

    public static async Task<bool> IsReadyAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return false;
        try
        {
            await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            await Scalar(connection, transaction, "SET TRANSACTION READ ONLY; SET LOCAL search_path=khospace,pg_catalog", ct);
            return await Matches(connection, transaction, ct);
        }
        catch (Exception error) when (error is not OutOfMemoryException && error is not OperationCanceledException) { return false; }
    }
}
