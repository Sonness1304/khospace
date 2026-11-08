using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlStockRequestTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<(Guid Id, string Version)> Identity(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var data = result.RootElement.GetProperty("result");
        return (data.GetProperty("resourceId").GetGuid(), data.GetProperty("version").GetString()!);
    }
    [Fact]
    public async Task Customer_items_and_request_lifecycle_are_scoped_and_never_post_stock()
    {
        using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        var item = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "DEMO-SKU", description = "Synthetic cartons", baseUom = "carton", reason = "Synthetic item setup" }, key: Guid.NewGuid()));
        var sameSku = await Identity(await foreign.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationB, sku = "DEMO-SKU", description = "Other customer cartons", baseUom = "carton", reason = "Independent SKU" }, key: Guid.NewGuid()));
        Assert.NotEqual(item.Id, sameSku.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today, today.AddDays(10));
        await using var db = fixture.Db(); var unit = await db.LeaseUnits.SingleAsync(x => x.Id == grant);
        foreach (var old in await db.UnitOccupancies.Where(x => x.UnitId == unit.UnitId && x.ClearedAt == null).ToListAsync()) old.ClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.UnitOccupancies.Add(new WarehouseRent.Infrastructure.Facilities.UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = today.AddDays(10), HandedOverAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        object Request(Guid itemId) => new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = unit.LeaseId, leaseUnitId = grant, type = "Receipt", lines = new[] { new { itemId, quantity = "100.000" } }, reason = "Customer inbound instruction" };
        Assert.Equal(HttpStatusCode.NotFound, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request(sameSku.Id), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = unit.LeaseId, leaseUnitId = grant, type = "Receipt", lines = new object?[] { null }, reason = "Malformed null child" }, key: Guid.NewGuid())).StatusCode);
        var request = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request(item.Id), key: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/stock-requests/{request.Id}")).StatusCode);
        var submitted = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{request.Id}/submit", new { reason = "Explicit customer submission" }, request.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{request.Id}/approve", new { reason = "Forged approval" }, submitted.Version, Guid.NewGuid())).StatusCode);
        using var clerk = await fixture.Login("clerk@example.test");
        var approvalKey = Guid.NewGuid();
        var approved = await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{request.Id}/approve", new { reason = "Assigned clerk approval" }, submitted.Version, approvalKey));
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Get, $"/api/v1/commands/{approvalKey}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clerk.Send(HttpMethod.Get, $"/api/v1/commands/{approvalKey}?scopeId={DemoIdentitySeed.FacilityA}")).StatusCode);
        Assert.NotEqual(submitted.Version, approved.Version);
        var read = await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{request.Id}");
        Assert.Contains("Approved", await read.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Fulfilled", await read.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Discrepancy_replacement_requires_customer_instruction_and_original_stays_unposted()
    {
        using var customer = await fixture.Login(); using var clerk = await fixture.Login("clerk@example.test");
        var item = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "MISMATCH", description = "Counted boxes", baseUom = "box", reason = "Test setup" }, key: Guid.NewGuid()));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitB, today, today.AddDays(10));
        await using var db = fixture.Db(); var unit = await db.LeaseUnits.SingleAsync(x => x.Id == grant);
        foreach (var old in await db.UnitOccupancies.Where(x => x.UnitId == unit.UnitId && x.ClearedAt == null).ToListAsync()) old.ClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.UnitOccupancies.Add(new WarehouseRent.Infrastructure.Facilities.UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = today.AddDays(10), HandedOverAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        object Request(string quantity, Guid? replacesRequestId = null) => new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = unit.LeaseId, leaseUnitId = grant, type = "Receipt", lines = new[] { new { itemId = item.Id, quantity } }, reason = "Authorized customer quantity", replacesRequestId };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("1.0001"), key: Guid.NewGuid())).StatusCode);
        var original = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("100"), key: Guid.NewGuid()));
        var submitted = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{original.Id}/submit", new { reason = "Request 100 boxes" }, original.Version, Guid.NewGuid()));
        var rejected = await Identity(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{original.Id}/reject", new { reason = "Only 85 boxes physically presented; request replacement" }, submitted.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("85", original.Id), key: Guid.NewGuid())).StatusCode);
        var replacement = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("85", original.Id), key: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("85", original.Id), key: Guid.NewGuid())).StatusCode);
        var read = await (await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{replacement.Id}")).Content.ReadAsStringAsync();
        Assert.Contains(original.Id.ToString(), read); Assert.Contains("85", read); Assert.Contains("Draft", read);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{original.Id}/submit", new { reason = "Cannot resurrect rejected" }, rejected.Version, Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Ended_term_refuses_inbound_but_manager_can_authorize_clearance_outbound()
    {
        using var customer = await fixture.Login(); using var clerk = await fixture.Login("clerk@example.test"); using var manager = await fixture.Login("manager@example.test");
        var item = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "CLEARANCE", description = "Remaining goods", baseUom = "box", reason = "Test setup" }, key: Guid.NewGuid()));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        // Separate physical unit avoids fixture occupancy from other lifecycle cases.
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, today.AddDays(-10), today);
        await using var db = fixture.Db(); var unit = await db.LeaseUnits.SingleAsync(x => x.Id == grant);
        foreach (var old in await db.UnitOccupancies.Where(x => x.UnitId == unit.UnitId && x.ClearedAt == null).ToListAsync()) old.ClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        foreach (var old in await db.UnitOccupancies.Where(x => x.UnitId == unit.UnitId && x.ClearedAt == null).ToListAsync()) old.ClearedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.UnitOccupancies.Add(new WarehouseRent.Infrastructure.Facilities.UnitOccupancy { Id = Guid.NewGuid(), UnitId = unit.UnitId, LeaseUnitId = grant, OrganizationId = DemoIdentitySeed.OrganizationA, ScheduledEnd = today, HandedOverAt = DateTimeOffset.UtcNow.AddDays(-10) }); await db.SaveChangesAsync();
        object Request(string type) => new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = unit.LeaseId, leaseUnitId = grant, type, lines = new[] { new { itemId = item.Id, quantity = "3" } }, reason = "Clearance customer instruction" };
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("Receipt"), key: Guid.NewGuid())).StatusCode);
        var issue = await Identity(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", Request("Issue"), key: Guid.NewGuid()));
        var submitted = await Identity(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/submit", new { reason = "Release remaining goods" }, issue.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/approve", new { reason = "Clerk lacks clearance authority" }, submitted.Version, Guid.NewGuid())).StatusCode);
        await Identity(await manager.Send(HttpMethod.Post, $"/api/v1/stock-requests/{issue.Id}/approve", new { reason = "Manager approves post-term release" }, submitted.Version, Guid.NewGuid()));
        Assert.Contains("clearanceAuthorizedAt", await (await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{issue.Id}")).Content.ReadAsStringAsync());
    }

}
