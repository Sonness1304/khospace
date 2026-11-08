using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class MembershipAuditTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public MembershipAuditTests(IdentitySqlFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task Failure_persisting_command_receipt_rolls_back_membership_and_security_audit()
    {
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(fixture.Connection).AddInterceptors(new RejectMembershipReceipt()).Options));
        }));
        using var browser = new BrowserSession(host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        await browser.RefreshCsrf();
        await using var db = fixture.Db();
        var beforeAudit = await db.SecurityAudits.CountAsync(x => x.Action.StartsWith("Membership"));
        var command = Guid.NewGuid();
        var response = await browser.Send(HttpMethod.Post, "/api/v1/memberships", new { organizationId = DemoIdentitySeed.OrganizationA,
            userName = "unassigned@example.test", role = "Member", reason = "Synthetic rollback fault" }, key: command);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await db.Memberships.CountAsync(x => x.UserId == "unassigned@example.test"));
        Assert.Equal(beforeAudit, await db.SecurityAudits.CountAsync(x => x.Action.StartsWith("Membership")));
        Assert.False(await db.MembershipCommands.AnyAsync(x => x.Id == command));
    }

    [Fact]
    public async Task Last_active_admin_cannot_self_demote_or_deactivate()
    {
        using var admin = await fixture.Login("admin.b@example.test");
        await using var db = fixture.Db();
        var target = await db.Memberships.SingleAsync(x => x.UserId == "admin.b@example.test");
        var version = Convert.ToBase64String(target.Version);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Patch, $"/api/v1/memberships/{target.Id}",
            new { organizationId = target.OrganizationId, role = "Member", active = true, reason = "Synthetic last admin test" }, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Send(HttpMethod.Delete,
            $"/api/v1/memberships/{target.Id}?organizationId={target.OrganizationId}&reason=Synthetic%20last%20admin%20test", version: version, key: Guid.NewGuid())).StatusCode);
        Assert.True(await db.Memberships.AnyAsync(x => x.Id == target.Id && x.Role == "CustomerAdmin" && x.Active));
    }
}

public sealed class RejectMembershipReceipt : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context!.ChangeTracker.Entries<MembershipCommand>().Any(x => x.State == EntityState.Added))
            throw new InvalidOperationException("Synthetic audit transaction fault after first save.");
        return ValueTask.FromResult(result);
    }
}
