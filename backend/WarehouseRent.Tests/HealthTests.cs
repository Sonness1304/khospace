using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class HealthTests
{
    private static WebApplicationFactory<Program> CreateHost(string connection = "") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:WarehouseRent", connection);
        });

    [Fact]
    public async Task Liveness_does_not_require_SQL()
    {
        await using var host = CreateHost();
        var response = await host.CreateClient().GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("live", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.TryGetProperty("observedAt", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=127.0.0.1,1;Database=WarehouseRent_Test_Unreachable;User ID=synthetic;Password=DoNotLeakThisSentinel;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1")]
    [InlineData("not a connection string")]
    public async Task Readiness_without_SQL_returns_safe_503(string connection)
    {
        await using var host = CreateHost(connection);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var response = await host.CreateClient().GetAsync("/health/ready", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DoNotLeakThisSentinel", body);
        Assert.DoesNotContain("127.0.0.1", body);
        Assert.DoesNotContain("SqlException", body);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("unavailable", document.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("Production", "Server=localhost;Database=WarehouseRent_Demo;Integrated Security=True")]
    [InlineData("Development", "Server=localhost;Database=WarehouseRent_Demo;Integrated Security=True")]
    [InlineData("Demo", "Server=localhost;Database=UnrelatedBusiness;Integrated Security=True")]
    [InlineData("Demo", "Server=localhost;Database=WarehouseRent_DemoOtherBusiness;Integrated Security=True")]
    public void Demo_seed_refuses_other_environments_and_unrelated_databases(string environment, string connection)
    {
        Assert.Throws<InvalidOperationException>(() => DemoSeedPolicy.Validate(environment, connection));
    }

    [Fact]
    public void Demo_seed_accepts_explicit_synthetic_database()
    {
        DemoSeedPolicy.Validate("Demo", "Server=localhost;Database=WarehouseRent_Demo;Integrated Security=True");
    }

    [Theory]
    [InlineData("Production", "Server=localhost;Database=WarehouseRent_Demo")]
    [InlineData("Demo", "Server=localhost;Database=Unrelated")]
    public void Local_migration_refuses_production_and_unrelated_targets(string environment, string connection)
    {
        Assert.Throws<InvalidOperationException>(() => DemoSeedPolicy.ValidateMigration(environment, connection));
    }

    [Fact]
    public void Missing_connection_is_explicit_and_never_defaults_to_a_database()
    {
        Assert.Throws<InvalidOperationException>(() => SqlConfiguration.Options(""));
    }

    [Fact]
    public async Task SPA_fallback_never_claims_unknown_API_routes()
    {
        await using var host = CreateHost();
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/not-implemented")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/health/not-implemented")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/not-implemented", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/not-implemented")).StatusCode);
    }

    [Fact]
    public async Task Same_origin_package_serves_SPA_without_intercepting_API_errors()
    {
        var staticRoot = Path.Combine(Path.GetTempPath(), $"WarehouseRent_Test_Spa_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staticRoot);
        await File.WriteAllTextAsync(Path.Combine(staticRoot, "index.html"), "<!doctype html><title>Synthetic SPA fixture</title>");
        try
        {
            await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseWebRoot(staticRoot);
            });
            var client = host.CreateClient();
            Assert.Contains("Synthetic SPA fixture", await client.GetStringAsync("/"));
            Assert.Contains("Synthetic SPA fixture", await client.GetStringAsync("/workspace"));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/not-implemented")).StatusCode);
        }
        finally
        {
            File.Delete(Path.Combine(staticRoot, "index.html"));
            Directory.Delete(staticRoot);
        }
    }
}
