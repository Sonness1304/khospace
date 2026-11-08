using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Api.Features.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class CommandReplayTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_receipt_is_returned_before_time_sensitive_revalidation(bool rate)
    {
        using var original = await fixture.Login("operator@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var path = rate ? $"/api/v1/units/{SqlFacilitiesTests.UnitB}/rates" : "/api/v1/maintenance-blocks";
        var key = Guid.NewGuid(); string? version = null;
        if (rate)
        {
            using var catalog = JsonDocument.Parse(await (await original.Send(HttpMethod.Get, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/catalog")).Content.ReadAsStringAsync());
            version = catalog.RootElement.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == SqlFacilitiesTests.UnitB).GetProperty("version").GetString();
        }
        object payload = rate ? new { dailyRate = "5050", salesValidFrom = today, salesValidTo = (DateOnly?)null, reason = "Synthetic rate replay" }
            : new { facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { SqlFacilitiesTests.UnitA }, startDate = today.AddDays(2), endDate = today.AddDays(3), reason = "Synthetic maintenance replay" };
        var first = await original.Send(HttpMethod.Post, path, payload, version, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await using var shifted = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new ForwardClock());
            // Shift application date checks only; an actual ten-day-old session would correctly expire.
            services.PostConfigure<CookieAuthenticationOptions>(IdentityRegistration.Scheme, options => options.TimeProvider = TimeProvider.System);
        }));
        using var replay = new BrowserSession(shifted.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        foreach (var cookie in original.Cookies) replay.Cookies[cookie.Key] = cookie.Value;
        await replay.RefreshCsrf();
        var again = await replay.Send(HttpMethod.Post, path, payload, version, key);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
    }
}
public sealed class ForwardClock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddDays(10); }
