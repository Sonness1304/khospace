using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Invitations;

public static class IdentityWriteLock
{
    // All identity/membership writers acquire this before organization/user locks.
    public static Task Acquire(AppDbContext db, CancellationToken ct) => DatabaseDialect.MutexAsync(db,
        "WarehouseRent.IdentityDirectory.v1", ct, timeoutMilliseconds: 10000, errorNumber: 51010, message: "Identity directory is busy.");
}
