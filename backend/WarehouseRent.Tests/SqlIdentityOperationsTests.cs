using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlIdentityOperationsTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlIdentityOperationsTests(IdentitySqlFixture fixture) => this.fixture = fixture;
    private const string Password = "Synthetic_NewPassword!428";
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<BrowserSession> Anonymous()
    { var browser = new BrowserSession(fixture.Client()); await browser.RefreshCsrf(); return browser; }
    private async Task<string> Delivery(string recipient, string kind)
    {
        await using var db = fixture.Db();
        var encrypted = await db.Set<CredentialDelivery>().Where(x => x.Recipient == recipient && x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt).Select(x => x.ProtectedToken).FirstAsync();
        return fixture.Host.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("WarehouseRent.CredentialDelivery.v1").Unprotect(encrypted);
    }
    private async Task<HttpResponseMessage> Invite(BrowserSession admin, string email, Guid? org = null, Guid? key = null) =>
        await admin.Send(HttpMethod.Post, "/api/v1/invitations", new { organizationId = org ?? DemoIdentitySeed.OrganizationA,
            email, role = "Member", reason = "Synthetic onboarding" }, key: key ?? Guid.NewGuid());

    [Fact]
    public async Task Invitation_is_scoped_encrypted_single_use_and_idempotent()
    {
        using var admin = await fixture.Login(); var email = $"invite-{Guid.NewGuid():N}@example.test"; var key = Guid.NewGuid();
        var issued = await Invite(admin, email, key: key); Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        Assert.Equal(await issued.Content.ReadAsStringAsync(), await (await Invite(admin, email, key: key)).Content.ReadAsStringAsync());
        var token = await Delivery(email, "Invitation"); Assert.DoesNotContain(token, await issued.Content.ReadAsStringAsync());
        using var anonymous = await Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token, password = Password })).StatusCode);
        await using var db = fixture.Db(); var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant());
        Assert.True(await db.Memberships.AnyAsync(x => x.UserId == user.Id && x.OrganizationId == DemoIdentitySeed.OrganizationA && x.Role == "Member"));
        Assert.Equal(HttpStatusCode.NotFound, (await Invite(admin, $"foreign-{email}", DemoIdentitySeed.OrganizationB)).StatusCode);
        Assert.DoesNotContain(token, string.Join(" ", await db.SecurityAudits.Select(x => x.Reason).ToArrayAsync()));
    }

    [Fact]
    public async Task Expired_revoked_and_withdrawn_issuer_invitations_fail_without_creating_users()
    {
        using var admin = await fixture.Login("admin2.a@example.test"); using var anonymous = await Anonymous();
        foreach (var mode in new[] { "expired", "revoked", "withdrawn" })
        {
            var email = $"{mode}-{Guid.NewGuid():N}@example.test"; Assert.Equal(HttpStatusCode.OK, (await Invite(admin, email)).StatusCode);
            var token = await Delivery(email, "Invitation");
            await using (var db = fixture.Db())
            {
                var credential = await db.Set<AccessCredential>().SingleAsync(x => x.Email == email);
                if (mode == "expired") credential.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                if (mode == "revoked") credential.RevokedAt = DateTimeOffset.UtcNow;
                if (mode == "withdrawn") (await db.Memberships.SingleAsync(x => x.UserId == "admin2.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA)).Active = false;
                await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token, password = Password })).StatusCode);
            await using var verify = fixture.Db(); Assert.False(await verify.Users.AnyAsync(x => x.NormalizedEmail == email.ToUpperInvariant()));
        }
        await using var restore = fixture.Db(); (await restore.Memberships.SingleAsync(x => x.UserId == "admin2.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA)).Active = true;
        await restore.SaveChangesAsync();
    }

    [Fact]
    public async Task Reset_is_generic_single_use_and_revokes_existing_sessions()
    {
        using var member = await fixture.Login("member.a@example.test"); using var anonymous = await Anonymous();
        var known = await anonymous.Send(HttpMethod.Post, "/api/v1/auth/request-reset", new { email = "member.a@example.test" });
        var unknown = await anonymous.Send(HttpMethod.Post, "/api/v1/auth/request-reset", new { email = "absent@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode); Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var token = await Delivery("member.a@example.test", "Reset");
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/reset-password", new { token, password = "weak" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/reset-password", new { token, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await member.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/reset-password", new { token, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "member.a@example.test", password = Password })).StatusCode);
        // Restore only this synthetic fixture identity for other tests sharing the fixture.
        await using var scope = fixture.Host.Services.CreateAsyncScope(); var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var user = (await users.FindByNameAsync("member.a@example.test"))!;
        user.PasswordHash = users.PasswordHasher.HashPassword(user, DemoIdentitySeed.Password); Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    [Fact]
    public async Task Concurrent_redemptions_create_exactly_one_identity()
    {
        using var admin = await fixture.Login(); var email = $"race-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.OK, (await Invite(admin, email)).StatusCode); var token = await Delivery(email, "Invitation");
        using var a = await Anonymous(); using var b = await Anonymous();
        var responses = await Task.WhenAll(a.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token, password = Password }),
            b.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token, password = Password }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = fixture.Db(); Assert.Equal(1, await db.Users.CountAsync(x => x.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task Staff_changes_require_operator_version_and_revoke_disabled_sessions()
    {
        using var admin = await fixture.Login(); using var op = await fixture.Login("operator@example.test"); using var target = await fixture.Login("unassigned@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Send(HttpMethod.Get, "/api/v1/staff-directory")).StatusCode);
        var directory = await Json(await op.Send(HttpMethod.Get, "/api/v1/staff-directory")); var version = directory.GetProperty("version").GetString()!;
        var key = Guid.NewGuid(); var grant = new { userId = "unassigned@example.test", facilityId = DemoIdentitySeed.FacilityA, role = "Clerk", granted = true, reason = "Synthetic staff assignment" };
        Assert.Equal(HttpStatusCode.PreconditionRequired, (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/grants", grant, key: Guid.NewGuid())).StatusCode);
        var saved = await op.Send(HttpMethod.Post, "/api/v1/staff-directory/grants", grant, version: version, key: key); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(await saved.Content.ReadAsStringAsync(), await (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/grants", grant, version: version, key: key)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/grants", grant with { granted = false }, version: version, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await target.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityA}")).StatusCode);
        directory = await Json(await op.Send(HttpMethod.Get, "/api/v1/staff-directory")); version = directory.GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/disable", new { userId = "unassigned@example.test", reason = "Synthetic leaver" }, version: version, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode);
        using var anon = await Anonymous(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "unassigned@example.test", password = DemoIdentitySeed.Password })).StatusCode);
    }
    [Fact]
    public async Task Revocation_endpoint_is_scoped_and_removes_private_delivery()
    {
        using var admin = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test"); using var member = await fixture.Login("member.a@example.test");
        var email = $"revoke-{Guid.NewGuid():N}@example.test"; var issue = await Json(await Invite(admin, email)); var id = issue.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, $"/api/v1/invitations/{id}/revoke", new { reason = "Foreign" }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.Send(HttpMethod.Post, $"/api/v1/invitations/{id}/revoke", new { reason = "Member" }, key: Guid.NewGuid())).StatusCode);
        var key = Guid.NewGuid(); var revoke = await admin.Send(HttpMethod.Post, $"/api/v1/invitations/{id}/revoke", new { reason = "Withdraw offer" }, key: key);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(await revoke.Content.ReadAsStringAsync(), await (await admin.Send(HttpMethod.Post, $"/api/v1/invitations/{id}/revoke", new { reason = "Withdraw offer" }, key: key)).Content.ReadAsStringAsync());
        await using var db = fixture.Db(); Assert.False(await db.Set<CredentialDelivery>().AnyAsync(x => x.CredentialId == id));
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/invitations?organizationId={DemoIdentitySeed.OrganizationA}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Get, $"/api/v1/invitations?organizationId={DemoIdentitySeed.OrganizationA}")).StatusCode);
    }

    [Fact]
    public async Task Reset_cooldown_replacement_expiry_and_token_purpose_are_enforced()
    {
        using var anonymous = await Anonymous(); const string email = "clerk@example.test";
        async Task Request() => Assert.Equal(HttpStatusCode.Accepted, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/request-reset", new { email })).StatusCode);
        await Request(); var first = await Delivery(email, "Reset"); await Request(); Assert.Equal(first, await Delivery(email, "Reset"));
        await using (var db = fixture.Db())
        { foreach (var row in await db.Set<RecoveryCooldown>().ToListAsync()) row.RequestedAt = DateTimeOffset.UtcNow.AddMinutes(-2); await db.SaveChangesAsync(); }
        await Request(); var next = await Delivery(email, "Reset"); Assert.NotEqual(first, next);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/reset-password", new { token = first, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/accept-invitation", new { token = next, password = Password })).StatusCode);
        await using (var db = fixture.Db())
        { var credential = await db.Set<AccessCredential>().SingleAsync(x => x.Email == email && x.RevokedAt == null); credential.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/reset-password", new { token = next, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.Send(HttpMethod.Post, "/api/v1/auth/request-reset", new { email }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Cannot_take_over_existing_accounts_or_disable_protected_last_admin()
    {
        using var admin = await fixture.Login(); using var op = await fixture.Login("operator@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await Invite(admin, "admin.b@example.test")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Send(HttpMethod.Post, "/api/v1/invitations", new { organizationId = DemoIdentitySeed.OrganizationA,
            email = "valid@example.test", role = "Finance", reason = "Forbidden role" }, key: Guid.NewGuid())).StatusCode);
        var directory = await Json(await op.Send(HttpMethod.Get, "/api/v1/staff-directory")); var version = directory.GetProperty("version").GetString()!;
        foreach (var userId in new[] { "operator@example.test", "admin.b@example.test" })
            Assert.Equal(HttpStatusCode.Conflict, (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/disable", new { userId, reason = "Synthetic protection check" }, version: version, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.Send(HttpMethod.Post, "/api/v1/staff-directory/grants", new { userId = "clerk@example.test", facilityId = DemoIdentitySeed.FacilityA,
            role = "OperatorAdmin", granted = true, reason = "No elevation" }, version: version, key: Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Unsupported_identity_username_email_is_rejected_before_queueing()
    {
        using var admin = await fixture.Login(); var email = $"o'hara-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Invite(admin, email)).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.Set<AccessCredential>().AnyAsync(x => x.Email == email));
    }

}
