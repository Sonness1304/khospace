using System.Net;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlAllocationGuardTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Parent_lock_blocks_an_independent_writer_until_the_owner_commits()
    {
        await using var first = fixture.Db(); await using var second = fixture.Db();
        await first.Database.OpenConnectionAsync(); await second.Database.OpenConnectionAsync();
        await using var firstTx = await first.Database.BeginTransactionAsync();
        await using var secondTx = await second.Database.BeginTransactionAsync();
        await new UnitLockService(first).LockAsync(DemoIdentitySeed.FacilityA, [SqlFacilitiesTests.UnitA]);
        var waiting = new UnitLockService(second).LockAsync(DemoIdentitySeed.FacilityA, [SqlFacilitiesTests.UnitA]);
        await Task.Delay(250);
        Assert.False(waiting.IsCompleted);
        await firstTx.CommitAsync();
        Assert.Single(await waiting.WaitAsync(TimeSpan.FromSeconds(5)));
        await secondTx.CommitAsync();
    }

    [Fact]
    public async Task Opposite_order_empty_allocation_sets_serialize_on_persistent_unit_parents()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(200);
        using var ready = new Barrier(2);
        async Task<bool> Allocate(Guid[] ids)
        {
            await using var db = fixture.Db(); await using var tx = await db.Database.BeginTransactionAsync();
            Assert.True(ready.SignalAndWait(TimeSpan.FromSeconds(10)));
            var guard = new UnitLockService(db);
            await guard.LockAsync(DemoIdentitySeed.FacilityA, ids);
            var utc = await guard.DatabaseUtcAsync();
            if (await guard.ConflictsAsync(ids, start, start.AddDays(2), utc)) return false;
            var maintenance = new MaintenanceBlock { Id = Guid.NewGuid(), FacilityId = DemoIdentitySeed.FacilityA, StartDate = start, EndDate = start.AddDays(2), Reason = "Synthetic opposite-order lock race" };
            db.MaintenanceBlocks.Add(maintenance);
            foreach (var id in ids) db.SpaceAllocations.Add(new SpaceAllocation { Id = Guid.NewGuid(), UnitId = id, StartDate = start, EndDate = start.AddDays(2), State = "Maintenance", MaintenanceId = maintenance.Id });
            await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
        }
        var outcomes = await Task.WhenAll(Task.Run(() => Allocate([SqlFacilitiesTests.UnitA, SqlFacilitiesTests.UnitB])), Task.Run(() => Allocate([SqlFacilitiesTests.UnitB, SqlFacilitiesTests.UnitA])));
        Assert.Single(outcomes, x => x); Assert.Single(outcomes, x => !x);
        await using var check = fixture.Db();
        Assert.Equal(2, await check.SpaceAllocations.CountAsync(x => x.StartDate == start));
        Assert.Equal(2, await check.SpaceAllocations.Where(x => x.StartDate == start).Select(x => x.UnitId).Distinct().CountAsync());
        Assert.False(await check.SpaceAllocations.AnyAsync(x => check.RentableUnits.Any(u => u.Id == x.UnitId && u.FacilityId == DemoIdentitySeed.FacilityB)));
    }

    [Fact]
    public async Task Expired_hold_is_ignored_but_active_hold_and_overdue_occupancy_block()
    {
        await using var db = fixture.Db();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(50);
        var now = DateTimeOffset.UtcNow;
        var expired = new SpaceAllocation { Id = Guid.NewGuid(), UnitId = SqlFacilitiesTests.UnitA, StartDate = start, EndDate = start.AddDays(2), State = "Held", HoldExpiresAt = now.AddMinutes(-1), BookingLineId = await fixture.SeedAllocationOwner(SqlFacilitiesTests.UnitA, start, start.AddDays(2)) };
        var active = new SpaceAllocation { Id = Guid.NewGuid(), UnitId = SqlFacilitiesTests.UnitB, StartDate = start, EndDate = start.AddDays(2), State = "Held", HoldExpiresAt = now.AddMinutes(15), BookingLineId = await fixture.SeedAllocationOwner(SqlFacilitiesTests.UnitB, start, start.AddDays(2)) };
        db.SpaceAllocations.AddRange(expired, active); await db.SaveChangesAsync();
        using var client = fixture.Client();
        var path = $"/api/v1/availability?facilityId={DemoIdentitySeed.FacilityA}&startDate={start:yyyy-MM-dd}&endDate={start.AddDays(1):yyyy-MM-dd}";
        var first = await client.GetStringAsync(path);
        Assert.Contains(SqlFacilitiesTests.UnitA.ToString(), first); Assert.DoesNotContain(SqlFacilitiesTests.UnitB.ToString(), first);
        var occupancy = new UnitOccupancy { Id = Guid.NewGuid(), UnitId = SqlFacilitiesTests.UnitA, LeaseUnitId = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, start.AddDays(-80), start.AddDays(-60)), OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = start.AddDays(-60), HandedOverAt = now.AddDays(-20) };
        db.UnitOccupancies.Add(occupancy); await db.SaveChangesAsync();
        Assert.DoesNotContain(SqlFacilitiesTests.UnitA.ToString(), await client.GetStringAsync(path));
        occupancy.ClearedAt = now; await db.SaveChangesAsync();
        Assert.Contains(SqlFacilitiesTests.UnitA.ToString(), await client.GetStringAsync(path));
    }

    [Fact]
    public async Task Foreign_unit_maintenance_writes_nothing_and_missing_transaction_refuses_lock()
    {
        using var manager = await fixture.Login("operator@example.test");
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(100);
        var response = await manager.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", new { facilityId = DemoIdentitySeed.FacilityA,
            unitIds = new[] { SqlFacilitiesTests.UnitA, Guid.Parse("b0000001-0000-4000-8000-000000000001") }, startDate = start, endDate = start.AddDays(1), reason = "Synthetic forged facility child" }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var db = fixture.Db();
        Assert.False(await db.SpaceAllocations.AnyAsync(x => x.StartDate == start));
        Assert.False(await db.MaintenanceBlocks.AnyAsync(x => x.StartDate == start));
        Assert.False(await db.SecurityAudits.AnyAsync(x => x.Reason == "Synthetic forged facility child"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitLockService(db).LockAsync(DemoIdentitySeed.FacilityA, [SqlFacilitiesTests.UnitA]));
    }
}
