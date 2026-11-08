using Microsoft.EntityFrameworkCore;
using Npgsql;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class PostgresRuntimeTests
{
    [Fact]
    public async Task Actual_postgres_schema_rowlocks_and_database_clock_work_without_switching_production()
    {
        await using var fixture = await PostgresRuntimeFixture.Create();
        await using var db = fixture.Context();
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO khospace.\"Facilities\" (\"Id\",\"Code\",\"Name\",\"Address\",\"PermittedGoods\",\"TimeZoneId\",\"Published\") VALUES ({id},'FIXTURE','Synthetic','Fixture','General','Asia/Ho_Chi_Minh',false)");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var row = await DatabaseDialect.Rows<WarehouseRent.Infrastructure.Facilities.Facility>(db,
            $"SELECT * FROM [Facilities] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync();
        Assert.Equal(id, row.Id); Assert.Equal(8, row.Version.Length);
        var now = await DatabaseDialect.UtcNowAsync(db, CancellationToken.None);
        Assert.Equal(TimeSpan.Zero, now.Offset);
        Assert.InRange(now, DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow.AddSeconds(10));
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Advisory_mutex_blocks_other_transaction_and_preserves_prior_lock_timeout()
    {
        await using var fixture = await PostgresRuntimeFixture.Create(stockOnly: true);
        await using var first = fixture.Context(); await using var second = fixture.Context();
        await using var t1 = await first.Database.BeginTransactionAsync();
        await using var t2 = await second.Database.BeginTransactionAsync();
        foreach(var timeout in new[]{0,-1,10001})
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>DatabaseDialect.MutexAsync(first,"invalid-fixture",CancellationToken.None,timeout));
        await Assert.ThrowsAsync<ArgumentException>(()=>DatabaseDialect.MutexAsync(first," ",CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>DatabaseDialect.MutexAsync(first,new string('x',256),CancellationToken.None));
        await DatabaseDialect.MutexAsync(first, "synthetic-mutex", CancellationToken.None);
        await first.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='173ms'");
        await DatabaseDialect.MutexAsync(first, "different-synthetic-mutex", CancellationToken.None);
        var setting = await first.Database.SqlQuery<string>($"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync();
        Assert.Equal("173ms", setting);
        var error = await Assert.ThrowsAsync<PostgresException>(() => DatabaseDialect.MutexAsync(second, "synthetic-mutex", CancellationToken.None, 150));
        Assert.Equal("55P03", error.SqlState);
        await t2.RollbackAsync(); await t1.CommitAsync();
    }

    [Fact]
    public async Task Absent_stock_key_serializes_insert_then_reads_committed_balance_not_duplicate()
    {
        // Minimal StockBalances fixture deliberately tests lock semantics, not full business FK parity.
        await using var fixture = await PostgresRuntimeFixture.Create(stockOnly: true);
        var organization = Guid.NewGuid(); var item = Guid.NewGuid(); var grant = Guid.NewGuid();
        await using var first = fixture.Context(); await using var second = fixture.Context();
        await using var t1 = await first.Database.BeginTransactionAsync();
        await using var t2 = await second.Database.BeginTransactionAsync();
        var balance = await new StockLockService(first).LockAsync(organization,item,grant,CancellationToken.None);
        balance.Quantity = 7;
        var waiting = new StockLockService(second).LockAsync(organization,item,grant,CancellationToken.None);
        await Task.Delay(100); Assert.False(waiting.IsCompleted);
        await first.SaveChangesAsync(); await t1.CommitAsync();
        var observed = await waiting;
        Assert.Equal(7, observed.Quantity); Assert.Equal(8, observed.Version.Length);
        Assert.Equal(EntityState.Unchanged, second.Entry(observed).State);
        await t2.CommitAsync();
        Assert.Equal(1, await second.StockBalances.CountAsync());
    }
}
