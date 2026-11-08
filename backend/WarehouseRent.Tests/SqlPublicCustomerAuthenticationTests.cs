using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlPublicCustomerAuthenticationTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private const string Password = "Synthetic_Customer!428";
    private static BrowserSession Browser(WebApplicationFactory<Program> host) => new(host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false }));

    [Fact]
    public async Task Concurrent_normalized_email_registration_creates_exactly_one_customer()
    {
        await using var host = fixture.Host.WithWebHostBuilder(_ => { });
        using var first = Browser(host); using var second = Browser(host);
        await first.RefreshCsrf(); await second.RefreshCsrf();
        var email = $"race-{Guid.NewGuid():N}@example.test";
        var results = await Task.WhenAll(first.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "First", email, password = Password }),
            second.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "Second", email = email.ToUpperInvariant(), password = Password }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.Db(); var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.Single(await db.Memberships.Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Single(await db.LoginSessions.Where(x => x.UserId == user.Id).ToListAsync());
    }

    [Fact]
    public async Task Registration_rotates_existing_session_and_preserves_customer_only_access()
    {
        await using var host = fixture.Host.WithWebHostBuilder(_ => { }); using var browser = Browser(host);
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        var oldCookies = browser.Cookies.ToDictionary(); await browser.RefreshCsrf();
        var email = $"rotation-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "New customer", email, password = Password })).StatusCode);
        using var old = Browser(host); foreach (var cookie in oldCookies) old.Cookies.Add(cookie.Key, cookie.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityA}")).StatusCode);
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = email.ToUpperInvariant(), password = Password })).StatusCode);
    }

    [Fact]
    public async Task Registration_ip_limit_is_bounded_and_partial_google_config_keeps_password_flow_available()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
        { builder.UseSetting("Authentication:Google:ClientId", "synthetic-only"); builder.UseSetting("Authentication:Google:ClientSecret", ""); });
        using var browser = Browser(host); await browser.RefreshCsrf();
        Assert.Equal("{\"google\":false}", await (await browser.Send(HttpMethod.Get, "/api/v1/auth/providers")).Content.ReadAsStringAsync());
        for (var attempt = 0; attempt < 10; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "", email = "invalid", password = "weak" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "", email = "invalid", password = "weak" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode);
    }

    [Fact]
    public async Task Registration_database_failure_rolls_back_user_workspace_membership_session_and_audit()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options())
                .AddInterceptors(new FailProvisioning()).Options));
        }));
        using var browser = Browser(host); await browser.RefreshCsrf(); await using var db = fixture.Db();
        var organizations = await db.CustomerOrganizations.CountAsync(); var sessions = await db.LoginSessions.CountAsync();
        var email = $"rollback-{Guid.NewGuid():N}@example.test";
        var response = await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "Rollback", email, password = Password });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Synthetic provisioning", await response.Content.ReadAsStringAsync());
        Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        Assert.Equal(organizations, await db.CustomerOrganizations.CountAsync()); Assert.Equal(sessions, await db.LoginSessions.CountAsync());
    }

    private sealed class FailProvisioning : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action == "CustomerRegistered"))
                throw new DbUpdateException("Synthetic provisioning failure; never exposed.");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Registration_creates_isolated_customer_and_revocable_session()
    {
        using var browser = new BrowserSession(fixture.Client());
        await browser.RefreshCsrf();
        var email = $"new-{Guid.NewGuid():N}@example.test";
        var response = await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "  Customer workspace  ", email = $"  {email}  ", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await browser.RefreshCsrf();
        var me = await browser.Send(HttpMethod.Get, "/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("operatorAdmin").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("facilities").EnumerateArray());
        var org = Assert.Single(json.RootElement.GetProperty("organizations").EnumerateArray());
        Assert.Equal("Customer workspace", org.GetProperty("name").GetString());
        Assert.Equal("CustomerAdmin", org.GetProperty("role").GetString());
        Assert.NotEqual(DemoIdentitySeed.OrganizationA, org.GetProperty("id").GetGuid());
        await using var db = fixture.Db();
        var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.False(user.OperatorAdmin);
        Assert.False(user.EmailConfirmed);
        var session = await db.LoginSessions.SingleAsync(x => x.UserId == user.Id);
        Assert.InRange(session.ExpiresAt, DateTimeOffset.UtcNow.AddHours(7.9), DateTimeOffset.UtcNow.AddHours(8.1));
        Assert.True(await db.SecurityAudits.AnyAsync(x => x.ActorId == user.Id && x.Action == "CustomerRegistered"));
        Assert.Equal(HttpStatusCode.NoContent, (await browser.Send(HttpMethod.Post, "/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Registration_rejects_duplicate_case_inputs_and_missing_csrf_without_partial_rows()
    {
        using var browser = new BrowserSession(fixture.Client()); await browser.RefreshCsrf();
        await using var db = fixture.Db();
        var before = await db.CustomerOrganizations.CountAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "Workspace", email = "csrf@example.test", password = Password }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", new { name = "Workspace", email = "ADMIN.A@EXAMPLE.TEST", password = Password })).StatusCode);
        foreach (var input in new[] {
            new { name = "", email = "empty@example.test", password = Password },
            new { name = new string('x', 201), email = "long@example.test", password = Password },
            new { name = "Workspace", email = "invalid", password = Password },
            new { name = "Workspace", email = "weak@example.test", password = "weak" },
            new { name = "Workspace", email = "long-pw@example.test", password = new string('x', 1025) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(HttpMethod.Post, "/api/v1/auth/register", input)).StatusCode);
        Assert.Equal(before, await db.CustomerOrganizations.CountAsync());
        Assert.False(await db.Users.AnyAsync(x => x.Email == "csrf@example.test" || x.Email == "weak@example.test"));
    }

    [Fact]
    public async Task Providers_without_google_configuration_is_safe_and_password_login_trims_email()
    {
        using var browser = new BrowserSession(fixture.Host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false }));
        var providers = await browser.Send(HttpMethod.Get, "/api/v1/auth/providers");
        Assert.Equal(HttpStatusCode.OK, providers.StatusCode);
        Assert.Equal("{\"google\":false}", await providers.Content.ReadAsStringAsync());
        var start = await browser.Send(HttpMethod.Get, "/api/v1/auth/google");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Equal("/?authError=google-unavailable#account", start.Headers.Location?.OriginalString);
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "  admin.a@example.test  ", password = DemoIdentitySeed.Password })).StatusCode);
    }
}
