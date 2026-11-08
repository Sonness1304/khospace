using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace WarehouseRent.Api.Features.Documents;

public static partial class DocumentContent
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public static bool Valid(string? name, string? mime, byte[] bytes)
    {
        if (name is null || name.Length > 150 || !SafeName().IsMatch(name) || bytes.Length is 0 or > MaxBytes) return false;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return (mime, extension) switch
        {
            ("image/png", ".png") => Png(bytes),
            ("image/jpeg", ".jpg" or ".jpeg") => Jpeg(bytes),
            ("application/pdf", ".pdf") => Pdf(bytes),
            _ => false
        };
    }
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _.()-]*$")]
    private static partial Regex SafeName();
    [GeneratedRegex(@"#[0-9a-fA-F]{2}")]
    private static partial Regex PdfEscape();
    private static bool Pdf(byte[] bytes)
    {
        if (bytes.Length < 25 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return false;
        var value = Encoding.Latin1.GetString(bytes);
        if (!value.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal) || !value.Contains("obj", StringComparison.Ordinal)) return false;
        // This detects common active names, not a malware scanner or full PDF interpreter.
        value = PdfEscape().Replace(value, m => ((char)Convert.ToInt32(m.Value[1..], 16)).ToString());
        return !new[] { "/JavaScript", "/JS", "/OpenAction", "/AA", "/Launch", "/EmbeddedFile", "/RichMedia", "/XFA" }
            .Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
    private static bool Dimensions(uint width, uint height) => width is > 0 and <= 20000 && height is > 0 and <= 20000 && (ulong)width * height <= 40_000_000;
    private static bool Png(byte[] bytes)
    {
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        var offset = 8; var first = true; var data = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)); if (length > bytes.Length - offset - 12) return false;
            var count = (int)length; var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            var crc = uint.MaxValue;
            foreach (var b in bytes.AsSpan(offset + 4, count + 4)) { crc ^= b; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0); }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + count))) return false;
            if (first && (type != "IHDR" || count != 13 || !Dimensions(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8)), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 12))))) return false;
            if (!first && type == "IHDR") return false;
            if (type == "IDAT") data = true;
            offset += count + 12; first = false;
            if (type == "IEND") return data && count == 0 && offset == bytes.Length;
        }
        return false;
    }
    private static bool Jpeg(byte[] bytes)
    {
        if (bytes.Length < 20 || bytes[0] != 255 || bytes[1] != 216 || bytes[^2] != 255 || bytes[^1] != 217) return false;
        var offset = 2; var dimensions = false; var components = 0;
        while (offset < bytes.Length - 2)
        {
            if (bytes[offset++] != 255) return false;
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) return false; var marker = bytes[offset++];
            if (marker == 218)
            {
                if (!dimensions || offset + 3 >= bytes.Length - 2) return false;
                var scanLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)); var count = bytes[offset + 2];
                return count > 0 && count <= components && scanLength == 6 + 2 * count && offset + scanLength < bytes.Length - 2;
            }
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));
            if (length < 2 || offset + length > bytes.Length - 2) return false;
            if (marker is 192 or 193 or 194)
            {
                if (length < 11 || !Dimensions(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3)))) return false;
                components = bytes[offset + 7];
                if (components is not (1 or 3 or 4) || length != 8 + 3 * components) return false;
                dimensions = true;
            }
            offset += length;
        }
        return false;
    }
}
