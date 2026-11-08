using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlRenewalPreviewTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Preview_uses_added_period_quote_without_changing_lease_or_capacity_and_rechecks_conflicts()
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(20); var end = start.AddDays(3);
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, start, end);
        await using var db = fixture.Db(); var leaseId = await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.LeaseId).SingleAsync();
        var lease = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == leaseId);
        using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var clerk = await fixture.Login("clerk@example.test");
        var quoteResponse = await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = DemoIdentitySeed.OrganizationA, facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { SqlFacilitiesTests.UnitA }, startDate = end, endDate = end.AddDays(2) }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, quoteResponse.StatusCode); using var quoteDoc = JsonDocument.Parse(await quoteResponse.Content.ReadAsStringAsync());
        var quote = quoteDoc.RootElement.GetProperty("result"); var body = new { quoteId = quote.GetProperty("resourceId").GetGuid(), expectedQuoteVersion = quote.GetProperty("version").GetString(), expectedLeaseVersion = Convert.ToBase64String(lease.Version) };
        var path = $"/api/v1/leases/{leaseId}/renewal-preview";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, path, body)).StatusCode);
        var preview = await customer.Send(HttpMethod.Post, path, body); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var first = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()); Assert.True(first.RootElement.GetProperty("canApply").GetBoolean()); Assert.False(first.RootElement.GetProperty("capacityReserved").GetBoolean()); Assert.Contains(grant.ToString(), await preview.Content.ReadAsStringAsync());
        Assert.Equal("RetainExistingDeposit", first.RootElement.GetProperty("depositPolicy").GetString()); Assert.Equal("20", first.RootElement.GetProperty("requiredDeposit").GetString()); Assert.Equal("0", first.RootElement.GetProperty("additionalDeposit").GetString());
        var conflictLine = await fixture.SeedAllocationOwner(SqlFacilitiesTests.UnitA, end, end.AddDays(2));
        await using (var conflictDb = fixture.Db()) { var allocation = new WarehouseRent.Infrastructure.Facilities.SpaceAllocation { Id = Guid.NewGuid(), UnitId = SqlFacilitiesTests.UnitA, BookingLineId = conflictLine, StartDate = end, EndDate = end.AddDays(2), State = "Confirmed" }; conflictDb.SpaceAllocations.Add(allocation); await conflictDb.SaveChangesAsync(); }
        var conflict = await customer.Send(HttpMethod.Post, path, body); Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        using var blocked = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()); Assert.False(blocked.RootElement.GetProperty("canApply").GetBoolean());
        Assert.Equal(end, (await db.Leases.AsNoTracking().SingleAsync(x => x.Id == leaseId)).EndDate); Assert.Equal(0, await db.LeaseVersions.CountAsync(x => x.LeaseId == leaseId));
        var stale = await customer.Send(HttpMethod.Post, path, new { body.quoteId, body.expectedQuoteVersion, expectedLeaseVersion = "stale" }); Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }
}
