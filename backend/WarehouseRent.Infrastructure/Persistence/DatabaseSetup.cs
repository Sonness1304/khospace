using Microsoft.EntityFrameworkCore;

namespace WarehouseRent.Infrastructure.Persistence;

public static class DatabaseSetup
{
    public static async Task ApplyAsync(AppDbContext db, string environment, string connectionString,
        bool seedDemo, CancellationToken cancellationToken = default)
    {
        // Validate before opening SQL or applying any migration, including for the seed command.
        if (seedDemo) DemoSeedPolicy.Validate(environment, connectionString);
        else DemoSeedPolicy.ValidateMigration(environment, connectionString);
        var originalTimeout = db.Database.GetCommandTimeout();
        // Explicit one-shot DDL/seed needs a separate bound from short runtime health queries.
        db.Database.SetCommandTimeout(30);
        try
        {
        await db.Database.MigrateAsync(cancellationToken);
        if (!seedDemo) return;

        // Command is explicit and local; serializable prevents concurrent demo seeding duplicates.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        if (!await db.FoundationMarkers.AnyAsync(marker => marker.Id == "synthetic-demo-v1", cancellationToken))
        {
            db.FoundationMarkers.Add(new FoundationMarker
            {
                Id = "synthetic-demo-v1",
                Description = "Synthetic foundation workspace. No real customers or transactions.",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await Identity.DemoIdentitySeed.ApplyAsync(db, cancellationToken);
        await Facilities.DemoFacilitySeed.ApplyAsync(db, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        }
        finally { db.Database.SetCommandTimeout(originalTimeout); }
    }
}
