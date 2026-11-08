using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class SqlPrivateDocumentTests : IClassFixture<IdentitySqlFixture>
{
    private readonly IdentitySqlFixture fixture;
    public SqlPrivateDocumentTests(IdentitySqlFixture fixture) => this.fixture = fixture;
    // Small static synthetic PDF; delivery always uses attachment + sandbox, never inline.
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
    private async Task<Guid> Lease()
    {
        await using var seed = fixture.Db(); var original = await seed.RentableUnits.SingleAsync(x => x.Id == SqlFacilitiesTests.UnitA);
        var unit = new WarehouseRent.Infrastructure.Facilities.RentableUnit { Id = Guid.NewGuid(), Code = "DOC-" + Guid.NewGuid().ToString("N")[..12], FacilityId = original.FacilityId, ZoneId = original.ZoneId, AreaM2 = 10, Published = true };
        seed.RentableUnits.Add(unit); await seed.SaveChangesAsync();
        var grantId = await fixture.SeedOccupancyOwner(unit.Id, new DateOnly(2038, 1, 1), new DateOnly(2038, 2, 1));
        await using var db = fixture.Db(); return await db.LeaseUnits.Where(x => x.Id == grantId).Select(x => x.LeaseId).SingleAsync();
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    private Task<HttpResponseMessage> Upload(BrowserSession browser, Guid lease, byte[]? bytes = null, string name = "handover.pdf", string mime = "application/pdf", Guid? key = null) =>
        browser.Send(HttpMethod.Post, $"/api/v1/leases/{lease}/documents", new { fileName = name, contentType = mime, contentBase64 = Convert.ToBase64String(bytes ?? Pdf), reason = "Synthetic private attachment" }, key: key ?? Guid.NewGuid());

    [Fact]
    public async Task Documents_are_private_idempotent_and_revocation_is_rechecked_before_download()
    {
        var lease = await Lease(); using var admin = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        var key = Guid.NewGuid(); var upload = await Upload(admin, lease, key: key); Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(await upload.Content.ReadAsStringAsync(), await (await Upload(admin, lease, key: key)).Content.ReadAsStringAsync());
        var id = (await Json(upload)).GetProperty("result").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/documents/{id}/download")).StatusCode);
        var download = await admin.Send(HttpMethod.Get, $"/api/v1/documents/{id}/download"); Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync()); Assert.Equal("nosniff", download.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(HttpStatusCode.OK, (await admin.Send(HttpMethod.Post, $"/api/v1/documents/{id}/revoke", new { reason = "Withdraw private document" }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Send(HttpMethod.Get, $"/api/v1/documents/{id}/download")).StatusCode);
    }

    [Fact]
    public async Task Current_membership_revocation_denies_an_already_known_download_id()
    {
        var lease = await Lease(); using var admin = await fixture.Login(); using var member = await fixture.Login("member.a@example.test");
        var id = (await Json(await Upload(admin, lease))).GetProperty("result").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await member.Send(HttpMethod.Get, $"/api/v1/documents/{id}/download")).StatusCode);
        await using (var db = fixture.Db()) { var membership = await db.Memberships.SingleAsync(x => x.UserId == "member.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA); membership.Active = false; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.NotFound, (await member.Send(HttpMethod.Get, $"/api/v1/documents/{id}/download")).StatusCode);
        await using var restore = fixture.Db(); (await restore.Memberships.SingleAsync(x => x.UserId == "member.a@example.test" && x.OrganizationId == DemoIdentitySeed.OrganizationA)).Active = true; await restore.SaveChangesAsync();
    }

    [Theory]
    [InlineData("../../outside.pdf", "application/pdf", "%PDF-1.4\n%%EOF")]
    [InlineData("unsafe.svg", "image/svg+xml", "<svg/>")]
    [InlineData("fake.png", "image/png", "<html>not an image</html>")]
    [InlineData("active.pdf", "application/pdf", "%PDF-1.4 /JavaScript /OpenAction %%EOF")]
    [InlineData("truncated.pdf", "application/pdf", "%PDF-1.4 missing trailer")]
    public async Task Invalid_or_active_content_and_unsafe_names_are_rejected(string name, string mime, string value)
    {
        var lease = await Lease(); using var admin = await fixture.Login();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Upload(admin, lease, System.Text.Encoding.ASCII.GetBytes(value), name, mime)).StatusCode);
    }

    [Fact]
    public async Task Oversized_and_foreign_uploads_fail_without_document_metadata_leaks()
    {
        var lease = await Lease(); using var admin = await fixture.Login(); using var foreign = await fixture.Login("admin.b@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await Upload(foreign, lease)).StatusCode);
        var oversized = new byte[10 * 1024 * 1024 + 1];
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Upload(admin, lease, oversized)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/leases/{lease}/documents")).StatusCode);
    }
}
