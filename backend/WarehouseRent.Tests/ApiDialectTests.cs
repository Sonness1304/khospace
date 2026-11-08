using System.Text.RegularExpressions;
using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

// Source contracts supplement provider translation tests; they do not claim live PostgreSQL parity.
public sealed class ApiDialectTests
{
    private static string Features
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "backend", "WarehouseRent.Api", "Features"))) directory = directory.Parent;
            return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "backend", "WarehouseRent.Api", "Features");
        }
    }
    private static string Source(string path) => File.ReadAllText(Path.Combine(Features, path));

    [Theory]
    [InlineData(true, true, false, PostgresErrorCodes.SerializationFailure, IsolationLevel.Serializable)]
    [InlineData(true, true, true, PostgresErrorCodes.SerializationFailure, IsolationLevel.Serializable)]
    [InlineData(true, false, false, PostgresErrorCodes.DeadlockDetected, IsolationLevel.ReadCommitted)]
    [InlineData(false, true, false, PostgresErrorCodes.SerializationFailure, IsolationLevel.ReadCommitted)]
    public async Task Transaction_abort_returns_safe_conflict_without_writes_or_callbacks(bool postgres, bool serializable,
        bool wrapped, string sqlState, IsolationLevel expectedIsolation)
    {
        var failure = new PostgresException("synthetic transaction abort", "ERROR", "ERROR", sqlState);
        var probe = new AbortTransactionProbe(wrapped ? new DbUpdateException("synthetic wrapper", failure) : failure);
        var options = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) options.UseNpgsql("Host=127.0.0.1;Port=1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture");
        else options.UseSqlServer("Server=127.0.0.1,1;Database=WarehouseRent_Test_Unreachable;Integrated Security=True");
        options.AddInterceptors(new NoConnection(), probe);
        await using var db = new AppDbContext(options.Options);
        var context = new DefaultHttpContext();
        context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString();
        var executor = new CommandExecutor(db, new CurrentActor(db, new HttpContextAccessor { HttpContext = context }));
        var writes = 0; var committed = false;
        var result = await executor.ExecuteAsync(context, Guid.NewGuid(), "SyntheticAbort", new { },
            _ => { writes++; return Task.FromResult<object>(new { }); }, CancellationToken.None,
            serializableAfterReceipt: serializable, onCommitted: () => committed = true);
        Assert.Equal(expectedIsolation, probe.Isolation);
        Assert.Equal(1, probe.Starts);
        Assert.Equal(0, writes);
        Assert.False(committed);
        Assert.Equal(409, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var body = Assert.IsAssignableFrom<IValueHttpResult>(result).Value;
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        Assert.Contains("COMMAND_RETRY_REQUIRED", json);
        Assert.Contains("same Idempotency-Key", json);
    }

    private sealed class NoConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class AbortTransactionProbe(Exception failure) : DbTransactionInterceptor
    {
        public IsolationLevel? Isolation { get; private set; }
        public int Starts { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        { Starts++; Isolation = eventData.IsolationLevel; throw failure; }
    }

    [Fact]
    public void All_existing_row_locks_and_database_clocks_route_through_dialect()
    {
        var source = string.Join("\n", Directory.GetFiles(Features, "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.DoesNotContain("FromSqlInterpolated", source);
        Assert.DoesNotContain("SYSUTCDATETIME", source);
        Assert.DoesNotContain("sp_getapplock", source);
        Assert.Equal(23, Regex.Matches(source, @"DatabaseDialect\.Rows<").Count);
        Assert.Equal(7, Regex.Matches(source, @"DatabaseDialect\.UtcNowAsync\(").Count);
        Assert.Equal(4, Regex.Matches(source, @"DatabaseDialect\.MutexAsync\(").Count);
    }

    [Fact]
    public void Serializable_receipt_flow_is_provider_explicit_and_never_automatically_retries_writes()
    {
        var source = Source(Path.Combine("Common", "CommandExecutor.cs"));
        Assert.Contains("serializableAfterReceipt && db.Database.IsNpgsql() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted", source);
        Assert.Contains("serializableAfterReceipt && !db.Database.IsNpgsql()", source);
        Assert.Contains("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;", source);
        Assert.Contains("PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }", source);
        Assert.Contains("COMMAND_RETRY_REQUIRED", source);
        Assert.Contains("same Idempotency-Key", source);
        Assert.Contains("DatabaseDialect.IsUniqueViolation(e)", source);
        Assert.DoesNotContain("CreateExecutionStrategy", source);
        Assert.DoesNotContain("while (", source);
    }

    [Fact]
    public void Identity_and_notice_mutexes_keep_their_resources_and_original_timeouts()
    {
        var identity = Source(Path.Combine("Invitations", "IdentityWriteLock.cs"));
        Assert.Contains("WarehouseRent.IdentityDirectory.v1", identity);
        Assert.Contains("timeoutMilliseconds: 10000", identity);
        Assert.Contains("errorNumber: 51010", identity);
        Assert.Contains("Identity directory is busy.", identity);
        foreach (var file in new[] { "NoticeEndpoints.cs", "ReceivableReminderEndpoints.cs" })
        {
            var notice = Source(Path.Combine("Notices", file));
            Assert.Contains("\"WarehouseRent.Notice.\" + facilityId", notice);
            Assert.Contains("errorNumber: 51013", notice);
            Assert.Contains("Notice job busy.", notice);
        }
    }
}
