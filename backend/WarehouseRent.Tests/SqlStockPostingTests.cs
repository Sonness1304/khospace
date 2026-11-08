using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlStockPostingTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<(Guid Id, string Version)> Identity(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var result = data.RootElement.GetProperty("result");
        return (result.GetProperty("resourceId").GetGuid(), result.TryGetProperty("version", out var version) ? version.GetString()! : "");
    }
    private async Task<(Guid Grant, Guid Lease, Guid Item, BrowserSession Customer, BrowserSession Clerk)> Setup()
    {
        var customer = await fixture.Login(); var clerk = await fixture.Login("clerk@example.test");
        var item = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "POST-" + Guid.NewGuid().ToString("N"), description = "Synthetic goods", baseUom = "box", reason = "SQL posting fixture" }, key: Guid.NewGuid()));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today, today.AddDays(10));
        await using var db = fixture.Db(); var unit = await db.LeaseUnits.SingleAsync(x => x.Id == grant);
        foreach (var old in await db.UnitOccupancies.Where(x => x.UnitId == unit.UnitId && x.ClearedAt == null).ToListAsync()) old.ClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.UnitOccupancies.Add(new UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = today.AddDays(10), HandedOverAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        return (grant, unit.LeaseId, item.Id, customer, clerk);
    }
    private async Task<(Guid Id, string Version, object Body)> Approved(Guid grant, Guid lease, Guid item, BrowserSession customer, BrowserSession clerk, string type, string quantity)
    {
        var draft = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = lease, leaseUnitId = grant, type, lines = new[] { new { itemId = item, quantity } }, reason = "Explicit customer instruction" }, key: Guid.NewGuid()));
        var submitted = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/submit", new { reason = "Customer authorization" }, draft.Version, Guid.NewGuid()));
        var approved = await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/approve", new { reason = "Clerk review" }, submitted.Version, Guid.NewGuid()));
        using var read = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{draft.Id}")).Content.ReadAsStringAsync());
        var line = read.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid();
        return (draft.Id, approved.Version, new { reason = "Complete observed quantity", lines = new[] { new { requestLineId = line, quantity } } });
    }
    [Fact]
    public async Task Concurrent_issue_seven_plus_seven_from_ten_yields_three_and_reconciles_once()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk; using var secondClerk = await fixture.Login("clerk@example.test");
        var receipt = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "10");
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{receipt.Id}/post", receipt.Body, receipt.Version, Guid.NewGuid()));
        var one = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Issue", "7"); var two = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Issue", "7");
        var results = await Task.WhenAll(clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{one.Id}/post", one.Body, one.Version, Guid.NewGuid()), secondClerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{two.Id}/post", two.Body, two.Version, Guid.NewGuid()));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        using var balance = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock/balances?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={setup.Grant}")).Content.ReadAsStringAsync());
        Assert.Equal("3", balance.RootElement.GetProperty("items")[0].GetProperty("quantity").GetString());
        using var ledger = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock/ledger?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={setup.Grant}")).Content.ReadAsStringAsync());
        var rows = ledger.RootElement.GetProperty("items").EnumerateArray().ToArray(); Assert.Equal(2, rows.Length); Assert.Equal(3m, rows.Sum(x => decimal.Parse(x.GetProperty("delta").GetString()!, System.Globalization.CultureInfo.InvariantCulture)));
        using var foreign = await fixture.Login("admin.b@example.test"); Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/stock/balances?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={setup.Grant}")).StatusCode);
    }
    [Fact]
    public async Task Missing_balance_receipts_race_and_same_source_different_keys_never_double_post()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk; using var second = await fixture.Login("clerk@example.test");
        var one = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "5"); var two = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "8");
        var results = await Task.WhenAll(clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{one.Id}/post", one.Body, one.Version, Guid.NewGuid()), second.Send(HttpMethod.Post, $"/api/v1/stock-requests/{two.Id}/post", two.Body, two.Version, Guid.NewGuid()));
        Assert.All(results, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{one.Id}/post", one.Body, one.Version, Guid.NewGuid()));
        using var balance = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock/balances?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={setup.Grant}")).Content.ReadAsStringAsync());
        Assert.Equal("13", balance.RootElement.GetProperty("items")[0].GetProperty("quantity").GetString());
    }
    [Fact]
    public async Task Observed_mismatch_refused_and_inventory_blocks_physical_clearance_until_zero_and_no_pending()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk; using var manager = await fixture.Login("manager@example.test");
        var receipt = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "10");
        var mismatch = JsonSerializer.SerializeToElement(receipt.Body); var lineId = mismatch.GetProperty("lines")[0].GetProperty("requestLineId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{receipt.Id}/post", new { reason = "Only eight arrived", lines = new[] { new { requestLineId = lineId, quantity = "8" } } }, receipt.Version, Guid.NewGuid())).StatusCode);
        using var read = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/leases/{setup.Lease}")).Content.ReadAsStringAsync()); var version = read.RootElement.GetProperty("version").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{setup.Lease}/physical-clearance", new { reason = "Pending inbound blocks", inspectionConfirmed = true }, version, Guid.NewGuid())).StatusCode);
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{receipt.Id}/post", receipt.Body, receipt.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{setup.Lease}/physical-clearance", new { reason = "Nonzero stock blocks", inspectionConfirmed = true }, version, Guid.NewGuid())).StatusCode);
        var issue = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Issue", "10");
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/post", issue.Body, issue.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/leases/{setup.Lease}/physical-clearance", new { reason = "Empty, no requests, inspected", inspectionConfirmed = true }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = setup.Lease, leaseUnitId = setup.Grant, type = "Receipt", lines = new[] { new { itemId = setup.Item, quantity = "1" } }, reason = "Cannot recreate custody after clearance" }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Multi_line_insufficient_issue_rolls_back_balance_ledger_audit_and_receipt()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk;
        var stock = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "10"); await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{stock.Id}/post", stock.Body, stock.Version, Guid.NewGuid()));
        var emptyItem = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "EMPTY-" + Guid.NewGuid().ToString("N"), description = "No stock", baseUom = "box", reason = "Atomic posting test" }, key: Guid.NewGuid()));
        var draft = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = setup.Lease, leaseUnitId = setup.Grant, type = "Issue", lines = new[] { new { itemId = setup.Item, quantity = "7" }, new { itemId = emptyItem.Id, quantity = "1" } }, reason = "Two-line customer instruction" }, key: Guid.NewGuid()));
        var submit = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/submit", new { reason = "Two-line instruction" }, draft.Version, Guid.NewGuid()));
        var approve = await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/approve", new { reason = "Review without posting" }, submit.Version, Guid.NewGuid()));
        using var read = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{draft.Id}")).Content.ReadAsStringAsync());
        var observations = read.RootElement.GetProperty("lines").EnumerateArray().Select(x => new { requestLineId = x.GetProperty("id").GetGuid(), quantity = x.GetProperty("quantity").GetString() }).ToArray(); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/post", new { reason = "Observed complete issue", lines = observations }, approve.Version, key)).StatusCode);
        await using var db = fixture.Db(); Assert.Equal(10, await db.StockBalances.Where(x => x.LeaseUnitId == setup.Grant && x.ItemId == setup.Item).Select(x => x.Quantity).SingleAsync());
        Assert.False(await db.StockPostings.AnyAsync(x => x.StockRequestId == draft.Id)); Assert.False(await db.SecurityAudits.AnyAsync(x => x.AggregateId == draft.Id && x.Action == "StockPosted")); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.Equal("Approved", await db.StockRequests.Where(x => x.Id == draft.Id).Select(x => x.State).SingleAsync());
        var ledger = await db.StockLedger.SingleAsync(x => x.LeaseUnitId == setup.Grant); ledger.Delta = 99;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task New_pending_request_and_clearance_race_cannot_both_commit()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk; using var manager = await fixture.Login("manager@example.test");
        using var lease = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/leases/{setup.Lease}")).Content.ReadAsStringAsync());
        var version = lease.RootElement.GetProperty("version").GetString();
        var outcomes = await Task.WhenAll(manager.Send(HttpMethod.Post, $"/api/v1/leases/{setup.Lease}/physical-clearance", new { reason = "Empty unit inspected", inspectionConfirmed = true }, version, Guid.NewGuid()),
            customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = setup.Lease, leaseUnitId = setup.Grant, type = "Receipt", lines = new[] { new { itemId = setup.Item, quantity = "1" } }, reason = "Concurrent customer inbound" }, key: Guid.NewGuid()));
        Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(outcomes, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.Db(); var cleared = await db.Leases.AnyAsync(x => x.Id == setup.Lease && x.State == "SettlementPending");
        var pending = await db.StockRequests.AnyAsync(x => x.LeaseId == setup.Lease && (x.State == "Draft" || x.State == "Submitted" || x.State == "Approved")); Assert.False(cleared && pending);
    }

    [Fact]
    public async Task Posting_revalidates_term_and_requires_new_manager_authorization_for_ended_issue()
    {
        var setup = await Setup(); using var customer = setup.Customer; using var clerk = setup.Clerk; using var manager = await fixture.Login("manager@example.test");
        var received = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "10");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{received.Id}/post", received.Body, received.Version, Guid.NewGuid())).StatusCode);
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{received.Id}/post", received.Body, received.Version, Guid.NewGuid()));
        var issue = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Issue", "10");
        var lateInbound = await Approved(setup.Grant, setup.Lease, setup.Item, customer, clerk, "Receipt", "1");
        await using (var db = fixture.Db())
        {
            // Synthetic date advancement without changing authoritative SQL time or server configuration.
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var lease = await db.Leases.SingleAsync(x => x.Id == setup.Lease); lease.StartDate = today.AddDays(-10); lease.EndDate = today;
            (await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == setup.Grant)).ScheduledEnd = today; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{lateInbound.Id}/post", lateInbound.Body, lateInbound.Version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/post", issue.Body, issue.Version, Guid.NewGuid())).StatusCode);
        var authorization = await Identity(await manager.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/approve", new { reason = "Authorize remaining goods release after term" }, issue.Version, Guid.NewGuid()));
        await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/post", issue.Body, authorization.Version, Guid.NewGuid()));
    }

}
