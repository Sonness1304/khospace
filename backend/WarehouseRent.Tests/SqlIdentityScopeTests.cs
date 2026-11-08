using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlIdentityScopeTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlIdentityScopeTests(IdentitySqlFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Signing_in_as_another_user_revokes_the_replaced_session()
    {
        using var browser = await fixture.Login();
        var previousCookie = browser.CookieHeader;
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login",
            new { userName = "admin.b@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        using var copied = fixture.Client();
        copied.DefaultRequestHeaders.Add("Cookie", previousCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Contains("admin.b@example.test", await (await browser.Send(HttpMethod.Get, "/api/v1/auth/me")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invalid_login_is_generic_for_missing_user_and_wrong_password()
    {
        using var browser = new BrowserSession(fixture.Client());
        await browser.RefreshCsrf();
        var first = await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "missing@example.test", password = "Wrong!1234" });
        var second = await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = "Wrong!1234" });
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstJson.RootElement.GetProperty("code").GetString(), secondJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(firstJson.RootElement.GetProperty("detail").GetString(), secondJson.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Protected_cookie_me_CSRF_and_copied_cookie_revocation()
    {
        using var browser = await fixture.Login();
        var me = await browser.Send(HttpMethod.Get, "/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var payload = await me.Content.ReadAsStringAsync();
        Assert.Contains("admin.a@example.test", payload);
        Assert.DoesNotContain("passwordHash", payload);
        var cookie = browser.CookieHeader;
        Assert.DoesNotContain("admin.a@example.test", cookie);
        var absent = await browser.Send(HttpMethod.Post, "/api/v1/auth/logout", csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, absent.StatusCode);
        var logout = await browser.Send(HttpMethod.Post, "/api/v1/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var copy = fixture.Client();
        copy.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Organization_and_facility_scope_uses_current_database_membership()
    {
        using var admin = await fixture.Login();
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Get, $"/api/v1/organizations/{DemoIdentitySeed.OrganizationA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, $"/api/v1/organizations/{DemoIdentitySeed.OrganizationB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, $"/api/v1/organizations/current?organizationId={DemoIdentitySeed.OrganizationB}")).StatusCode);
        using var clerk = await fixture.Login("clerk@example.test");
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityA}")).StatusCode);
        await using var db = fixture.Db();
        var scope = await db.StaffFacilityScopes.SingleAsync(x => x.UserId == "clerk@example.test");
        db.StaffFacilityScopes.Remove(scope);
        await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.Forbidden, (await clerk.Send(HttpMethod.Get, $"/api/v1/scope/facilities/{DemoIdentitySeed.FacilityA}")).StatusCode); }
        finally { db.StaffFacilityScopes.Add(scope); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Membership_actions_reject_forged_children_operator_roles_and_stale_versions()
    {
        using var admin = await fixture.Login();
        using var member = await fixture.Login("member.a@example.test");
        await using var db = fixture.Db();
        var target = await db.Memberships.SingleAsync(x => x.UserId == "member.a@example.test");
        var foreign = await db.Memberships.SingleAsync(x => x.UserId == "admin.b@example.test");
        object Change(Guid org, string role = "Member") => new { organizationId = org, role, active = true, reason = "Synthetic role review" };
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Send(HttpMethod.Patch, $"/api/v1/memberships/{target.Id}", Change(target.OrganizationId), Convert.ToBase64String(target.Version), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{foreign.Id}", Change(target.OrganizationId), Convert.ToBase64String(foreign.Version), Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.Send(HttpMethod.Post, "/api/v1/memberships", new { organizationId = target.OrganizationId, userName = "unassigned@example.test", role = "OperatorAdmin", reason = "Synthetic invalid grant" }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{target.Id}", Change(target.OrganizationId), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{target.Id}", Change(target.OrganizationId), Convert.ToBase64String(new byte[8]), Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Current_session_observes_membership_deactivation_and_user_disable()
    {
        using var member = await fixture.Login("member.a@example.test");
        await using var db = fixture.Db();
        var membership = await db.Memberships.SingleAsync(x => x.UserId == "member.a@example.test");
        membership.Active = false; await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.NotFound, (await member.Send(HttpMethod.Get, $"/api/v1/organizations/{membership.OrganizationId}")).StatusCode); }
        finally { membership.Active = true; await db.SaveChangesAsync(); }
        var user = await db.Users.SingleAsync(x => x.Id == "member.a@example.test");
        user.Active = false; await db.SaveChangesAsync();
        try { Assert.Equal(HttpStatusCode.Unauthorized, (await member.Send(HttpMethod.Get, "/api/v1/auth/me")).StatusCode); }
        finally { user.Active = true; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Existing_user_membership_add_is_idempotent_audited_and_role_change_revokes_admin_action()
    {
        using var admin = await fixture.Login();
        await using var db = fixture.Db();
        var key = Guid.NewGuid();
        var body = new { organizationId = DemoIdentitySeed.OrganizationA, userName = "unassigned@example.test", role = "CustomerAdmin", reason = "Synthetic membership setup" };
        var created = await admin.Send(HttpMethod.Post, "/api/v1/memberships", body, key: key);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var repeat = await admin.Send(HttpMethod.Post, "/api/v1/memberships", body, key: key);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await repeat.Content.ReadAsStringAsync());
        Assert.Equal(1, await db.Memberships.CountAsync(x => x.UserId == body.userName && x.OrganizationId == body.organizationId));
        var grant = await db.Memberships.SingleAsync(x => x.UserId == body.userName && x.OrganizationId == body.organizationId);
        Assert.Equal(1, await db.SecurityAudits.CountAsync(x => x.AggregateId == grant.Id && x.Action == "MembershipAdded"));
        using var added = await fixture.Login(body.userName);
        var changed = await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{grant.Id}", new { organizationId = body.organizationId, role = "Member", active = true, reason = "Synthetic demotion" }, Convert.ToBase64String(grant.Version), Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await added.Send(HttpMethod.Post, "/api/v1/memberships", new { organizationId = body.organizationId, userName = "admin.b@example.test", role = "Member", reason = "Should fail" }, key: Guid.NewGuid())).StatusCode);
        var collision = await admin.Send(HttpMethod.Post, "/api/v1/memberships", body with { }, key: key);
        Assert.Equal(HttpStatusCode.OK, collision.StatusCode);
        var altered = await admin.Send(HttpMethod.Post, "/api/v1/memberships", new { organizationId = body.organizationId, userName = body.userName, role = "Member", reason = body.reason }, key: key);
        Assert.Equal(HttpStatusCode.Conflict, altered.StatusCode);
    }

    [Fact]
    public async Task Concurrent_self_removal_cannot_remove_last_active_admin()
    {
        using var first = await fixture.Login();
        using var second = await fixture.Login("admin2.a@example.test");
        await using var db = fixture.Db();
        var targets = await db.Memberships.Where(x => x.OrganizationId == DemoIdentitySeed.OrganizationA && (x.UserId == "admin.a@example.test" || x.UserId == "admin2.a@example.test")).ToListAsync();
        var responses = await Task.WhenAll(targets.Select(target => (target.UserId == "admin.a@example.test" ? first : second).Send(HttpMethod.Patch,
            $"/api/v1/memberships/{target.Id}", new { organizationId = target.OrganizationId, role = "Member", active = false, reason = "Synthetic concurrent self removal" }, Convert.ToBase64String(target.Version), Guid.NewGuid())));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await db.Memberships.CountAsync(x => x.OrganizationId == DemoIdentitySeed.OrganizationA && x.Role == "CustomerAdmin" && x.Active));
        }
        finally
        {
            foreach (var target in targets) { await db.Entry(target).ReloadAsync(); target.Role = "CustomerAdmin"; target.Active = true; }
            await db.SaveChangesAsync();
        }
    }
}
