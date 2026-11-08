using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;
public sealed class BuildIdentityTests
{
    [Fact]
    public async Task Build_identity_is_SQL_independent_and_reports_assembly_revision_and_latest_migration()
    {
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Testing"); b.UseSetting("ConnectionStrings:WarehouseRent", "invalid-sentinel-never-print"); });
        var response = await host.CreateClient().GetAsync("/health/build"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body); var root = json.RootElement;
        var informational = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var revision = informational.Split('+').Last();
        Assert.Equal(revision, root.GetProperty("sourceRevision").GetString());
        Assert.Equal("WarehouseRent", root.GetProperty("application").GetString());
        Assert.False(root.GetProperty("runtimeAcceptanceVerified").GetBoolean());
        Assert.DoesNotContain("invalid-sentinel", body); Assert.DoesNotContain("ConnectionStrings", body);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        using var db = new AppDbContext(SqlConfiguration.Options("Server=127.0.0.1,1;Database=WarehouseRent_Test_Unreachable;Integrated Security=True"));
        Assert.Equal(Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GetMigrations(db.Database).Last(), root.GetProperty("latestExpectedMigration").GetString());
    }
}
