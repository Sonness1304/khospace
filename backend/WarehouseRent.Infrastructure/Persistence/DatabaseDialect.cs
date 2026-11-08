using System.Buffers.Binary;
using System.Data;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace WarehouseRent.Infrastructure.Persistence;

/// <summary>Bounded runtime SQL differences. This does not enable PostgreSQL in the API host.</summary>
public static class DatabaseDialect
{
    private sealed record RowLock(string Table, string Predicate, int ArgumentCount);

    // Exact formats, not a general SQL translator. Each predicate corresponds to a reviewed call site.
    private static readonly IReadOnlyDictionary<string, RowLock> RowLocks = ReviewedRowLocks();

    public static bool IsPostgres(AppDbContext db) => db.Database.IsNpgsql();

    public static IQueryable<T> Rows<T>(AppDbContext db, FormattableString sqlServer) where T : class
    {
        if (!IsPostgres(db)) return db.Set<T>().FromSqlInterpolated(sqlServer);
        if (!RowLocks.TryGetValue(sqlServer.Format, out var shape)
            || sqlServer.ArgumentCount != shape.ArgumentCount
            || db.Model.FindEntityType(typeof(T)) is not { } entity
            || entity.GetTableName() != shape.Table || entity.GetSchema() != "khospace")
            throw new InvalidOperationException("Unreviewed PostgreSQL row-lock shape or entity mapping.");
        // GetArguments preserves parameter objects; neither identifiers nor values come from interpolation.
        var sql = FormattableStringFactory.Create(
            $"SELECT * FROM \"khospace\".\"{shape.Table}\" WHERE {shape.Predicate} FOR UPDATE", sqlServer.GetArguments());
        return db.Set<T>().FromSqlInterpolated(sql);
    }

    public static Task<DateTimeOffset> UtcNowAsync(AppDbContext db, CancellationToken ct = default) => IsPostgres(db)
        // Unlike CURRENT_TIMESTAMP, clock_timestamp is not frozen at the transaction's start.
        ? db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct)
        : db.Database.SqlQuery<DateTimeOffset>($"SELECT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00') AS [Value]").SingleAsync(ct);

    public static async Task MutexAsync(AppDbContext db, string resource, CancellationToken ct,
        int timeoutMilliseconds = 5000, int errorNumber = 51000, string message = "Command lock unavailable.")
    {
        if (db.Database.CurrentTransaction is not { } transaction)
            throw new InvalidOperationException("Mutexes require the caller's transaction.");
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (resource.Length > 255) throw new ArgumentOutOfRangeException(nameof(resource));
        if (timeoutMilliseconds is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (errorNumber < 50000) throw new ArgumentOutOfRangeException(nameof(errorNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!IsPostgres(db))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout={timeoutMilliseconds}; IF @r<0 THROW {errorNumber},{message},1;", ct);
            return;
        }

        // A deterministic signed 64-bit key shared by all application processes, not GetHashCode().
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        // Bound only this command; do not alter provider options or the caller's command timeout.
        command.CommandTimeout = (timeoutMilliseconds + 999) / 1000 + 1;
        command.CommandText = "SELECT pg_catalog.current_setting('lock_timeout')";
        var previous = (string)(await command.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("PostgreSQL lock timeout setting is unavailable."));
        command.CommandText = """
            SELECT pg_catalog.set_config('lock_timeout', @timeout, true);
            SELECT pg_catalog.pg_advisory_xact_lock(@key);
            SELECT pg_catalog.set_config('lock_timeout', @previous, true);
            """;
        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "timeout"; timeoutParameter.DbType = DbType.String;
        timeoutParameter.Value = timeoutMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms";
        command.Parameters.Add(timeoutParameter);
        var keyParameter = command.CreateParameter();
        keyParameter.ParameterName = "key"; keyParameter.DbType = DbType.Int64; keyParameter.Value = key;
        command.Parameters.Add(keyParameter);
        var previousParameter = command.CreateParameter();
        previousParameter.ParameterName = "previous"; previousParameter.DbType = DbType.String; previousParameter.Value = previous;
        command.Parameters.Add(previousParameter);
        // On failure PostgreSQL aborts the transaction. Do not hide the original error with a reset/retry;
        // caller rollback restores SET LOCAL and releases every transaction-owned advisory/row lock.
        await command.ExecuteNonQueryAsync(ct);
    }

    public static bool IsUniqueViolation(DbUpdateException error) => error.InnerException is
        SqlException { Number: 2601 or 2627 } or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IReadOnlyDictionary<string, RowLock> ReviewedRowLocks()
    {
        var result = new Dictionary<string, RowLock>(StringComparer.Ordinal);
        foreach (var table in new[] { "Bookings", "GateLanes", "GateVisits", "RenewalAcceptances", "StockRequests", "Facilities", "PaymentIntents", "PaymentReceipts", "ChargeStatements", "StockCounts", "CustomerOrganizations", "RentableUnits" })
            Add(table, "Id");
        Add("RentalAccounts", "BookingId");
        Add("Leases", "Id");
        Add("Leases", "BookingId");
        Add("Leases", "RentalAccountId");
        Add("StockBalances", "OrganizationId", "ItemId", "LeaseUnitId");
        return result;

        void Add(string table, params string[] columns)
        {
            var source = string.Join(" AND ", columns.Select((column, index) => $"[{column}]={{{index}}}"));
            var target = string.Join(" AND ", columns.Select((column, index) => $"\"{column}\"={{{index}}}"));
            result.Add($"SELECT * FROM [{table}] WITH (UPDLOCK,HOLDLOCK) WHERE {source}", new(table, target, columns.Length));
        }
    }
}
