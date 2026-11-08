using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlBarcodeTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlBarcodeTests(IdentitySqlFixture fixture) => this.fixture = fixture;
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private async Task<Guid> Item(BrowserSession browser, Guid org)
    {
        var response = await browser.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = org, sku = "BC-" + Guid.NewGuid().ToString("N"), description = "Synthetic barcode item", baseUom = "box", reason = "Barcode fixture" }, key: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Json(response)).GetProperty("result").GetProperty("resourceId").GetGuid();
    }
    private async Task<Guid> Lease(Guid? facility = null)
    {
        await using var db = fixture.Db(); var original = await db.RentableUnits.FirstAsync(x => x.FacilityId == (facility ?? DemoIdentitySeed.FacilityA));
        var unit = new RentableUnit { Id = Guid.NewGuid(), FacilityId = original.FacilityId, ZoneId = original.ZoneId, Code = "BC-" + Guid.NewGuid().ToString("N")[..12], AreaM2 = 10, Published = true };
        db.RentableUnits.Add(unit); await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        var grant = await fixture.SeedOccupancyOwner(unit.Id, today, today.AddDays(10));
        return await db.LeaseUnits.Where(x => x.Id == grant).Select(x => x.LeaseId).SingleAsync();
    }
    private static Task<HttpResponseMessage> Add(BrowserSession browser, Guid item, Guid org, string value, string kind = "Internal", Guid? key = null) =>
        browser.Send(HttpMethod.Post, $"/api/v1/items/{item}/barcodes", new { organizationId = org, kind, value, reason = "Register synthetic label" }, key: key ?? Guid.NewGuid());
    private static string Lookup(Guid lease, string value, string kind = "Internal") => $"/api/v1/leases/{lease}/barcode-lookup?kind={kind}&value={Uri.EscapeDataString(value)}";

    [Fact]
    public async Task Barcode_receipt_failure_rolls_back_mapping_and_audit()
    {
        using var admin = await fixture.Login(); var item = await Item(admin, DemoIdentitySeed.OrganizationA); var key = Guid.NewGuid(); var code = "ROLLBACK-" + key.ToString("N").ToUpperInvariant();
        int auditCount; await using (var before = fixture.Db()) auditCount = await before.SecurityAudits.CountAsync(x => x.Action == "BarcodeRegistered");
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.RemoveAll<AppDbContext>(); services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectCatalogReceipt()).Options));
        }));
        using var browser = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await browser.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode); await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.InternalServerError, (await Add(browser, item, DemoIdentitySeed.OrganizationA, code, key: key)).StatusCode);
        await using var db = fixture.Db(); Assert.False(await db.ItemBarcodes.AnyAsync(x => x.CanonicalValue == code)); Assert.False(await db.OperationalCommands.AnyAsync(x => x.Id == key));
        Assert.Equal(auditCount, await db.SecurityAudits.CountAsync(x => x.Action == "BarcodeRegistered"));
        Assert.Equal(HttpStatusCode.OK, (await Add(admin, item, DemoIdentitySeed.OrganizationA, code, key: key)).StatusCode);
        var alias = await db.ItemBarcodes.SingleAsync(x => x.CanonicalValue == code);
        Assert.Single(await db.SecurityAudits.Where(x => x.Action == "BarcodeRegistered" && x.AggregateId == alias.Id).ToArrayAsync());
    }
    [Fact]
    public async Task Populated_barcode_migration_refuses_destructive_downgrade()
    {
        using var admin = await fixture.Login(); var item = await Item(admin, DemoIdentitySeed.OrganizationA); var code = "MIGRATION-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        var id = (await Json(await Add(admin, item, DemoIdentitySeed.OrganizationA, code))).GetProperty("result").GetProperty("resourceId").GetGuid();
        await using var db = fixture.Db();
        if(db.Database.IsNpgsql()) {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>db.GetService<IMigrator>().MigrateAsync("20261028220406_InAppNoticeOutbox"));
            Assert.True(await db.ItemBarcodes.AnyAsync(x=>x.Id==id && x.CanonicalValue==code));
            Assert.True(await PostgresMigrations.IsReadyAsync(db,default));return;
        }
        var error = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20261028220406_InAppNoticeOutbox"));
        Assert.Equal(51015, error.Number); Assert.True(await db.ItemBarcodes.AnyAsync(x => x.Id == id && x.CanonicalValue == code));
        Assert.Contains("20261029042636_CustomerItemBarcodes", await db.Database.GetAppliedMigrationsAsync());
        // EF may remove later empty additive migrations before the barcode history guard refuses its own Down.
        await db.GetService<IMigrator>().MigrateAsync(); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
    [Fact]
    public async Task Barcode_identity_is_customer_scoped_and_scan_does_not_post_stock_or_create_requests()
    {
        using var admin = await fixture.Login(); using var other = await fixture.Login("admin.b@example.test"); using var clerk = await fixture.Login("clerk@example.test");
        var item = await Item(admin, DemoIdentitySeed.OrganizationA); var foreignItem = await Item(other, DemoIdentitySeed.OrganizationB);
        var code = "LABEL-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.OK, (await Add(admin, item, DemoIdentitySeed.OrganizationA, code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Add(other, foreignItem, DemoIdentitySeed.OrganizationB, code)).StatusCode);
        var lease = await Lease(); var lookup = await admin.Send(HttpMethod.Get, Lookup(lease, code.ToLowerInvariant())); Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        var result = await Json(lookup); Assert.Equal(item, result.GetProperty("itemId").GetGuid()); Assert.True(result.GetProperty("selectionOnly").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await clerk.Send(HttpMethod.Get, Lookup(lease, code))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Send(HttpMethod.Get, Lookup(lease, code))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clerk.Send(HttpMethod.Get, Lookup(await Lease(DemoIdentitySeed.FacilityB), code))).StatusCode);
        await using var db = fixture.Db(); Assert.Empty(await db.StockLedger.ToArrayAsync()); Assert.Empty(await db.StockRequests.ToArrayAsync());
    }
    [Fact]
    public async Task Registration_requires_customer_admin_and_replays_exact_command_without_duplicate_alias()
    {
        using var admin = await fixture.Login(); using var member = await fixture.Login("member.a@example.test"); using var clerk = await fixture.Login("clerk@example.test");
        var item = await Item(admin, DemoIdentitySeed.OrganizationA); var code = "AUTH-" + Guid.NewGuid().ToString("N"); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await Add(member, item, DemoIdentitySeed.OrganizationA, code)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Add(clerk, item, DemoIdentitySeed.OrganizationA, code)).StatusCode);
        var created = await Add(admin, item, DemoIdentitySeed.OrganizationA, code, key: key); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await (await Add(admin, item, DemoIdentitySeed.OrganizationA, code, key: key)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await Add(admin, item, DemoIdentitySeed.OrganizationA, code + "-CHANGED", key: key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.Send(HttpMethod.Get, $"/api/v1/items/{item}/barcodes?organizationId={DemoIdentitySeed.OrganizationA}")).StatusCode);
    }
    [Fact]
    public async Task Disabled_code_cannot_be_reassigned_and_stale_versions_cannot_change_state()
    {
        using var admin = await fixture.Login(); var item = await Item(admin, DemoIdentitySeed.OrganizationA); var other = await Item(admin, DemoIdentitySeed.OrganizationA);
        var code = "STATE-" + Guid.NewGuid().ToString("N"); var created = (await Json(await Add(admin, item, DemoIdentitySeed.OrganizationA, code))).GetProperty("result");
        var id = created.GetProperty("resourceId").GetGuid(); var version = created.GetProperty("version").GetString()!; var lease = await Lease();
        object Change(bool active) => new { organizationId = DemoIdentitySeed.OrganizationA, active, reason = "Label lifecycle fixture" };
        var disabled = await admin.Send(HttpMethod.Post, $"/api/v1/barcodes/{id}/status", Change(false), version: version, key: Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, Lookup(lease, code))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Add(admin, other, DemoIdentitySeed.OrganizationA, code)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.Send(HttpMethod.Post, $"/api/v1/barcodes/{id}/status", Change(true), version: version, key: Guid.NewGuid())).StatusCode);
        var next = (await Json(disabled)).GetProperty("result").GetProperty("version").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Post, $"/api/v1/barcodes/{id}/status", Change(true), version: next, key: Guid.NewGuid())).StatusCode);
        await using var db = fixture.Db(); (await db.Items.SingleAsync(x => x.Id == item)).Active = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, Lookup(lease, code))).StatusCode);
    }
    [Fact]
    public async Task Concurrent_duplicate_registration_commits_only_one_mapping()
    {
        using var first = await fixture.Login(); using var second = await fixture.Login(); var item = await Item(first, DemoIdentitySeed.OrganizationA); var other = await Item(first, DemoIdentitySeed.OrganizationA); var code = "RACE-" + Guid.NewGuid().ToString("N");
        var results = await Task.WhenAll(Add(first, item, DemoIdentitySeed.OrganizationA, code), Add(second, other, DemoIdentitySeed.OrganizationA, code));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Gtin_formats_are_canonical_and_invalid_control_inputs_fail()
    {
        using var admin = await fixture.Login(); var item = await Item(admin, DemoIdentitySeed.OrganizationA);
        Assert.Equal(HttpStatusCode.OK, (await Add(admin, item, DemoIdentitySeed.OrganizationA, "4006381333931", "GTIN")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Add(admin, item, DemoIdentitySeed.OrganizationA, "04006381333931", "GTIN")).StatusCode);
        var result = await Json(await admin.Send(HttpMethod.Get, Lookup(await Lease(), "4006381333931", "GTIN"))); Assert.Equal("04006381333931", result.GetProperty("canonicalValue").GetString());
        foreach (var code in new[] { "4006381333932", "0000000000000", "１２３４５６７８", "123\n4567" }) Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(admin, item, DemoIdentitySeed.OrganizationA, code, "GTIN")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(admin, item, DemoIdentitySeed.OrganizationA, "https://external.test/")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Add(admin, item, DemoIdentitySeed.OrganizationA, "label\tcode")).StatusCode);
    }
    [Fact]
    public async Task Mapping_identity_and_history_cannot_be_rewritten_or_deleted_by_application_context()
    {
        using var admin = await fixture.Login(); var item = await Item(admin, DemoIdentitySeed.OrganizationA); var other = await Item(admin, DemoIdentitySeed.OrganizationA);
        var id = (await Json(await Add(admin,item,DemoIdentitySeed.OrganizationA,"IMMUTABLE-"+Guid.NewGuid().ToString("N")))).GetProperty("result").GetProperty("resourceId").GetGuid();
        await using (var db=fixture.Db()) { var alias=await db.ItemBarcodes.SingleAsync(x=>x.Id==id); alias.ItemId=other; await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync()); }
        await using (var db=fixture.Db()) { db.ItemBarcodes.Remove(await db.ItemBarcodes.SingleAsync(x=>x.Id==id)); await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync()); }
    }
    [Fact]
    public async Task Required_versions_current_membership_and_foreign_item_references_are_checked()
    {
        using var admin=await fixture.Login(); using var foreign=await fixture.Login("admin.b@example.test"); using var member=await fixture.Login("member.a@example.test");
        var item=await Item(admin,DemoIdentitySeed.OrganizationA); var other=await Item(foreign,DemoIdentitySeed.OrganizationB); var code="SCOPED-"+Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.NotFound,(await Add(admin,other,DemoIdentitySeed.OrganizationA,code)).StatusCode);
        var data=(await Json(await Add(admin,item,DemoIdentitySeed.OrganizationA,code))).GetProperty("result");var id=data.GetProperty("resourceId").GetGuid();
        Assert.Equal(HttpStatusCode.PreconditionRequired,(await admin.Send(HttpMethod.Post,$"/api/v1/barcodes/{id}/status",new {organizationId=DemoIdentitySeed.OrganizationA,active=false,reason="Missing version"},key:Guid.NewGuid())).StatusCode);
        var lease=await Lease(); Assert.Equal(HttpStatusCode.OK,(await member.Send(HttpMethod.Get,Lookup(lease,code))).StatusCode);
        await using(var db=fixture.Db()){(await db.Memberships.SingleAsync(x=>x.UserId=="member.a@example.test"&&x.OrganizationId==DemoIdentitySeed.OrganizationA)).Active=false;await db.SaveChangesAsync();}
        Assert.Equal(HttpStatusCode.NotFound,(await member.Send(HttpMethod.Get,Lookup(lease,code))).StatusCode);
        await using(var db=fixture.Db()){(await db.Memberships.SingleAsync(x=>x.UserId=="member.a@example.test"&&x.OrganizationId==DemoIdentitySeed.OrganizationA)).Active=true;await db.SaveChangesAsync();}
    }

}
