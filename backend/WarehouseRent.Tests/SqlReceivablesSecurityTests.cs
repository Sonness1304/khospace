using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Finance;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;
public sealed partial class SqlReceivablesTests
{
    [Fact]
    public async Task Revoked_finance_grant_and_foreign_facility_are_denied_with_existing_cookie()
    {
        using var finance = await fixture.Login("finance@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Get, PathFor(facility: DemoIdentitySeed.FacilityB))).StatusCode);
        await using (var db = fixture.Db()) await db.StaffFacilityScopes.Where(x => x.UserId == "finance@example.test" && x.FacilityId == DemoIdentitySeed.FacilityA && x.StaffRole == "Finance").ExecuteDeleteAsync();
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Get, PathFor())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await finance.Send(HttpMethod.Get, PathFor("exports"))).StatusCode);
        }
        finally
        {
            await using var db = fixture.Db(); db.StaffFacilityScopes.Add(new StaffFacilityScope { UserId = "finance@example.test", FacilityId = DemoIdentitySeed.FacilityA, StaffRole = "Finance" }); await db.SaveChangesAsync();
        }
    }
    [Fact]
    public async Task Csv_formula_escaping_large_integer_precision_and_due_date_half_open_range()
    {
        var due = Today.AddDays(-17); var s = await Statement(due, 9007199254740993m);
        string original;
        await using (var db = fixture.Db())
        {
            var org = await db.CustomerOrganizations.SingleAsync(x => x.Id == s.OrganizationId); original = org.Name; org.Name = "=SUM(1,2)\"Synthetic"; await db.SaveChangesAsync();
        }
        using var op = await fixture.Login("operator@example.test");
        var path = $"/api/v1/reports/receivables?organizationId={s.OrganizationId}&facilityId={s.FacilityId}&startDate={due:yyyy-MM-dd}&endDate={due.AddDays(1):yyyy-MM-dd}";
        try
        {
            var data = await Read(await op.Send(HttpMethod.Get, path));
            Assert.Equal("9007199254740993", data.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("statementId").GetGuid() == s.Id).GetProperty("originalVnd").GetString());
            var excluded = await Read(await op.Send(HttpMethod.Get, path.Replace($"startDate={due:yyyy-MM-dd}&endDate={due.AddDays(1):yyyy-MM-dd}", $"startDate={due.AddDays(-1):yyyy-MM-dd}&endDate={due:yyyy-MM-dd}")));
            Assert.DoesNotContain(excluded.GetProperty("items").EnumerateArray(), x => x.GetProperty("statementId").GetGuid() == s.Id);
            var csv = await (await op.Send(HttpMethod.Get, path.Replace("/reports/", "/exports/"))).Content.ReadAsStringAsync();
            Assert.Contains("\"'=SUM(1,2)\"\"Synthetic\"", csv); Assert.Contains("9007199254740993", csv);
        }
        finally { await using var db = fixture.Db(); (await db.CustomerOrganizations.SingleAsync(x => x.Id == s.OrganizationId)).Name = original; await db.SaveChangesAsync(); }
    }
    [Fact]
    public async Task Negative_reconciliation_is_refused_instead_of_clamped_and_not_export_audited()
    {
        var s = await Statement(Today, 10);
        await using (var db = fixture.Db()) { db.StatementAdjustments.Add(new StatementAdjustment { Id = Guid.NewGuid(), ChargeStatementId = s.Id, RentalAccountId = s.RentalAccountId, Kind = "Credit", Amount = 11, EvidenceReference = "Synthetic corrupt fixture", Reason = "Fault injection", ActorId = "synthetic", At = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        using var finance = await fixture.Login("finance@example.test");
        try
        {
            await using var before = fixture.Db(); var count = await before.SecurityAudits.CountAsync(x => x.Action == "ReceivablesExported");
            foreach (var kind in new[] { "reports", "exports" })
            {
                var response = await finance.Send(HttpMethod.Get, PathFor(kind)); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("ACCOUNTING_RECONCILIATION_REQUIRED", await response.Content.ReadAsStringAsync());
            }
            await using var after = fixture.Db(); Assert.Equal(count, await after.SecurityAudits.CountAsync(x => x.Action == "ReceivablesExported"));
        }
        finally { await using var db = fixture.Db(); await db.StatementAdjustments.Where(x => x.ChargeStatementId == s.Id).ExecuteDeleteAsync(); }
    }
    [Fact]
    public async Task Export_audit_failure_returns_no_attachment_and_commits_no_success_audit()
    {
        using var host = fixture.Host.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<AppDbContext>(); s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectReceivableAudit()).Options));
        }));
        using var client = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await client.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await client.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "finance@example.test", password = DemoIdentitySeed.Password })).StatusCode); await client.RefreshCsrf();
        await using var db = fixture.Db(); var count = await db.SecurityAudits.CountAsync(x => x.Action == "ReceivablesExported");
        var response = await client.Send(HttpMethod.Get, PathFor("exports")); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal(count, await db.SecurityAudits.CountAsync(x => x.Action == "ReceivablesExported"));
    }
    private sealed class RejectReceivableAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x => x.State == EntityState.Added && x.Entity.Action == "ReceivablesExported")) throw new InvalidOperationException("Synthetic export audit failure");
            return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task More_than_5000_matched_statements_refuses_both_formats_without_truncation()
    {
        var own = new IdentitySqlFixture(); await own.InitializeAsync();
        try
        {
            var seed = new SqlReceivablesTests(own); var first = await seed.Statement(Today); await using var db = own.Db();
            db.ChargeStatements.AddRange(Enumerable.Range(0, 4999).Select(i => new ChargeStatement { Id = Guid.NewGuid(), LeaseId = first.LeaseId, RentalAccountId = first.RentalAccountId, OrganizationId = first.OrganizationId,
                FacilityId = first.FacilityId, PeriodStart = new DateOnly(1500, 1, 1).AddMonths(i), DueDate = Today, IssuedAt = DateTimeOffset.UtcNow, Total = 1 }));
            await db.SaveChangesAsync(); using var finance = await own.Login("finance@example.test");
            var boundary = await Read(await finance.Send(HttpMethod.Get, PathFor()));
            Assert.Equal(5000, boundary.GetProperty("summary").GetProperty("matchedStatements").GetInt32());
            Assert.Equal(50, boundary.GetProperty("items").GetArrayLength());
            var lastPage = await Read(await finance.Send(HttpMethod.Get, PathFor(page: 99)));
            Assert.Equal(50, lastPage.GetProperty("items").GetArrayLength()); Assert.Equal(System.Text.Json.JsonValueKind.Null, lastPage.GetProperty("nextPage").ValueKind);
            db.ChargeStatements.Add(new ChargeStatement { Id = Guid.NewGuid(), LeaseId = first.LeaseId, RentalAccountId = first.RentalAccountId, OrganizationId = first.OrganizationId,
                FacilityId = first.FacilityId, PeriodStart = new DateOnly(2000, 1, 1), DueDate = Today, IssuedAt = DateTimeOffset.UtcNow, Total = 1 });
            await db.SaveChangesAsync();
            foreach (var kind in new[] { "reports", "exports" }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await finance.Send(HttpMethod.Get, PathFor(kind))).StatusCode);
            Assert.False(await db.SecurityAudits.AnyAsync(x => x.Action == "ReceivablesExported"));
        }
        finally { await own.DisposeAsync(); }
    }
}
