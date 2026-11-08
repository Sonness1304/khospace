using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Stock;
namespace WarehouseRent.Infrastructure.Persistence;
public sealed class StockLockService(AppDbContext db)
{
    public async Task<StockBalance> LockAsync(Guid organization, Guid item, Guid grant, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Stock locks require the caller's transaction.");
        // PostgreSQL FOR UPDATE cannot lock an absent row. Serialize this exact composite key BEFORE
        // lookup; the lock belongs to the caller's transaction and survives until commit/rollback.
        if (DatabaseDialect.IsPostgres(db))
            await DatabaseDialect.MutexAsync(db, $"WarehouseRent.Stock.{organization:D}/{item:D}/{grant:D}", ct);
        // SQL Server retains its original serializable composite-PK key-range lock.
        var value = await DatabaseDialect.Rows<StockBalance>(db, $"SELECT * FROM [StockBalances] WITH (UPDLOCK,HOLDLOCK) WHERE [OrganizationId]={organization} AND [ItemId]={item} AND [LeaseUnitId]={grant}").SingleOrDefaultAsync(ct);
        if (value is not null) return value;
        value = new StockBalance { OrganizationId = organization, ItemId = item, LeaseUnitId = grant };
        db.StockBalances.Add(value); return value;
    }
}
