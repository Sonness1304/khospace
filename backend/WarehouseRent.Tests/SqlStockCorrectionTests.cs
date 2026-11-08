using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data.Common;
using Microsoft.AspNetCore.Http;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlStockCorrectionTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
    private async Task<(Guid Grant, Guid Lease)> Grant()
    {
        Guid id;
        await using (var db = fixture.Db()) { var source = await db.RentableUnits.SingleAsync(x => x.Id == SqlFacilitiesTests.UnitA); var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = source.FacilityId, ZoneId = source.ZoneId, Code = "SC-" + Guid.NewGuid().ToString("N"), AreaM2 = 10, Published = true }; db.RentableUnits.Add(unit); await db.SaveChangesAsync(); id = unit.Id; }
        var grant = await fixture.SeedOccupancyOwner(id, Today, Today.AddDays(10));
        await using var check = fixture.Db(); var lu = await check.LeaseUnits.SingleAsync(x => x.Id == grant);
        check.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = id, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = Today.AddDays(10), HandedOverAt = DateTimeOffset.UtcNow }); await check.SaveChangesAsync(); return (grant, lu.LeaseId);
    }
    private async Task<(Guid Grant, Guid Lease, Guid Other, Guid Item)> Setup()
    {
        var one = await Grant(); var other = await Grant(); using var customer = await fixture.Login();
        var item = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = Guid.NewGuid().ToString(), description = "Stock correction fixture", baseUom = "box", reason = "Synthetic item" }, key: Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        return (one.Grant, one.Lease, other.Grant, item);
    }
    private async Task<(Guid Id, Guid Line, string Version)> Approved(Guid grant, Guid lease, Guid item, string type, string quantity)
    {
        using var customer = await fixture.Login(); using var clerk = await fixture.Login("clerk@example.test");
        var draft = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = lease, leaseUnitId = grant, type, lines = new[] { new { itemId = item, quantity } }, reason = "Customer instruction" }, key: Guid.NewGuid()))).GetProperty("result"); var id = draft.GetProperty("resourceId").GetGuid();
        var submitted = (await Ok(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{id}/submit", new { reason = "Submit instruction" }, draft.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result");
        var approved = (await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{id}/approve", new { reason = "Review customer instruction" }, submitted.GetProperty("version").GetString(), Guid.NewGuid()))).GetProperty("result");
        var read = await Ok(await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{id}")); return (id, read.GetProperty("lines")[0].GetProperty("id").GetGuid(), approved.GetProperty("version").GetString()!);
    }
    private async Task<Guid> Full(Guid grant, Guid lease, Guid item, string type, string quantity)
    {
        var r = await Approved(grant, lease, item, type, quantity); using var clerk = await fixture.Login("clerk@example.test");
        return (await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{r.Id}/post", new { reason = "Observed quantity", lines = new[] { new { requestLineId = r.Line, quantity } } }, r.Version, Guid.NewGuid()))).GetProperty("result").GetProperty("postingId").GetGuid();
    }
    private static object Transfer(Guid from, Guid to, Guid item, string quantity, Guid? id = null) => new { transferId = id ?? Guid.NewGuid(), organizationId = DemoIdentitySeed.OrganizationA, sourceLeaseUnitId = from, destinationLeaseUnitId = to, itemId = item, quantity, reason = "Observed internal transfer" };
    private async Task<decimal> Balance(Guid grant) { await using var db = fixture.Db(); return await db.StockBalances.Where(x => x.LeaseUnitId == grant).SumAsync(x => x.Quantity); }
    [Fact]
    public async Task Transfer_is_atomic_conserves_stock_and_business_replay_matches_payload()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var clerk = await fixture.Login("clerk@example.test"); var id = Guid.NewGuid(); var body = Transfer(s.Grant, s.Other, s.Item, "4", id);
        var saved = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", body, key: Guid.NewGuid()))).GetProperty("result");
        Assert.Equal(saved.GetRawText(), (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", body, key: Guid.NewGuid()))).GetProperty("result").GetRawText());
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "2", id), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(6, await Balance(s.Grant)); Assert.Equal(4, await Balance(s.Other)); await using var db = fixture.Db(); var entries = await db.StockLedger.Where(x => x.StockPostingId == id).ToListAsync(); Assert.Equal(2, entries.Count); Assert.Equal(0, entries.Sum(x => x.Delta));
    }
    [Fact]
    public async Task Competing_transfers_cannot_overdraw_and_reconcile_with_ledger()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var one = await fixture.Login("clerk@example.test"); using var two = await fixture.Login("clerk@example.test");
        var results = await Task.WhenAll(one.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "7"), key: Guid.NewGuid()), two.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "7"), key: Guid.NewGuid()));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict); Assert.Equal(3, await Balance(s.Grant)); Assert.Equal(7, await Balance(s.Other));
    }
    [Fact]
    public async Task Consumed_receipt_cannot_be_reversed_then_issue_reversal_and_concurrent_receipt_reversal_commit_once()
    {
        var s = await Setup(); var receipt = await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); var issue = await Full(s.Grant, s.Lease, s.Item, "Issue", "7"); using var manager = await fixture.Login("manager@example.test"); using var second = await fixture.Login("manager@example.test"); var body = new { reason = "Correct erroneous document", reversalConfirmed = true };
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{receipt}/reverse", body, key: Guid.NewGuid())).StatusCode);
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{issue}/reverse", body, key: Guid.NewGuid()));
        var results = await Task.WhenAll(manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{receipt}/reverse", body, key: Guid.NewGuid()), second.Send(HttpMethod.Post, $"/api/v1/stock/postings/{receipt}/reverse", body, key: Guid.NewGuid()));
        var one = (await Ok(results[0])).GetProperty("result"); Assert.Equal(one.GetRawText(), (await Ok(results[1])).GetProperty("result").GetRawText()); Assert.Equal(0, await Balance(s.Grant));
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{one.GetProperty("postingId").GetGuid()}/reverse", body, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Transfer_reversal_restores_both_legs_without_editing_original()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var clerk = await fixture.Login("clerk@example.test"); using var manager = await fixture.Login("manager@example.test");
        var transfer = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "4"), key: Guid.NewGuid()))).GetProperty("result").GetProperty("postingId").GetGuid();
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{transfer}/reverse", new { reason = "Reverse wrong destination", reversalConfirmed = true }, key: Guid.NewGuid()));
        Assert.Equal(10, await Balance(s.Grant)); Assert.Equal(0, await Balance(s.Other)); await using var db = fixture.Db(); Assert.Equal(2, await db.StockLedger.CountAsync(x => x.StockPostingId == transfer));
    }
    [Fact]
    public async Task Count_requires_different_manager_current_snapshot_and_blocks_physical_clearance()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var manager = await fixture.Login("manager@example.test"); using var other = await fixture.Login("operator@example.test");
        var created = (await Ok(await manager.Send(HttpMethod.Post, "/api/v1/stock/counts", new { organizationId = DemoIdentitySeed.OrganizationA, leaseUnitId = s.Grant, itemId = s.Item, expectedQuantity = "10", countedQuantity = "8", reason = "Observed two missing boxes" }, key: Guid.NewGuid()))).GetProperty("result"); var id = created.GetProperty("resourceId").GetGuid(); var version = created.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/approve", new { reason = "Cannot approve own count" }, version, Guid.NewGuid())).StatusCode);
        await Ok(await other.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/approve", new { reason = "Independent recount approval" }, version, Guid.NewGuid())); Assert.Equal(8, await Balance(s.Grant));
        var count = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/stock/counts/{id}")); Assert.Equal("Posted", count.GetProperty("state").GetString()); Assert.NotEqual(count.GetProperty("createdBy").GetString(), count.GetProperty("approvedBy").GetString());
    }
    [Fact]
    public async Task Count_stale_after_stock_changes_requires_cancel_and_recount_even_when_quantity_restored()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var clerk = await fixture.Login("clerk@example.test"); using var manager = await fixture.Login("manager@example.test");
        var created = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/counts", new { organizationId = DemoIdentitySeed.OrganizationA, leaseUnitId = s.Grant, itemId = s.Item, expectedQuantity = "10", countedQuantity = "9", reason = "Count before activity" }, key: Guid.NewGuid()))).GetProperty("result"); var id = created.GetProperty("resourceId").GetGuid(); var version = created.GetProperty("version").GetString();
        await Full(s.Grant, s.Lease, s.Item, "Issue", "1"); await Full(s.Grant, s.Lease, s.Item, "Receipt", "1");
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/approve", new { reason = "Stale snapshot" }, version, Guid.NewGuid())).StatusCode);
        await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/cancel", new { reason = "Cancel stale observation" }, version, Guid.NewGuid())); Assert.Equal(10, await Balance(s.Grant));
    }
    [Fact]
    public async Task Partial_fulfillment_has_stable_business_identity_and_never_exceeds_instruction()
    {
        var s = await Setup(); var r = await Approved(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var clerk = await fixture.Login("clerk@example.test");
        var id = Guid.NewGuid(); var body = new { fulfillmentId = id, reason = "First four arrived", lines = new[] { new { requestLineId = r.Line, quantity = "4" } } }; var path = $"/api/v1/stock-requests/{r.Id}/fulfillments";
        var saved = (await Ok(await clerk.Send(HttpMethod.Post, path, body, r.Version, Guid.NewGuid()))).GetProperty("result");
        Assert.Equal(saved.GetRawText(), (await Ok(await clerk.Send(HttpMethod.Post, path, body, r.Version, Guid.NewGuid()))).GetProperty("result").GetRawText());
        var read = await Ok(await clerk.Send(HttpMethod.Get, $"/api/v1/stock-requests/{r.Id}")); Assert.Equal("Approved", read.GetProperty("state").GetString()); Assert.Equal("4", read.GetProperty("lines")[0].GetProperty("fulfilledQuantity").GetString()); var version = read.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, path, new { fulfillmentId = Guid.NewGuid(), reason = "Too many", lines = new[] { new { requestLineId = r.Line, quantity = "7" } } }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{r.Id}/post", new { reason = "Cannot double post full request", lines = new[] { new { requestLineId = r.Line, quantity = "10" } } }, version, Guid.NewGuid())).StatusCode);
        await Ok(await clerk.Send(HttpMethod.Post, path, new { fulfillmentId = Guid.NewGuid(), reason = "Remaining six arrived", lines = new[] { new { requestLineId = r.Line, quantity = "6" } } }, version, Guid.NewGuid())); Assert.Equal(10, await Balance(s.Grant));
        read = await Ok(await clerk.Send(HttpMethod.Get, $"/api/v1/stock-requests/{r.Id}")); Assert.Equal("Fulfilled", read.GetProperty("state").GetString());
    }
    [Fact]
    public async Task Corrections_enforce_customer_and_role_scope_and_custody()
    {
        var s = await Setup(); using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var clerk = await fixture.Login("clerk@example.test"); var transfer = Transfer(s.Grant, s.Other, s.Item, "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, "/api/v1/stock/transfers", transfer, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, "/api/v1/stock/transfers", transfer, key: Guid.NewGuid())).StatusCode);
        await using (var db = fixture.Db()) { var occupancy = await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Other); occupancy.ClearedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", transfer, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Populated_P0_upgrade_preserves_ids_deltas_balances_backfills_fulfillment_and_refuses_destructive_down()
    {
        var isolated = new IdentitySqlFixture(); await isolated.InitializeAsync();
        try { await new SqlStockCorrectionTests(isolated).RehearseMigration(); }
        finally { await isolated.DisposeAsync(); }
    }
    private async Task RehearseMigration()
    {
        var s = await Setup(); var original = await Full(s.Grant, s.Lease, s.Item, "Receipt", "10");
        await using (var db = fixture.Db()) {
            db.Database.SetCommandTimeout(30); var before = await db.StockLedger.AsNoTracking().SingleAsync(x => x.StockPostingId == original);
            var migrator = db.GetService<IMigrator>();
            if(db.Database.IsNpgsql()) {
                // PG starts at its reviewed full catalog; never apply historical SQL Server DDL.
                await Assert.ThrowsAsync<InvalidOperationException>(()=>migrator.MigrateAsync("20261028161040_LeaseTerminationUnitRetirement"));
                var retained=await db.StockLedger.AsNoTracking().SingleAsync(x=>x.StockPostingId==original);
                Assert.Equal(before.Id,retained.Id);Assert.Equal(before.Delta,retained.Delta);
                Assert.Equal(10,await Balance(s.Grant));Assert.True(await PostgresMigrations.IsReadyAsync(db,default));
            } else {
            await migrator.MigrateAsync("20261028161040_LeaseTerminationUnitRetirement");
            // The database now has the actual P0 full-post schema and populated history.
            var p0 = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [StockPostings] WHERE [Id]={original}").SingleAsync(); Assert.Equal(1, p0);
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            var after = await db.StockLedger.AsNoTracking().SingleAsync(x => x.StockPostingId == original); Assert.Equal(before.Id, after.Id); Assert.Equal(before.Delta, after.Delta);
            var posting = await db.StockPostings.SingleAsync(x => x.Id == original); Assert.Equal(original, posting.FulfillmentId); Assert.True(posting.IsFullRequest);
            var line = await db.StockRequestLines.SingleAsync(x => x.Id == after.RequestLineId); Assert.Equal(10, line.FulfilledQuantity); Assert.Equal(10, await Balance(s.Grant));
        }
        }
        using var manager = await fixture.Login("manager@example.test"); await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{original}/reverse", new { reason = "Reverse original P0 posting after upgrade", reversalConfirmed = true }, key: Guid.NewGuid()));
        await using var check = fixture.Db(); check.Database.SetCommandTimeout(30);
        if(check.Database.IsNpgsql()) {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>check.GetService<IMigrator>().MigrateAsync("20261028161040_LeaseTerminationUnitRetirement"));
            Assert.True(await PostgresMigrations.IsReadyAsync(check,default));Assert.Equal(0,await Balance(s.Grant));return;
        }
        await Assert.ThrowsAnyAsync<Exception>(() => check.GetService<IMigrator>().MigrateAsync("20261028161040_LeaseTerminationUnitRetirement"));
        Assert.Contains("20261028163914_StockCorrectionsPartialFulfillment", await check.Database.GetAppliedMigrationsAsync());
        // Later empty migrations can roll back before the WP14 data-preservation guard refuses its own down step.
        await check.GetService<IMigrator>().MigrateAsync(); Assert.Empty(await check.Database.GetPendingMigrationsAsync()); Assert.Equal(0, await Balance(s.Grant));
    }
    [Fact]
    public async Task Pending_zero_count_blocks_clearance_and_retirement_until_cancelled()
    {
        var s = await Setup(); using var clerk = await fixture.Login("clerk@example.test"); using var manager = await fixture.Login("manager@example.test");
        var count = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/counts", new { organizationId = DemoIdentitySeed.OrganizationA, leaseUnitId = s.Grant, itemId = s.Item, expectedQuantity = "0", countedQuantity = "0", reason = "Physical count in progress" }, key: Guid.NewGuid()))).GetProperty("result");
        var lease = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/leases/{s.Lease}")); var clear = new { reason = "Try clearance during count", inspectionConfirmed = true };
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", clear, lease.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode);
        await Ok(await clerk.Send(HttpMethod.Post, $"/api/v1/stock/counts/{count.GetProperty("resourceId").GetGuid()}/cancel", new { reason = "Cancel observation" }, count.GetProperty("version").GetString(), Guid.NewGuid()));
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/physical-clearance", clear, lease.GetProperty("version").GetString(), Guid.NewGuid()));
    }
    [Fact]
    public async Task Two_partial_postings_with_same_expected_request_version_cannot_both_exceed_remaining()
    {
        var s = await Setup(); var r = await Approved(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var one = await fixture.Login("clerk@example.test"); using var two = await fixture.Login("clerk@example.test");
        object Body() => new { fulfillmentId = Guid.NewGuid(), reason = "Observed seven", lines = new[] { new { requestLineId = r.Line, quantity = "7" } } };
        var path = $"/api/v1/stock-requests/{r.Id}/fulfillments"; var results = await Task.WhenAll(one.Send(HttpMethod.Post, path, Body(), r.Version, Guid.NewGuid()), two.Send(HttpMethod.Post, path, Body(), r.Version, Guid.NewGuid()));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.PreconditionFailed); Assert.Equal(7, await Balance(s.Grant));
    }
    [Fact]
    public async Task Termination_stops_positive_reversal_and_transfers_but_physical_counts_can_reconcile_custody()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); var issue = await Full(s.Grant, s.Lease, s.Item, "Issue", "2"); using var manager = await fixture.Login("manager@example.test"); using var clerk = await fixture.Login("clerk@example.test");
        var lease = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/leases/{s.Lease}")); await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/terminate", new { reason = "End term", terminationConfirmed = true }, lease.GetProperty("version").GetString(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/stock/postings/{issue}/reverse", new { reason = "Cannot resurrect inbound after termination", reversalConfirmed = true }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "1"), key: Guid.NewGuid())).StatusCode);
        var count = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/counts", new { organizationId = DemoIdentitySeed.OrganizationA, leaseUnitId = s.Grant, itemId = s.Item, expectedQuantity = "8", countedQuantity = "9", reason = "Actual goods found during clearance" }, key: Guid.NewGuid()))).GetProperty("result");
        await Ok(await manager.Send(HttpMethod.Post, $"/api/v1/stock/counts/{count.GetProperty("resourceId").GetGuid()}/approve", new { reason = "Independent physical recount" }, count.GetProperty("version").GetString(), Guid.NewGuid())); Assert.Equal(9, await Balance(s.Grant));
    }
    [Fact]
    public async Task Transfer_audit_fault_rolls_back_both_legs_ledger_and_saved_command()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10"); var id = Guid.NewGuid(); var key = Guid.NewGuid();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => { services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectTransferAudit()).Options)); }));
        using var clerk = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await clerk.RefreshCsrf(); await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "clerk@example.test", password = DemoIdentitySeed.Password })); await clerk.RefreshCsrf();
        Assert.Equal(HttpStatusCode.InternalServerError, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "4", id), key: key)).StatusCode);
        Assert.Equal(10, await Balance(s.Grant)); Assert.Equal(0, await Balance(s.Other)); await using var db = fixture.Db(); Assert.False(await db.StockPostings.AnyAsync(x => x.Id == id)); Assert.False(await db.StockLedger.AnyAsync(x => x.StockPostingId == id)); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
    }
    private sealed class RejectTransferAudit : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action == "StockTransferred")) throw new InvalidOperationException("Synthetic transfer audit fault"); return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Transfers_between_two_large_leases_lock_all_parents_without_the_single_booking_limit()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10");
        await using (var db = fixture.Db()) {
            var ids = new[] { s.Grant, s.Other }; var grants = await db.LeaseUnits.Where(x => ids.Contains(x.Id)).ToListAsync();
            foreach (var grant in grants) {
                var lease = await db.Leases.SingleAsync(x => x.Id == grant.LeaseId); var template = await db.RentableUnits.SingleAsync(x => x.Id == grant.UnitId);
                for (var n = 0; n < 10; n++) {
                    var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = template.FacilityId, ZoneId = template.ZoneId, Code = "MULTI-" + Guid.NewGuid().ToString("N"), AreaM2 = 10, Published = true };
                    var line = new WarehouseRent.Infrastructure.Rentals.BookingLine { Id = Guid.NewGuid(), BookingId = lease.BookingId, UnitId = unit.Id, StartDate = lease.StartDate, EndDate = lease.EndDate };
                    var allocation = new SpaceAllocation { Id = Guid.NewGuid(), UnitId = unit.Id, BookingLineId = line.Id, StartDate = lease.StartDate, EndDate = lease.EndDate, State = "Confirmed" };
                    db.RentableUnits.Add(unit); db.BookingLines.Add(line); db.SpaceAllocations.Add(allocation); db.LeaseUnits.Add(new WarehouseRent.Infrastructure.Rentals.LeaseUnit { Id = Guid.NewGuid(), LeaseId = lease.Id, UnitId = unit.Id, InitialAllocationId = allocation.Id });
                }
            }
            await db.SaveChangesAsync();
        }
        using var clerk = await fixture.Login("clerk@example.test"); await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "3"), key: Guid.NewGuid())); Assert.Equal(7, await Balance(s.Grant)); Assert.Equal(3, await Balance(s.Other));
    }
    [Fact]
    public async Task Concurrent_count_approval_posts_once_and_observation_is_immutable()
    {
        var s = await Setup(); using var clerk = await fixture.Login("clerk@example.test"); using var manager = await fixture.Login("manager@example.test"); using var other = await fixture.Login("operator@example.test");
        var count = (await Ok(await clerk.Send(HttpMethod.Post, "/api/v1/stock/counts", new { organizationId = DemoIdentitySeed.OrganizationA, leaseUnitId = s.Grant, itemId = s.Item, expectedQuantity = "0", countedQuantity = "3", reason = "Found goods" }, key: Guid.NewGuid()))).GetProperty("result"); var id = count.GetProperty("resourceId").GetGuid(); var version = count.GetProperty("version").GetString(); var body = new { reason = "Independent approval" };
        var results = await Task.WhenAll(manager.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/approve", body, version, Guid.NewGuid()), other.Send(HttpMethod.Post, $"/api/v1/stock/counts/{id}/approve", body, version, Guid.NewGuid())); Assert.Equal((await Ok(results[0])).GetProperty("result").GetRawText(), (await Ok(results[1])).GetProperty("result").GetRawText()); Assert.Equal(3, await Balance(s.Grant));
        await using var db = fixture.Db(); Assert.Equal(1, await db.StockPostings.CountAsync(x => x.StockCountId == id)); var observation = await db.StockCounts.SingleAsync(x => x.Id == id); observation.CountedQuantity = 99; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0001")]
    [InlineData("1000000000000000")]
    public async Task Transfer_rejects_invalid_quantity_without_any_movement(string quantity)
    {
        var s = await Setup(); using var clerk = await fixture.Login("clerk@example.test"); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, quantity), key: Guid.NewGuid())).StatusCode); Assert.Equal(0, await Balance(s.Grant)); Assert.Equal(0, await Balance(s.Other));
    }
    [Fact]
    public async Task Partial_replay_rejects_changed_payload_foreign_lines_stale_versions_and_cancelled_remainder()
    {
        var s = await Setup(); var r = await Approved(s.Grant, s.Lease, s.Item, "Receipt", "10"); using var clerk = await fixture.Login("clerk@example.test"); using var customer = await fixture.Login(); var id = Guid.NewGuid(); var path = $"/api/v1/stock-requests/{r.Id}/fulfillments";
        object Body(string quantity, Guid? fulfillment = null, Guid? line = null) => new { fulfillmentId = fulfillment ?? id, reason = "Observed subset", lines = new[] { new { requestLineId = line ?? r.Line, quantity } } };
        Assert.Equal((HttpStatusCode)428, (await clerk.Send(HttpMethod.Post, path, Body("2"), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clerk.Send(HttpMethod.Post, path, Body("2", line: Guid.NewGuid()), r.Version, Guid.NewGuid())).StatusCode);
        await Ok(await clerk.Send(HttpMethod.Post, path, Body("2"), r.Version, Guid.NewGuid())); Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, path, Body("3"), r.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await clerk.Send(HttpMethod.Post, path, Body("2", Guid.NewGuid()), r.Version, Guid.NewGuid())).StatusCode);
        var read = await Ok(await clerk.Send(HttpMethod.Get, $"/api/v1/stock-requests/{r.Id}")); var version = read.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{r.Id}/reject", new { reason = "Cannot erase posted history" }, version, Guid.NewGuid())).StatusCode);
        await Ok(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{r.Id}/cancel", new { reason = "Cancel unfulfilled remainder" }, version, Guid.NewGuid()));
        read = await Ok(await clerk.Send(HttpMethod.Get, $"/api/v1/stock-requests/{r.Id}")); Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, path, Body("2", Guid.NewGuid()), read.GetProperty("version").GetString(), Guid.NewGuid())).StatusCode); Assert.Equal(2, await Balance(s.Grant));
    }
    [Fact]
    public async Task Destination_overflow_reverts_the_source_decrease_and_leaves_no_document()
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "1"); using var clerk = await fixture.Login("clerk@example.test");
        await using (var db = fixture.Db()) { db.StockBalances.Add(new WarehouseRent.Infrastructure.Stock.StockBalance { OrganizationId = DemoIdentitySeed.OrganizationA, ItemId = s.Item, LeaseUnitId = s.Other, Quantity = 999999999999999.999m }); await db.SaveChangesAsync(); }
        var id = Guid.NewGuid(); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "1", id), key: Guid.NewGuid())).StatusCode); Assert.Equal(1, await Balance(s.Grant)); await using var check = fixture.Db(); Assert.False(await check.StockPostings.AnyAsync(x => x.Id == id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Termination_waits_for_inflight_transfer_or_partial_posting_before_changing_custody(bool partial)
    {
        var s = await Setup(); await Full(s.Grant, s.Lease, s.Item, "Receipt", "10");
        var request = await Approved(s.Grant, s.Lease, s.Item, "Receipt", "4");
        var gate = new StockRaceGate(partial ? "StockPosted" : "StockTransferred");
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<AppDbContext>(); services.AddScoped(sp => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options())
                .AddInterceptors(new PauseStockSave(gate), new ObserveTerminationLock(gate, sp.GetRequiredService<IHttpContextAccessor>())).Options));
        }));
        async Task<BrowserSession> Login(string user)
        {
            var session = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await session.RefreshCsrf();
            await Ok(await session.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = user, password = DemoIdentitySeed.Password })); await session.RefreshCsrf(); return session;
        }
        using var clerk = await Login("clerk@example.test"); using var manager = await Login("manager@example.test");
        var lease = await Ok(await manager.Send(HttpMethod.Get, $"/api/v1/leases/{s.Lease}"));
        Task<HttpResponseMessage>? movement = null; Task<HttpResponseMessage>? termination = null;
        try {
            movement = partial ? clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{request.Id}/fulfillments", new { fulfillmentId = Guid.NewGuid(), reason = "Paused inbound posting", lines = new[] { new { requestLineId = request.Line, quantity = "4" } } }, request.Version, Guid.NewGuid())
                : clerk.Send(HttpMethod.Post, "/api/v1/stock/transfers", Transfer(s.Grant, s.Other, s.Item, "4"), key: Guid.NewGuid());
            await gate.StockWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
            termination = manager.Send(HttpMethod.Post, $"/api/v1/leases/{s.Lease}/terminate", new { reason = "Terminate behind in-flight movement", terminationConfirmed = true }, lease.GetProperty("version").GetString(), Guid.NewGuid());
            await gate.TerminationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(termination.IsCompleted);
        } finally { gate.Release.TrySetResult(); }
        await Ok(await movement!); await Ok(await termination!);
        Assert.Equal(partial ? 14 : 6, await Balance(s.Grant)); Assert.Equal(partial ? 0 : 4, await Balance(s.Other));
        await using var db = fixture.Db(); Assert.NotNull((await db.Leases.SingleAsync(x => x.Id == s.Lease)).TerminatedAt); Assert.Null((await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Grant)).ClearedAt);
    }
    private sealed class StockRaceGate(string action)
    {
        public string Action { get; } = action;
        public TaskCompletionSource StockWritten { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource TerminationAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class PauseStockSave(StockRaceGate gate) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action == gate.Action)) { gate.StockWritten.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
            return result;
        }
    }
    private sealed class ObserveTerminationLock(StockRaceGate gate, IHttpContextAccessor accessor) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (accessor.HttpContext?.Request.Path.Value?.EndsWith("/terminate", StringComparison.Ordinal) == true && (command.CommandText.Contains("[RentableUnits] WITH (UPDLOCK,HOLDLOCK)", StringComparison.Ordinal) || command.CommandText.Contains("\"khospace\".\"RentableUnits\"", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))) gate.TerminationAttempted.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
}
