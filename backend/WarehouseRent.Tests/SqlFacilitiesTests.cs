using System.Net;
using System.Text.Json;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlFacilitiesTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    public static readonly Guid UnitA = Guid.Parse("a0000001-0000-4000-8000-000000000001");
    public static readonly Guid UnitB = Guid.Parse("a0000002-0000-4000-8000-000000000002");

    [Fact]
    public async Task Rate_change_versions_unit_closes_previous_sales_window_and_rejects_overlap()
    {
        using var manager = await fixture.Login("manager@example.test");
        var catalog = await manager.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog");
        using var data = JsonDocument.Parse(await catalog.Content.ReadAsStringAsync());
        var version = data.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == UnitA).GetProperty("version").GetString()!;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var input = new { dailyRate = "102.0001", salesValidFrom = today, salesValidTo = (DateOnly?)null, reason = "Synthetic tariff revision" };
        var response = await manager.Send(HttpMethod.Post, $"/api/v1/units/{UnitA}/rates", input, version, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var next = result.RootElement.GetProperty("result").GetProperty("version").GetString()!;
        Assert.NotEqual(version, next);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{UnitA}/rates", input with { salesValidFrom = today.AddDays(1) }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, $"/api/v1/units/{UnitA}/rates", input, next, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Send(HttpMethod.Post, $"/api/v1/facilities/{DemoIdentitySeed.FacilityB}/zones", new { code = "FORGED", reason = "Outside manager scope" }, key: Guid.NewGuid())).StatusCode);
        using var client = fixture.Client();
        Assert.Contains("102.0001", await client.GetStringAsync($"/api/v1/public/facilities/{DemoIdentitySeed.FacilityA}/units"));
    }

    [Fact]
    public async Task Public_catalog_hides_unpublished_units_and_customer_identity()
    {
        using var client = fixture.Client();
        var facilities = await client.GetAsync("/api/v1/public/facilities");
        Assert.Equal(HttpStatusCode.OK, facilities.StatusCode);
        var body = await facilities.Content.ReadAsStringAsync();
        Assert.DoesNotContain("example.test", body);
        var units = await client.GetAsync($"/api/v1/public/facilities/{DemoIdentitySeed.FacilityA}/units");
        Assert.Equal(HttpStatusCode.OK, units.StatusCode);
        using var data = JsonDocument.Parse(await units.Content.ReadAsStringAsync());
        Assert.Equal(3, data.RootElement.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain("A-04", await units.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Availability_validates_dates_area_and_bounds()
    {
        using var client = fixture.Client();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        string Path(DateOnly start, DateOnly end, string min = "0") => $"/api/v1/availability?facilityId={DemoIdentitySeed.FacilityA}&startDate={start:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}&minArea={min}";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Path(today, today))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Path(today.AddDays(-1), today))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Path(today, today.AddDays(366)))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Path(today, today.AddDays(1), "-1"))).StatusCode);
        var available = await client.GetAsync(Path(today, today.AddDays(1), "15"));
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        using var body = JsonDocument.Parse(await available.Content.ReadAsStringAsync());
        Assert.All(body.RootElement.GetProperty("items").EnumerateArray(), row => Assert.True(decimal.Parse(row.GetProperty("areaM2").GetString()!, System.Globalization.CultureInfo.InvariantCulture) >= 15));
    }

    [Fact]
    public async Task Unit_creation_requires_manager_scope_positive_area_and_unique_code()
    {
        using var clerk = await fixture.Login("clerk@example.test");
        using var customer = await fixture.Login();
        using var manager = await fixture.Login("operator@example.test");
        var body = new { zoneId = Guid.Parse("a0000000-0000-4000-8000-000000000000"), code = "A-01", areaM2 = "20.00", published = true, reason = "Synthetic create" };
        var path = $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/units";
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.Send(HttpMethod.Post, path, body with { code = "BAD", areaM2 = "0" }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await manager.Send(HttpMethod.Post, path, body with { code = "BAD", areaM2 = "1.234" }, key: Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Maintenance_is_atomic_idempotent_and_adjacency_is_available()
    {
        using var manager = await fixture.Login("operator@example.test");
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(20);
        var body = new { facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { UnitA, UnitB }, startDate = start, endDate = start.AddDays(2), reason = "Synthetic planned inspection" };
        var key = Guid.NewGuid();
        var created = await manager.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", body, key: key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await (await manager.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", body, key: key)).Content.ReadAsStringAsync());
        Assert.Equal(await created.Content.ReadAsStringAsync(), await (await manager.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.FacilityA}")).Content.ReadAsStringAsync());
        using var otherManager = await fixture.Login("manager@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await otherManager.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.FacilityA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Send(HttpMethod.Post, "/api/v1/maintenance-blocks", body with { unitIds = new[] { UnitB, UnitA }, reason = "Conflict" }, key: Guid.NewGuid())).StatusCode);
        using var client = fixture.Client();
        var adjacent = await client.GetAsync($"/api/v1/availability?facilityId={DemoIdentitySeed.FacilityA}&startDate={start.AddDays(2):yyyy-MM-dd}&endDate={start.AddDays(3):yyyy-MM-dd}");
        Assert.Contains(UnitA.ToString(), await adjacent.Content.ReadAsStringAsync());
        var overlapping = await client.GetAsync($"/api/v1/availability?facilityId={DemoIdentitySeed.FacilityA}&startDate={start:yyyy-MM-dd}&endDate={start.AddDays(1):yyyy-MM-dd}");
        Assert.DoesNotContain(UnitA.ToString(), await overlapping.Content.ReadAsStringAsync());
    }
}
