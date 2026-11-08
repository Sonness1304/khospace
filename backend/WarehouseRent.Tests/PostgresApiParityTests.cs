using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

// HTTP integration against generated loopback catalogs only, not a production provider switch.
public sealed class PostgresApiParityTests
{
    private static WebApplicationFactory<Program> Host(PostgresRuntimeFixture fixture) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:WarehouseRent", "Server=localhost;Database=UnusedFixture;Integrated Security=true;TrustServerCertificate=true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<AppDbContext>();
                services.AddScoped(_ => fixture.Context());
            });
        });

    [Fact]
    public async Task Bootstrap_is_idempotent_and_never_promotes_existing_customer_on_postgres()
    {
        await using var fixture = await PostgresRuntimeFixture.Create();
        await using var host = Host(fixture);
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        const string email = "owner@example.test", password = "SyntheticOperator!123456789";
        await CloudDatabaseSetup.InitializeOperatorAsync(db, users, email, password, default);
        await CloudDatabaseSetup.InitializeOperatorAsync(db, users, email, password, default);
        Assert.True(Assert.Single(await db.Users.ToListAsync()).OperatorAdmin);
        Assert.Single(await db.SecurityAudits.ToListAsync());
        Assert.Empty(await db.Facilities.ToListAsync());
        Assert.Empty(await db.FoundationMarkers.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => CloudDatabaseSetup.InitializeOperatorAsync(db, users, email, "WrongPassword!123456789", default));
        var customer = new AppUser { Email = "customer@example.test", UserName = "customer@example.test" };
        Assert.True((await users.CreateAsync(customer, password)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CloudDatabaseSetup.InitializeOperatorAsync(db, users, customer.Email, password, default));
        await db.Entry(customer).ReloadAsync();
        Assert.False(customer.OperatorAdmin);
        Assert.Single(await db.SecurityAudits.ToListAsync());
    }

    [Fact]
    public async Task Cookie_login_csrf_tenant_isolation_and_concurrent_multiunit_holds_match_sql_contract()
    {
        await using var fixture = await PostgresRuntimeFixture.Create();
        // Invoke existing internal seed routines only inside this generated synthetic fixture.
        await using (var db = fixture.Context())
            foreach (var type in new[] { typeof(DemoIdentitySeed), typeof(DemoFacilitySeed) })
                await (Task)type.GetMethod("ApplyAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { db, CancellationToken.None })!;
        await using var host = Host(fixture);
        async Task<BrowserSession> Login(string user)
        {
            var session = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
            await session.RefreshCsrf();
            var response = await session.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = user, password = DemoIdentitySeed.Password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await session.RefreshCsrf();
            return session;
        }
        using var one = await Login("admin.a@example.test");
        using var two = await Login("admin2.a@example.test");
        using var foreign = await Login("admin.b@example.test");
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(30);
        async Task<(Guid Id, string Version)> Quote(BrowserSession session, Guid[] units)
        {
            var response = await session.Send(HttpMethod.Post, "/api/v1/quotes", new { facilityId = DemoIdentitySeed.FacilityA, organizationId = DemoIdentitySeed.OrganizationA, unitIds = units, startDate = start, endDate = start.AddDays(2) }, key: Guid.NewGuid());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var result = json.RootElement.GetProperty("result");
            return (result.GetProperty("resourceId").GetGuid(), result.GetProperty("version").GetString()!);
        }
        var quote1 = await Quote(one, new[] { SqlFacilitiesTests.UnitA, SqlFacilitiesTests.UnitB });
        var quote2 = await Quote(two, new[] { SqlFacilitiesTests.UnitB, SqlFacilitiesTests.UnitA });
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/quotes/{quote1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await one.Send(HttpMethod.Post, $"/api/v1/quotes/{quote1.Id}/holds", new { expectedQuoteVersion = quote1.Version }, key: Guid.NewGuid(), csrf: false)).StatusCode);
        var key = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var responses = await Task.WhenAll(
            one.Send(HttpMethod.Post, $"/api/v1/quotes/{quote1.Id}/holds", new { expectedQuoteVersion = quote1.Version }, key: key),
            two.Send(HttpMethod.Post, $"/api/v1/quotes/{quote2.Id}/holds", new { expectedQuoteVersion = quote2.Version }, key: secondKey));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var check = fixture.Context();
        Assert.Equal(2, await check.SpaceAllocations.CountAsync(x => x.State == "Held"));
        Assert.Single(await check.RentalAccounts.ToListAsync());
        var firstWon = responses[0].StatusCode == HttpStatusCode.OK;
        var winner = firstWon ? one : two;
        var winningQuote = firstWon ? quote1 : quote2;
        var replay = await winner.Send(HttpMethod.Post, $"/api/v1/quotes/{winningQuote.Id}/holds", new { expectedQuoteVersion = winningQuote.Version }, key: firstWon ? key : secondKey);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await responses[firstWon ? 0 : 1].Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, await check.SpaceAllocations.CountAsync(x => x.State == "Held"));
        Assert.Single(await check.RentalAccounts.ToListAsync());
    }
}
