using WarehouseRent.Api.Features.Documents;
namespace WarehouseRent.Tests;
public sealed class DocumentContentTests
{
    [Fact]
    public void Png_accepts_real_small_image_and_rejects_crc_damage_trailing_payload_and_mime_mismatch()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGD4DwABBAEAX+XDSwAAAABJRU5ErkJggg==");
        Assert.True(DocumentContent.Valid("one.png", "image/png", png));
        Assert.False(DocumentContent.Valid("one.jpg", "image/png", png));
        var broken = (byte[])png.Clone(); broken[30] ^= 1; Assert.False(DocumentContent.Valid("broken.png", "image/png", broken));
        Assert.False(DocumentContent.Valid("polyglot.png", "image/png", [..png, 60, 115, 118, 103, 62]));
    }
    [Theory]
    [InlineData("%PDF-1.4 obj /Java#53cript %%EOF")]
    [InlineData("%PDF-1.4 obj /OpenAction %%EOF")]
    public void Pdf_active_name_checks_handle_hex_escapes(string value) =>
        Assert.False(DocumentContent.Valid("active.pdf", "application/pdf", System.Text.Encoding.ASCII.GetBytes(value)));
    [Fact]
    public void Jpeg_rejects_zero_components_and_malformed_scan_length()
    {
        var malformed = Convert.FromHexString("FFD8FFC00008080001000100FFDA00000102FFD9");
        Assert.False(DocumentContent.Valid("broken.jpg", "image/jpeg", malformed));
    }

}
