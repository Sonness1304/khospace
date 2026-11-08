using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api;

public sealed record HealthStatus(string Status, DateTimeOffset ObservedAt);

public sealed class ReadinessProbe(IServiceProvider services)
{
    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var db = services.GetRequiredService<AppDbContext>();
            if (!await db.Database.CanConnectAsync(timeout.Token)) return false;
            if (db.Database.IsNpgsql())
                return await PostgresMigrations.IsReadyAsync(db, timeout.Token)
                    && await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db, timeout.Token)
                    && await db.Users.AnyAsync(user=>user.Active && user.OperatorAdmin, timeout.Token);
            if ((await db.Database.GetPendingMigrationsAsync(timeout.Token)).Any()) return false;
            // Verify the mapped schema, not only a reachable SQL instance.
            _ = await db.FoundationMarkers.AsNoTracking().Select(marker => marker.Id).FirstOrDefaultAsync(timeout.Token);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }
}
