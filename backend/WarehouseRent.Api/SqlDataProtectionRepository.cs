using System.Data;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.SqlClient;
using WarehouseRent.Infrastructure.Persistence;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("WarehouseRent.Tests")]

namespace WarehouseRent.Api;

// Dedicated bounded, append-only SQL repository; no scoped Identity/EF services or filesystem fallback.
public sealed class SqlDataProtectionRepository(string connectionString) : IXmlRepository
{
    private readonly string connection = SqlConfiguration.BoundedConnection(connectionString);
    private const int MaxCharacters = 32768;

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        try
        {
            using var sql = new SqlConnection(connection);
            sql.Open();
            using var command = new SqlCommand("SELECT TOP (1025) Xml FROM dbo.DataProtectionKeys ORDER BY CreatedAt, Id", sql) { CommandTimeout = 3 };
            using var rows = command.ExecuteReader();
            var elements = new List<XElement>();
            while (rows.Read())
            {
                if (elements.Count >= 1024) throw new InvalidOperationException();
                var xml = rows.GetString(0);
                if (xml.Length is < 1 or > MaxCharacters) throw new InvalidOperationException();
                using var reader = XmlReader.Create(new StringReader(xml), CloudKeyEncryption.SafeXml());
                var element = XElement.Load(reader);
                // Validate every read: XmlKeyManager otherwise silently skips malformed key metadata.
                ValidateElement(element);
                elements.Add(element);
            }
            return elements;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new InvalidOperationException("Cloud key store is unavailable or invalid."); }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        try
        {
            var xml = element.ToString(SaveOptions.DisableFormatting);
            if (xml.Length is < 1 or > MaxCharacters || string.IsNullOrEmpty(friendlyName) || friendlyName.Length > 200)
                throw new InvalidOperationException();
            // Never write a plaintext master key, including accidental encryptor misconfiguration.
            if (element.DescendantsAndSelf().Any(x => x.Name.LocalName == "masterKey")) throw new InvalidOperationException();
            using var sql = new SqlConnection(connection);
            sql.Open();
            using var command = new SqlCommand("INSERT dbo.DataProtectionKeys (Id, FriendlyName, Xml, CreatedAt) VALUES (@id, @name, @xml, SYSUTCDATETIME())", sql)
            { CommandTimeout = 3 };
            command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = Guid.NewGuid();
            command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = friendlyName;
            command.Parameters.Add("@xml", SqlDbType.NVarChar, -1).Value = xml;
            command.ExecuteNonQuery();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new InvalidOperationException("Cloud key store write failed."); }
    }

    public void Validate(CloudKeyEncryption encryption)
    {
        foreach (var element in GetAllElements()) ValidateElement(element, encryption);
    }

    internal static void ValidateElement(XElement element, CloudKeyEncryption? encryption = null)
    {
        try
        {
            if ((string?)element.Attribute("version") != "1"
                || element.DescendantsAndSelf().Any(x => x.Name.LocalName == "masterKey"))
                throw new InvalidOperationException();
            if (element.Name == "revocation")
            {
                RequireChildren(element, "revocationDate", "key", "reason");
                ReadDate(element, "revocationDate");
                var revoked = element.Element("key")!;
                if (revoked.HasElements || ((string?)revoked.Attribute("id") != "*" && !ValidId((string?)revoked.Attribute("id"))))
                    throw new InvalidOperationException();
                if (element.Element("reason")!.HasElements) throw new InvalidOperationException();
                return;
            }
            if (element.Name != "key" || !ValidId((string?)element.Attribute("id")))
                throw new InvalidOperationException();
            RequireChildren(element, "creationDate", "activationDate", "expirationDate", "descriptor");
            ReadDate(element, "creationDate");
            ReadDate(element, "activationDate");
            ReadDate(element, "expirationDate");
            var descriptor = element.Element("descriptor")!;
            if (!MatchesType((string?)descriptor.Attribute("deserializerType"), typeof(AuthenticatedEncryptorDescriptorDeserializer)))
                throw new InvalidOperationException();
            RequireChildren(descriptor, "descriptor");
            var payload = descriptor.Element("descriptor")!;
            var algorithm = payload.Elements("encryption").Single();
            if (algorithm.HasElements || !Enum.TryParse<EncryptionAlgorithm>((string?)algorithm.Attribute("algorithm"), out var cipher)
                || !Enum.IsDefined(cipher)) throw new InvalidOperationException();
            var secretName = XName.Get("encryptedSecret", "http://schemas.asp.net/2015/03/dataProtection");
            if (cipher is EncryptionAlgorithm.AES_128_GCM or EncryptionAlgorithm.AES_192_GCM or EncryptionAlgorithm.AES_256_GCM)
                RequireChildren(payload, "encryption", secretName);
            else
            {
                RequireChildren(payload, "encryption", "validation", secretName);
                var validation = payload.Element("validation")!;
                if (validation.HasElements || !Enum.TryParse<ValidationAlgorithm>((string?)validation.Attribute("algorithm"), out var hash)
                    || !Enum.IsDefined(hash)) throw new InvalidOperationException();
            }
            var secret = payload.Element(secretName)!;
            // Match a fixed identity without loading or activating a type selected by persisted data.
            if (!MatchesType((string?)secret.Attribute("decryptorType"), typeof(CloudKeyXmlDecryptor)))
                throw new InvalidOperationException();
            RequireChildren(secret, "aes256gcm");
            var encrypted = secret.Element("aes256gcm")!;
            if ((string?)encrypted.Attribute("version") != "1") throw new InvalidOperationException();
            RequireChildren(encrypted, "nonce", "tag", "ciphertext");
            foreach (var field in encrypted.Elements()) if (field.HasElements) throw new InvalidOperationException();
            if (Convert.FromBase64String(encrypted.Element("nonce")!.Value).Length != 12
                || Convert.FromBase64String(encrypted.Element("tag")!.Value).Length != 16
                || Convert.FromBase64String(encrypted.Element("ciphertext")!.Value).Length is < 1 or > 32768)
                throw new InvalidOperationException();
            if (encryption is not null)
            {
                var masterKey = encryption.Decrypt(encrypted);
                if (masterKey.Name != "masterKey") throw new InvalidOperationException();
                RequireChildren(masterKey, "value");
                if (masterKey.Element("value")!.HasElements) throw new InvalidOperationException();
                var bytes = Convert.FromBase64String(masterKey.Value);
                try { if (bytes.Length != 64) throw new InvalidOperationException(); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new InvalidOperationException("Cloud key store is invalid."); }
    }

    private static bool ValidId(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty;

    private static void ReadDate(XElement element, XName name)
    {
        var date = element.Element(name)!;
        if (date.HasElements) throw new InvalidOperationException();
        _ = XmlConvert.ToDateTimeOffset(date.Value);
    }

    private static void RequireChildren(XElement element, params XName[] names)
    {
        if (element.Elements().Count() != names.Length || names.Any(name => element.Elements(name).Count() != 1))
            throw new InvalidOperationException();
    }

    private static bool MatchesType(string? value, Type expected)
    {
        if (value is null) return false;
        var parts = value.Split(',', 2);
        if (parts.Length != 2 || parts[0].Trim() != expected.FullName) return false;
        var assembly = new AssemblyName(parts[1].Trim());
        var known = expected.Assembly.GetName();
        // Assembly versions may change during a normal framework/application update.
        return assembly.Name == known.Name && (assembly.CultureName ?? "") == (known.CultureName ?? "")
            && (assembly.GetPublicKeyToken() ?? []).SequenceEqual(known.GetPublicKeyToken() ?? []);
    }
}
