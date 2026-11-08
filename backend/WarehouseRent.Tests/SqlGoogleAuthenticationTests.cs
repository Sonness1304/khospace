using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlGoogleAuthenticationTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private WebApplicationFactory<Program> GoogleHost(FakeGoogle provider, bool failProvisioning = false) => fixture.Host.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Authentication:Google:ClientId", "synthetic-client");
        builder.UseSetting("Authentication:Google:ClientSecret", "synthetic-secret-not-real");
        builder.ConfigureTestServices(services =>
        {
            services.PostConfigure<GoogleOptions>(GoogleDefaults.AuthenticationScheme, options => options.Backchannel = new HttpClient(provider));
            if (failProvisioning)
            {
                services.RemoveAll<AppDbContext>();
                services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options())
                    .AddInterceptors(new ProvisioningFailure()).Options));
            }
        });
    });

    private static BrowserSession Browser(WebApplicationFactory<Program> host) => new(host.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false }));

    private static async Task<string> Challenge(BrowserSession browser, FakeGoogle provider)
    {
        var response = await browser.Send(HttpMethod.Get, "/api/v1/auth/google?returnUrl=https://evil.example");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("accounts.google.com", location.Host);
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("https://localhost/signin-google", query["redirect_uri"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        provider.Challenge = query["code_challenge"].ToString();
        Assert.DoesNotContain("synthetic-secret", location.OriginalString);
        return query["state"].ToString();
    }

    private static async Task<HttpResponseMessage> Callback(BrowserSession browser, string state, string code) =>
        await browser.Send(HttpMethod.Get, QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?> { ["state"] = state, ["code"] = code }));

    private static void Error(HttpResponseMessage response, string code)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/?authError={code}#account", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Http_google_challenge_requires_secure_origin_without_provider_or_cookie()
    {
        var provider = new FakeGoogle($"http-{Guid.NewGuid():N}@example.test");
        await using var host = GoogleHost(provider);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/v1/auth/google");
        Error(response, "google-https-required");
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Verified_google_v3_userinfo_provisions_isolated_workspace_and_external_ticket_is_single_use()
    {
        var email = $"google-{Guid.NewGuid():N}@example.test";
        var provider = new FakeGoogle(email); await using var host = GoogleHost(provider); using var browser = Browser(host);
        Assert.Equal("{\"google\":true}", await (await browser.Send(HttpMethod.Get, "/api/v1/auth/providers")).Content.ReadAsStringAsync());
        var state = await Challenge(browser, provider);
        var callback = await Callback(browser, state, "first");
        Assert.Equal("/api/v1/auth/google/complete", callback.Headers.Location?.OriginalString);
        Assert.True(provider.PkceValidated);
        var captured = browser.Cookies.ToDictionary();
        var complete = await browser.Send(HttpMethod.Get, "/api/v1/auth/google/complete?returnUrl=https://evil.example");
        Assert.Equal("/#account", complete.Headers.Location?.OriginalString);
        var me = await browser.Send(HttpMethod.Get, "/api/v1/auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("operatorAdmin").GetBoolean());
        Assert.Empty(json.RootElement.GetProperty("facilities").EnumerateArray());
        var org = Assert.Single(json.RootElement.GetProperty("organizations").EnumerateArray());
        Assert.Equal("CustomerAdmin", org.GetProperty("role").GetString());
        await using var db = fixture.Db();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        Assert.True(user.EmailConfirmed); Assert.Null(user.PasswordHash);
        Assert.True(await db.UserLogins.AnyAsync(x => x.UserId == user.Id && x.LoginProvider == "Google" && x.ProviderKey == provider.Subject));
        Assert.False(await db.UserTokens.AnyAsync(x => x.UserId == user.Id));
        Assert.Single(await db.LoginSessions.Where(x => x.UserId == user.Id).ToListAsync());
        using var replay = Browser(host); foreach (var cookie in captured) replay.Cookies.Add(cookie.Key, cookie.Value);
        Error(await replay.Send(HttpMethod.Get, "/api/v1/auth/google/complete"), "google-failed");
        Assert.Single(await db.LoginSessions.Where(x => x.UserId == user.Id).ToListAsync());
        Error(await Callback(browser, state, "replayed-code"), "google-failed");
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.Send(HttpMethod.Post, "/api/v1/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
        using var returning = Browser(host); state = await Challenge(returning, provider);
        await Callback(returning, state, "returning");
        Assert.Equal("/#account", (await returning.Send(HttpMethod.Get, "/api/v1/auth/google/complete")).Headers.Location?.OriginalString);
        Assert.Single(await db.Memberships.Where(x => x.UserId == user.Id).ToListAsync());
        Assert.Equal(2, await db.LoginSessions.CountAsync(x => x.UserId == user.Id));
    }

    [Theory]
    [InlineData("unverified", "google-unverified")]
    [InlineData("missing-subject", "google-unverified")]
    [InlineData("collision", "google-link-required")]
    public async Task Unverified_missing_subject_and_existing_local_email_cannot_provision(string mode, string error)
    {
        var email = mode == "collision" ? "ADMIN.A@EXAMPLE.TEST" : $"reject-{Guid.NewGuid():N}@example.test";
        var provider = new FakeGoogle(email) { Verified = mode != "unverified", Subject = mode == "missing-subject" ? "" : Guid.NewGuid().ToString() };
        await using var host = GoogleHost(provider); using var browser = Browser(host);
        await using var db = fixture.Db(); var count = await db.CustomerOrganizations.CountAsync();
        var state = await Challenge(browser, provider); var result = await Callback(browser, state, "rejected");
        if (mode == "collision") result = await browser.Send(HttpMethod.Get, "/api/v1/auth/google/complete");
        Error(result, error);
        Assert.Equal(count, await db.CustomerOrganizations.CountAsync());
        Assert.False(await db.UserLogins.AnyAsync(x => x.LoginProvider == "Google" && x.ProviderKey == provider.Subject));
    }

    [Theory]
    [InlineData("missing-verification")]
    [InlineData("string-verification")]
    [InlineData("number-verification")]
    [InlineData("null-verification")]
    [InlineData("invalid-subject")]
    [InlineData("legacy-only")]
    [InlineData("conflicting-verification")]
    public async Task Invalid_v3_userinfo_is_rejected_without_provisioning(string mode)
    {
        var email = $"invalid-v3-{Guid.NewGuid():N}@example.test";
        var payload = new Dictionary<string, object?> { ["sub"] = Guid.NewGuid().ToString(), ["email"] = email, ["email_verified"] = true };
        if (mode == "missing-verification") payload.Remove("email_verified");
        if (mode == "string-verification") payload["email_verified"] = "true";
        if (mode == "number-verification") payload["email_verified"] = 1;
        if (mode == "null-verification") payload["email_verified"] = null;
        if (mode == "invalid-subject") payload["sub"] = 123;
        if (mode == "legacy-only") { payload.Remove("sub"); payload.Remove("email_verified"); payload["id"] = "legacy-subject"; payload["verified_email"] = true; }
        if (mode == "conflicting-verification") { payload["email_verified"] = false; payload["verified_email"] = true; }
        var provider = new FakeGoogle(email) { UserInformationOverride = JsonSerializer.SerializeToElement(payload) };
        await using var host = GoogleHost(provider); using var browser = Browser(host);
        await using var db = fixture.Db(); var count = await db.CustomerOrganizations.CountAsync();
        var googleLogins = await db.UserLogins.CountAsync(x => x.LoginProvider == "Google");
        var state = await Challenge(browser, provider);
        Error(await Callback(browser, state, "invalid-v3"), "google-unverified");
        Assert.Equal(count, await db.CustomerOrganizations.CountAsync());
        Assert.False(await db.Users.AnyAsync(x => x.Email == email));
        Assert.Equal(googleLogins, await db.UserLogins.CountAsync(x => x.LoginProvider == "Google"));
    }

    [Fact]
    public async Task Inactive_or_locked_external_identity_is_denied_and_provisioning_failure_rolls_back()
    {
        var email = $"existing-google-{Guid.NewGuid():N}@example.test";
        var provider = new FakeGoogle(email); await using var host = GoogleHost(provider); using var first = Browser(host);
        var state = await Challenge(first, provider); await Callback(first, state, "created"); await first.Send(HttpMethod.Get, "/api/v1/auth/google/complete");
        await using var db = fixture.Db(); var user = await db.Users.SingleAsync(x => x.Email == email); user.Active = false; await db.SaveChangesAsync();
        using var inactive = Browser(host); state = await Challenge(inactive, provider); await Callback(inactive, state, "inactive");
        Error(await inactive.Send(HttpMethod.Get, "/api/v1/auth/google/complete"), "account-disabled");
        user.Active = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15); await db.SaveChangesAsync();
        using var locked = Browser(host); state = await Challenge(locked, provider); await Callback(locked, state, "locked");
        Error(await locked.Send(HttpMethod.Get, "/api/v1/auth/google/complete"), "account-disabled");
        Assert.Single(await db.LoginSessions.Where(x => x.UserId == user.Id).ToListAsync());
        var failedEmail = $"rollback-google-{Guid.NewGuid():N}@example.test";
        var failingProvider = new FakeGoogle(failedEmail); await using var failingHost = GoogleHost(failingProvider, true); using var failing = Browser(failingHost);
        var count = await db.CustomerOrganizations.CountAsync(); state = await Challenge(failing, failingProvider); await Callback(failing, state, "rollback");
        Error(await failing.Send(HttpMethod.Get, "/api/v1/auth/google/complete"), "google-failed");
        Assert.Equal(count, await db.CustomerOrganizations.CountAsync()); Assert.False(await db.Users.AnyAsync(x => x.Email == failedEmail));
        Assert.False(await db.UserLogins.AnyAsync(x => x.LoginProvider == "Google" && x.ProviderKey == failingProvider.Subject));
    }

    [Fact]
    public async Task State_correlation_and_cancellation_fail_safely_without_provider_or_database_mutation()
    {
        var provider = new FakeGoogle($"state-{Guid.NewGuid():N}@example.test"); await using var host = GoogleHost(provider);
        using var browser = Browser(host); Error(await Callback(browser, "invalid-state", "ignored"), "google-failed");
        var state = await Challenge(browser, provider);
        using var foreign = Browser(host); Error(await Callback(foreign, state, "ignored"), "google-failed");
        Error(await browser.Send(HttpMethod.Get, QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?>
            { ["state"] = state, ["error"] = "access_denied", ["error_description"] = "UNTRUSTED_PROVIDER_DETAIL" })), "google-cancelled");
        Assert.Equal(0, provider.Requests);
        Error(await browser.Send(HttpMethod.Get, "/api/v1/auth/google/complete"), "google-failed");
    }

    [Fact]
    public async Task Google_replacement_revokes_prior_session_even_when_strict_cookie_is_absent_on_callback()
    {
        var email = $"switch-google-{Guid.NewGuid():N}@example.test";
        var provider = new FakeGoogle(email); await using var host = GoogleHost(provider); using var browser = Browser(host);
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        var oldCookies = browser.Cookies.ToDictionary(); var state = await Challenge(browser, provider);
        browser.Cookies.Remove("WarehouseRent.Session");
        await Callback(browser, state, "switch");
        Assert.Equal("/#account", (await browser.Send(HttpMethod.Get, "/api/v1/auth/google/complete")).Headers.Location?.OriginalString);
        using var old = Browser(host); foreach (var cookie in oldCookies) old.Cookies.Add(cookie.Key, cookie.Value);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Provider_exchange_failure_uses_fixed_safe_error_without_exception_details()
    {
        var provider = new FakeGoogle($"failure-{Guid.NewGuid():N}@example.test") { FailExchange = true };
        await using var host = GoogleHost(provider); using var browser = Browser(host);
        var state = await Challenge(browser, provider); var response = await Callback(browser, state, "provider-error");
        Error(response, "google-failed");
        Assert.DoesNotContain("UNTRUSTED_PROVIDER_DETAIL", await response.Content.ReadAsStringAsync());
        Assert.False(browser.Cookies.TryGetValue("WarehouseRent.External", out var external) && !string.IsNullOrEmpty(external));
    }
    private sealed class ProvisioningFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.Entity.Action == "CustomerRegistered"))
                throw new DbUpdateException("Synthetic provisioning failure; never exposed.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeGoogle(string email) : HttpMessageHandler
    {
        public string Subject { get; set; } = Guid.NewGuid().ToString();
        public bool Verified { get; set; } = true;
        public string Challenge { get; set; } = "";
        public bool PkceValidated { get; private set; }
        public int Requests { get; private set; }
        public bool FailExchange { get; init; }
        public JsonElement? UserInformationOverride { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method == HttpMethod.Post)
            {
                if (FailExchange) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant", error_description = "UNTRUSTED_PROVIDER_DETAIL" }) };
                var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
                var verifier = form["code_verifier"].ToString();
                PkceValidated = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) == Challenge;
                Assert.True(PkceValidated); Assert.Equal("https://localhost/signin-google", form["redirect_uri"].ToString());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { access_token = "fake-access-token", token_type = "Bearer", expires_in = 300 }) };
            }
            Assert.Equal("www.googleapis.com", request.RequestUri!.Host);
            Assert.Equal("/oauth2/v3/userinfo", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(UserInformationOverride ?? JsonSerializer.SerializeToElement(new { sub = Subject, email, email_verified = Verified, name = "Google Customer" })) };
        }
    }
}
