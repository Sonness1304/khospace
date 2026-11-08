using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WarehouseRent.Tests;

public sealed class IdentityScopeTests
{
    [Fact]
    public async Task Anonymous_identity_requires_authentication_without_redirect()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var response = await host.CreateClient().GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_requires_valid_antiforgery_before_credentials_or_database_access()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
        var client = host.CreateClient();
        var tokenResponse = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenBody = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(tokenBody.RootElement.GetProperty("token").GetString()));
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { userName = "missing@example.test", password = "Synthetic!1234" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CSRF_INVALID", await response.Content.ReadAsStringAsync());
    }
}
