using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace WarehouseRent.Api;

// Root key is supplied privately by the host, never persisted alongside the SQL key ring.
public sealed class CloudKeyEncryption : IXmlEncryptor, IDisposable
{
    private readonly byte[] key;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("WarehouseRent.Cloud.KeyRing.AES256GCM.v1");

    public CloudKeyEncryption(string encodedKey)
    {
        try { key = Convert.FromBase64String(encodedKey); }
        catch (FormatException) { throw InvalidConfiguration(); }
        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw InvalidConfiguration();
        }
    }

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
            return new EncryptedXmlInfo(new XElement("aes256gcm", new XAttribute("version", "1"),
                new XElement("nonce", Convert.ToBase64String(nonce)),
                new XElement("tag", Convert.ToBase64String(tag)),
                new XElement("ciphertext", Convert.ToBase64String(ciphertext))), typeof(CloudKeyXmlDecryptor));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public XElement Decrypt(XElement encryptedElement)
    {
        byte[]? plaintext = null;
        try
        {
            if (encryptedElement.Name != "aes256gcm" || (string?)encryptedElement.Attribute("version") != "1")
                throw new CryptographicException();
            var nonce = Convert.FromBase64String(encryptedElement.Element("nonce")!.Value);
            var tag = Convert.FromBase64String(encryptedElement.Element("tag")!.Value);
            var ciphertext = Convert.FromBase64String(encryptedElement.Element("ciphertext")!.Value);
            if (nonce.Length != 12 || tag.Length != 16 || ciphertext.Length is < 1 or > 32768)
                throw new CryptographicException();
            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);
            using var reader = XmlReader.Create(new StringReader(Encoding.UTF8.GetString(plaintext)), SafeXml());
            return XElement.Load(reader);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new CryptographicException("Cloud key decryption failed."); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static XmlReaderSettings SafeXml() => new()
    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32768 };
    private static InvalidOperationException InvalidConfiguration() => new("Cloud key encryption configuration is invalid.");
    public void Dispose() => CryptographicOperations.ZeroMemory(key);
}

// DataProtection activates this type when reading the encryptedSecret descriptor on a new host.
public sealed class CloudKeyXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement) => services.GetRequiredService<CloudKeyEncryption>().Decrypt(encryptedElement);
}
