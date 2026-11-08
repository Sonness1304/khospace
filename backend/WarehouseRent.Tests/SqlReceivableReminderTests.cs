using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Jobs;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed partial class SqlReceivableReminderTests : IAsyncLifetime
{
    private readonly IdentitySqlFixture fixture = new();
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static string PathFor(Guid? organization = null, Guid? facility = null) =>
        $"/api/v1/organizations/{organization ?? DemoIdentitySeed.OrganizationA}/facilities/{facility ?? DemoIdentitySeed.FacilityA}/receivable-reminders";
    private static object Input(int page = 0) => new { startDate = Today.AddDays(-120), endDate = Today.AddDays(2), page };
    public Task InitializeAsync() => fixture.InitializeAsync();
    public Task DisposeAsync() => fixture.DisposeAsync();
    private async Task<ChargeStatement> Statement(int daysOverdue, decimal total = 1000)
    {
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, Today, Today.AddDays(1));
        await using var db = fixture.Db();
        var lease = await db.Leases.SingleAsync(x => x.Id == db.LeaseUnits.Where(g => g.Id == grant).Select(g => g.LeaseId).Single());
        var due = Today.AddDays(-daysOverdue);
        var row = new ChargeStatement { Id = Guid.NewGuid(), LeaseId = lease.Id, RentalAccountId = lease.RentalAccountId,
            OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, PeriodStart = new DateOnly(due.Year, due.Month, 1), DueDate = due, Total = total, IssuedAt = DateTimeOffset.UtcNow };
        db.ChargeStatements.Add(row); await db.SaveChangesAsync(); return row;
    }
    private static async Task<JsonElement> Result(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text); return json.RootElement.GetProperty("result").Clone();
    }
    [Fact]
    public async Task Reminder_readback_requires_current_finance_even_with_customer_membership()
    {
        await using var db = fixture.Db();
        db.Memberships.Add(new Membership { Id = Guid.NewGuid(), OrganizationId = DemoIdentitySeed.OrganizationA,
            UserId = "finance@example.test", Role = "CustomerAdmin" });
        await db.SaveChangesAsync();
        using var finance = await fixture.Login("finance@example.test"); var key = Guid.NewGuid();
        var response = await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key); await Result(response);
        var readback = $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}";
        Assert.Equal(await response.Content.ReadAsStringAsync(), await (await finance.Send(HttpMethod.Get, readback)).Content.ReadAsStringAsync());
        var grant = await db.StaffFacilityScopes.SingleAsync(x => x.UserId == "finance@example.test");
        db.Remove(grant); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Get, readback)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key)).StatusCode);
        db.StaffFacilityScopes.Add(grant); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await finance.Send(HttpMethod.Get, readback)).StatusCode);
        var organization = await db.CustomerOrganizations.SingleAsync(x => x.Id == DemoIdentitySeed.OrganizationA);
        organization.Active = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Get, readback)).StatusCode);
    }
    [Fact]
    public async Task Only_overdue_open_statements_are_delivered_with_exact_observed_amount_and_customer_scope()
    {
        var overdue = await Statement(1, 9007199254740993m); await Statement(0); await Statement(-1); await Statement(2, 0);
        using var finance = await fixture.Login("finance@example.test");
        var result = await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid()));
        Assert.Equal(4, result.GetProperty("scanned").GetInt32()); Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), result.GetProperty("asOfDate").GetString());
        await using var db = fixture.Db(); var notice = await db.InAppNotices.SingleAsync(); var queued = await db.NoticeOutbox.SingleAsync();
        Assert.Equal(overdue.Id, notice.AggregateId); Assert.Equal("PaymentOverdue", notice.Kind);
        Assert.Contains("9007199254740993", notice.Message); Assert.Contains("Check the current statement before paying", notice.Message);
        Assert.NotNull(queued.DeliveredAt); Assert.Single(await db.SecurityAudits.Where(x => x.Action == "ReceivableRemindersCreated").ToListAsync());
        using var customer = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        var read = $"/api/v1/notices?organizationId={overdue.OrganizationId}&facilityId={overdue.FacilityId}";
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Get, read)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, read)).StatusCode);
    }
    [Fact]
    public async Task Concurrent_fresh_commands_and_replay_create_one_notice_per_statement_local_day()
    {
        await Statement(10); using var finance = await fixture.Login("finance@example.test"); var key = Guid.NewGuid();
        var first = await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key); await Result(first);
        var replay = await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var keys=new[]{Guid.NewGuid(),Guid.NewGuid()};
        var concurrent = await Task.WhenAll(keys.Select(command=>finance.Send(HttpMethod.Post,PathFor(),Input(),key:command)));
        for(var i=0;i<concurrent.Length;i++) {
            var reply=concurrent[i];
            if(reply.StatusCode==HttpStatusCode.Conflict) {
                await using var inspect=fixture.Db();Assert.True(inspect.Database.IsNpgsql());
                Assert.Contains("COMMAND_RETRY_REQUIRED",await reply.Content.ReadAsStringAsync());
                Assert.False(await inspect.OperationalCommands.AnyAsync(x=>x.Id==keys[i]));
                Assert.Equal(HttpStatusCode.NotFound,(await finance.Send(HttpMethod.Get,$"/api/v1/commands/{keys[i]}?scopeId={DemoIdentitySeed.OrganizationA}")).StatusCode);
                // Explicit caller retry, same intent and same key, only after reading the rolled-back result.
                reply=await finance.Send(HttpMethod.Post,PathFor(),Input(),key:keys[i]);
            }
            Assert.Equal(0,(await Result(reply)).GetProperty("created").GetInt32());
        }
        await using var db = fixture.Db(); Assert.Equal(1, await db.InAppNotices.CountAsync());
        Assert.Equal(3, await db.OperationalCommands.CountAsync(x => x.RequiredFacilityRole == "Finance"));
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, PathFor(), Input(1), key: key)).StatusCode);
    }
    [Theory]
    [InlineData("admin.a@example.test")][InlineData("manager@example.test")][InlineData("clerk@example.test")]
    public async Task Non_finance_roles_cannot_create_reminders(string identity)
    {
        using var user = await fixture.Login(identity);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Revoked_or_inactive_finance_cannot_replay_success_and_foreign_scope_is_hidden()
    {
        await Statement(3); using var finance = await fixture.Login("finance@example.test"); var key = Guid.NewGuid();
        await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key));
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, PathFor(facility: DemoIdentitySeed.FacilityB), Input(), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, PathFor(organization: Guid.NewGuid()), Input(), key: Guid.NewGuid())).StatusCode);
        await using var db = fixture.Db(); var grant = await db.StaffFacilityScopes.SingleAsync(x => x.UserId == "finance@example.test"); db.Remove(grant); await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key)).StatusCode);
        db.StaffFacilityScopes.Add(grant); var user = await db.Users.SingleAsync(x => x.Id == "finance@example.test"); user.Active = false; await db.SaveChangesAsync();
        Assert.False((await finance.Send(HttpMethod.Post, PathFor(), Input(), key: key)).IsSuccessStatusCode);
    }
    [Fact]
    public async Task Reconciliation_failure_is_atomic_and_invalid_range_or_page_is_rejected()
    {
        var s = await Statement(5);
        await using (var db = fixture.Db()) { db.StatementAdjustments.Add(new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId,
            Kind = "Credit", Amount = 1001, Reason = "Synthetic invalid balance", EvidenceReference = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        using var finance = await fixture.Login("finance@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await finance.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid())).StatusCode);
        foreach (var page in new[] { -1, 100 }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, PathFor(), Input(page), key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Post, PathFor(), new { startDate = Today, endDate = Today.AddDays(367), page = 0 }, key: Guid.NewGuid())).StatusCode);
        await using var after = fixture.Db(); Assert.Empty(await after.InAppNotices.ToListAsync()); Assert.Empty(await after.NoticeOutbox.ToListAsync());
        Assert.Empty(await after.OperationalCommands.ToListAsync()); Assert.False(await after.SecurityAudits.AnyAsync(x => x.Action == "ReceivableRemindersCreated"));
    }
    [Fact]
    public async Task Fresh_command_observes_credit_settlement_and_keeps_prior_notice_as_historical_event()
    {
        var s = await Statement(5); using var finance = await fixture.Login("finance@example.test");
        await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid()));
        await using var db = fixture.Db(); var message = (await db.InAppNotices.SingleAsync()).Message;
        db.StatementAdjustments.Add(new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId,
            Kind = "Credit", Amount = 1000, Reason = "Synthetic settled credit", EvidenceReference = "Synthetic", ActorId = "synthetic", At = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var result = await Result(await finance.Send(HttpMethod.Post, PathFor(), Input(), key: Guid.NewGuid()));
        Assert.Equal(0, result.GetProperty("eligible").GetInt32()); Assert.Equal(message, (await db.InAppNotices.SingleAsync()).Message);
    }
}
