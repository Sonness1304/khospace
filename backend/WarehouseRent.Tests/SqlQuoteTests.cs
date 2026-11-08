using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlQuoteTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static object Input(Guid unit, Guid? organization = null) => new { facilityId = DemoIdentitySeed.FacilityA,
        organizationId = organization ?? DemoIdentitySeed.OrganizationA, unitIds = new[] { unit }, startDate = "2028-01-31", endDate = "2028-02-02" };
    private static async Task<Guid> QuoteId(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid();
    }
    [Fact]
    public async Task Monthly_quote_is_immutable_idempotent_scoped_and_never_reserves_capacity()
    {
        using var customer = await fixture.Login(); var key = Guid.NewGuid();
        var response = await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitA), key: key);
        var id = await QuoteId(response);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitA), key: key)).Content.ReadAsStringAsync());
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await customer.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).Content.ReadAsStringAsync());
        var before = await (await customer.Send(HttpMethod.Get, $"/api/v1/quotes/{id}")).Content.ReadAsStringAsync();
        using (var quote = JsonDocument.Parse(before))
        {
            Assert.Equal("2122", quote.RootElement.GetProperty("rentalSubtotal").GetString());
            Assert.Equal("424", quote.RootElement.GetProperty("requiredDeposit").GetString());
            Assert.Equal(2, quote.RootElement.GetProperty("lines").GetArrayLength());
            Assert.False(quote.RootElement.GetProperty("reservation").GetBoolean());
            var created = quote.RootElement.GetProperty("createdAt").GetDateTimeOffset();
            Assert.Equal(TimeSpan.FromHours(24), quote.RootElement.GetProperty("expiresAt").GetDateTimeOffset() - created);
        }
        using var manager = await fixture.Login("operator@example.test");
        using var catalog = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog")).Content.ReadAsStringAsync());
        var version = catalog.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == SqlFacilitiesTests.UnitA).GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{SqlFacilitiesTests.UnitA}/rates", new { dailyRate = "202", salesValidFrom = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), salesValidTo = (DateOnly?)null, reason = "Synthetic quote immutability" }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(before, await (await customer.Send(HttpMethod.Get, $"/api/v1/quotes/{id}")).Content.ReadAsStringAsync());
        var next = await QuoteId(await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitA), key: Guid.NewGuid()));
        Assert.Contains("\"rentalSubtotal\":\"4242\"", await (await customer.Send(HttpMethod.Get, $"/api/v1/quotes/{next}")).Content.ReadAsStringAsync());
        using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/quotes/{id}")).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.SpaceAllocations.AnyAsync());
    }
    [Fact]
    public async Task Rate_is_selected_by_quote_creation_day_not_future_service_dates()
    {
        using var manager = await fixture.Login("operator@example.test");
        using var catalog = JsonDocument.Parse(await (await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog")).Content.ReadAsStringAsync());
        var version = catalog.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == SqlFacilitiesTests.UnitB).GetProperty("version").GetString()!;
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(1);
        Assert.Equal(HttpStatusCode.OK, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{SqlFacilitiesTests.UnitB}/rates", new { dailyRate = "10000", salesValidFrom = future, salesValidTo = (DateOnly?)null, reason = "Synthetic future tariff" }, version, Guid.NewGuid())).StatusCode);
        using var customer = await fixture.Login();
        var id = await QuoteId(await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitB), key: Guid.NewGuid()));
        using var quote = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/quotes/{id}")).Content.ReadAsStringAsync());
        Assert.All(quote.RootElement.GetProperty("lines").EnumerateArray(), line => Assert.Equal("5000", line.GetProperty("dailyRate").GetString()));
    }
    [Fact]
    public async Task Sales_quote_and_receipt_readback_require_current_matching_facility_role()
    {
        using var sales = await fixture.Login("sales@example.test"); var key = Guid.NewGuid();
        var id = await QuoteId(await sales.Send(HttpMethod.Post, "/api/v1/quotes", Input(Guid.Parse("a0000003-0000-4000-8000-000000000003")), key: key));
        Assert.Equal(HttpStatusCode.OK, (await sales.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        await using var db = fixture.Db();
        var grant = await db.StaffFacilityScopes.SingleAsync(x => x.UserId == "sales@example.test");
        db.StaffFacilityScopes.Remove(grant);
        var replacement = new StaffFacilityScope { UserId = grant.UserId, FacilityId = grant.FacilityId, StaffRole = "Clerk" };
        db.StaffFacilityScopes.Add(replacement); await db.SaveChangesAsync();
        try {
            Assert.Equal(HttpStatusCode.NotFound, (await sales.Send(HttpMethod.Get, $"/api/v1/quotes/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await sales.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        } finally { db.StaffFacilityScopes.Remove(replacement); db.StaffFacilityScopes.Add(grant); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Forged_customer_hidden_foreign_or_missing_rate_units_are_denied()
    {
        using var customer = await fixture.Login();
        using var member = await fixture.Login("member.a@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitA, DemoIdentitySeed.OrganizationB), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Send(HttpMethod.Post, "/api/v1/quotes", Input(SqlFacilitiesTests.UnitA), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(Guid.Parse("a0000004-0000-4000-8000-000000000004")), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(Guid.Parse("b0000001-0000-4000-8000-000000000001")), key: Guid.NewGuid())).StatusCode);
        using var manager = await fixture.Login("operator@example.test");
        var created = await manager.Send(HttpMethod.Post, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/units", new { zoneId = Guid.Parse("a0000000-0000-4000-8000-000000000000"), code = "NO-RATE", areaM2 = "20", published = true, reason = "Synthetic missing price" }, key: Guid.NewGuid());
        using var newUnit = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(HttpMethod.Post, "/api/v1/quotes", Input(newUnit.RootElement.GetProperty("result").GetProperty("resourceId").GetGuid()), key: Guid.NewGuid())).StatusCode);
    }
}
