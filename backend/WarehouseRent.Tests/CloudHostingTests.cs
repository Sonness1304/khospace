using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace WarehouseRent.Tests;

public sealed class CloudHostingTests
{
    private const string ProxyKey = "Synthetic_Cloud_Proxy_Key_Only_1234567890";
    private static string Keys() => Path.Combine(Path.GetTempPath(), $"WarehouseRent_CloudKeys_{Guid.NewGuid():N}");
    private static WebApplicationFactory<Program> Host(
        Dictionary<string, string?>? overrides = null, string? keys = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var item in new Dictionary<string, string?>
            {
                ["CloudHosting:Enabled"] = "true",
                ["CloudHosting:PublicOrigin"] = "https://warehouse.example.com",
                ["CloudHosting:BackendHost"] = "api.example.com",
                ["CloudHosting:HealthCheckHost"] = "healthcheck.railway.app",
                ["CloudHosting:SharedProxyKey"] = ProxyKey,
                ["CloudHosting:DataProtectionPath"] = keys ?? Keys(),
                ["Authentication:Google:ClientId"] = "synthetic-only",
                ["Authentication:Google:ClientSecret"] = "synthetic-not-real"
            }.Concat(overrides ?? [])) builder.UseSetting(item.Key, item.Value);
        });

    private static HttpClient Client(WebApplicationFactory<Program> host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://api.example.com"), HandleCookies = false, AllowAutoRedirect = false });

    private static HttpRequestMessage Signed(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-KhoSpace-Proxy-Key", ProxyKey);
        request.Headers.Add("X-Vercel-Forwarded-For", "203.0.113.10");
        return request;
    }

    [Theory]
    [InlineData("/api/v1/auth/providers")]
    [InlineData("/api/v1/auth/csrf")]
    [InlineData("/health/ready")]
    [InlineData("/signin-google")]
    [InlineData("/api/payment-notifications/momo")]
    public async Task Cloud_business_health_and_callback_paths_require_authenticated_gateway(string path)
    {
        await using var host = Host();
        using var client = Client(host);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ProxyKey, body);
        Assert.DoesNotContain("synthetic-not-real", body);
    }

    [Fact]
    public async Task Only_exact_liveness_is_public_and_foreign_host_is_rejected()
    {
        await using var host = Host();
        using var client = Client(host);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/health/live/extra")).StatusCode);
        using var healthClient = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://healthcheck.railway.app") });
        Assert.Equal(HttpStatusCode.OK, (await healthClient.GetAsync("/health/live")).StatusCode);
        using var foreign = Signed("/api/v1/auth/providers");
        foreign.Headers.Host = "attacker.example.org";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(foreign)).StatusCode);
    }

    [Theory]
    [InlineData("wrong-secret", "203.0.113.10")]
    [InlineData(ProxyKey, "not-an-ip")]
    [InlineData(ProxyKey, "203.0.113.10, 198.51.100.20")]
    [InlineData(ProxyKey, "")]
    public async Task Invalid_key_or_unverified_client_address_does_not_reach_authentication(string key, string address)
    {
        await using var host = Host(); using var client = Client(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/providers");
        request.Headers.TryAddWithoutValidation("X-KhoSpace-Proxy-Key", key);
        request.Headers.TryAddWithoutValidation("X-Vercel-Forwarded-For", address);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Google_uses_pinned_https_origin_and_ignores_spoofed_forwarded_fields()
    {
        await using var host = Host(); using var client = Client(host);
        using var request = Signed("/api/v1/auth/google");
        request.Headers.Add("X-Forwarded-Host", "attacker.example.org");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");
        request.Headers.Add("Forwarded", "host=attacker.example.org;proto=http;for=127.0.0.1");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("accounts.google.com", response.Headers.Location?.Host);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("https://warehouse.example.com/signin-google", query["redirect_uri"].ToString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(ProxyKey, response.Headers.Location!.OriginalString);
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Trusted_csrf_cookie_is_secure_and_no_store_without_SQL()
    {
        await using var host = Host(); using var client = Client(host);
        using var request = Signed("/api/v1/auth/csrf");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Secure", string.Join(";", response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Untrusted_legacy_client_address_cannot_replace_the_platform_address()
    {
        await using var host = Host(); using var client = Client(host);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/providers");
        request.Headers.Add("X-KhoSpace-Proxy-Key", ProxyKey);
        request.Headers.Add("X-KhoSpace-Client-IP", "203.0.113.10");
        request.Headers.Add("X-Forwarded-For", "203.0.113.10");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Spoofed_forwarded_addresses_do_not_split_the_authenticated_platform_rate_limit()
    {
        await using var host = Host(); using var client = Client(host);
        for (var index = 0; index < 31; index++)
        {
            using var request = Signed("/api/v1/auth/google");
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{index + 1}");
            var response = await client.SendAsync(request);
            Assert.Equal(index < 30 ? HttpStatusCode.Redirect : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("CloudHosting:PublicOrigin", "http://warehouse.example.com")]
    [InlineData("CloudHosting:PublicOrigin", "https://localhost")]
    [InlineData("CloudHosting:PublicOrigin", "https://127.0.0.1")]
    [InlineData("CloudHosting:PublicOrigin", "https://10.0.0.4")]
    [InlineData("CloudHosting:PublicOrigin", "https://[::1]")]
    [InlineData("CloudHosting:PublicOrigin", "https://warehouse.example.com/path")]
    [InlineData("CloudHosting:PublicOrigin", "https://user:password@warehouse.example.com")]
    [InlineData("CloudHosting:PublicOrigin", "https://warehouse.example.com?secret=sentinel")]
    [InlineData("CloudHosting:PublicOrigin", "https://warehouse.example.com#fragment")]
    [InlineData("CloudHosting:SharedProxyKey", "")]
    [InlineData("CloudHosting:SharedProxyKey", "short")]
    [InlineData("CloudHosting:BackendHost", "*")]
    [InlineData("CloudHosting:BackendHost", "localhost")]
    [InlineData("CloudHosting:HealthCheckHost", "*.railway.app")]
    [InlineData("CloudHosting:DataProtectionPath", "relative-keys")]
    public void Cloud_invalid_configuration_fails_closed_without_exposing_values(string key, string value)
    {
        using var host = Host(new() { [key] = value });
        var error = Assert.ThrowsAny<Exception>(() => Client(host));
        Assert.DoesNotContain(ProxyKey, error.Message);
        Assert.DoesNotContain("sentinel", error.Message);
        Assert.DoesNotContain("user:password", error.Message);
    }

    [Fact]
    public async Task Persistent_keys_are_shared_only_by_explicit_application_configuration()
    {
        var keys = Keys();
        string protectedValue;
        await using (var first = Host(keys: keys))
        {
            using var client = Client(first);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cloud-fixture").Protect("synthetic-fixture");
        }
        await using var second = Host(keys: keys); using var next = Client(second);
        Assert.Equal(HttpStatusCode.OK, (await next.GetAsync("/health/live")).StatusCode);
        var restored = second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cloud-fixture").Unprotect(protectedValue);
        Assert.Equal("synthetic-fixture", restored);
        Assert.NotEmpty(Directory.EnumerateFiles(keys, "key-*.xml"));
    }

    [Fact]
    public async Task Disabled_cloud_mode_preserves_local_behavior()
    {
        await using var host = Host(new() { ["CloudHosting:Enabled"] = "false", ["CloudHosting:SharedProxyKey"] = "" });
        using var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/providers")).StatusCode);
    }
}
