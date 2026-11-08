using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SessionHealthTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Authenticated_cookie_does_not_make_health_depend_on_session_SQL()
    {
        using var browser = await fixture.Login();
        await using var unavailable = fixture.Host.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:WarehouseRent", "Server=127.0.0.1,1;Database=WarehouseRent_Test_Unreachable;User ID=synthetic;Password=Synthetic_Unreachable!42;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1"));
        using var client = unavailable.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", browser.CookieHeader);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live", deadline.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready", deadline.Token)).StatusCode);
    }
}
