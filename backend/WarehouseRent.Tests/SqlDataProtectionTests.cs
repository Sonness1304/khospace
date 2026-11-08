using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class SqlDataProtectionTests
{
    private static readonly string RootKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
    private static WebApplicationFactory<Program> Host(string connection, Dictionary<string, string?>? overrides = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var pair in new Dictionary<string, string?>
            {
                ["CloudHosting:Enabled"] = "true",
                ["CloudHosting:PublicOrigin"] = "https://warehouse.example.com",
                ["CloudHosting:BackendHost"] = "api.example.com",
                ["CloudHosting:SharedProxyKey"] = "Synthetic_Cloud_Proxy_Key_Only_1234567890",
                ["CloudHosting:DataProtectionStore"] = "SqlServer",
                ["CloudHosting:DataProtectionEncryptionKey"] = RootKey,
                ["ConnectionStrings:WarehouseRent"] = connection
            }.Concat(overrides ?? [])) builder.UseSetting(pair.Key, pair.Value);
        });

    [Theory]
    [InlineData("")]
    [InlineData("SqlSever")]
    [InlineData("Redis")]
    public void Unknown_store_is_rejected_even_if_file_path_is_valid(string store)
    {
        using var host = Host("Server=localhost;Database=WarehouseRent_Demo_Test_fixture;Integrated Security=True", new()
        {
            ["CloudHosting:DataProtectionStore"] = store,
            ["CloudHosting:DataProtectionPath"] = Path.Combine(Path.GetTempPath(), $"WarehouseRent_CloudKeys_{Guid.NewGuid():N}")
        });
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")]
    public void Sql_store_requires_valid_private_root_key(string? key)
    {
        using var host = Host("Server=localhost;Database=WarehouseRent_Demo_Test_fixture;Integrated Security=True", new()
        {
            ["CloudHosting:DataProtectionEncryptionKey"] = key,
            ["CloudHosting:DataProtectionPath"] = Path.Combine(Path.GetTempPath(), $"WarehouseRent_CloudKeys_{Guid.NewGuid():N}")
        });
        var error = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.DoesNotContain("not-base64", error.Message);
    }

    [Fact]
    public async Task Independent_hosts_restore_encrypted_keys_after_restart_without_disk()
    {
        await InDatabase(async connection =>
        {
            string protectedValue;
            using (var first = Host(connection))
            {
                using var client = first.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://api.example.com") });
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
                protectedValue = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("sql-fixture").Protect("synthetic-fixture");
            }
            await using (var sql = new SqlConnection(connection))
            {
                await sql.OpenAsync();
                await using var command = new SqlCommand("SELECT Xml FROM dbo.DataProtectionKeys", sql);
                await using var rows = await command.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                do
                {
                    var xml = rows.GetString(0);
                    Assert.Contains("encryptedSecret", xml);
                    Assert.DoesNotContain("<masterKey", xml);
                    Assert.DoesNotContain(RootKey, xml);
                } while (await rows.ReadAsync());
            }
            using var second = Host(connection);
            using var next = second.CreateClient();
            Assert.Equal("synthetic-fixture", second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("sql-fixture").Unprotect(protectedValue));
            using var wrongKey = Host(connection, new() { ["CloudHosting:DataProtectionEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
            Assert.ThrowsAny<Exception>(() => wrongKey.CreateClient());
        });
    }

    [Fact]
    public async Task Malformed_persisted_metadata_refuses_repository_reads_and_restart()
    {
        await InDatabase(async connection =>
        {
            XElement key;
            using (var first = Host(connection))
            {
                using var client = first.CreateClient();
                key = new XElement(Assert.Single(first.Services.GetRequiredService<SqlDataProtectionRepository>().GetAllElements()));
            }
            // Append corruption rather than bypassing the append-only trigger, on this owned GUID fixture only.
            key.SetAttributeValue("id", "synthetic-invalid-guid");
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var insert = new SqlCommand("INSERT dbo.DataProtectionKeys (Id, FriendlyName, Xml, CreatedAt) VALUES (NEWID(), 'malformed-fixture', @xml, SYSUTCDATETIME())", sql);
            insert.Parameters.Add("@xml", System.Data.SqlDbType.NVarChar, -1).Value = key.ToString(SaveOptions.DisableFormatting);
            await insert.ExecuteNonQueryAsync();
            Assert.Throws<InvalidOperationException>(() => new SqlDataProtectionRepository(connection).GetAllElements());
            using var restarted = Host(connection);
            Assert.ThrowsAny<Exception>(() => restarted.CreateClient());
        });
    }

    [Fact]
    public async Task Missing_schema_and_outage_refuse_startup_without_creating_keys()
    {
        var missing = new SqlConnectionStringBuilder("Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
        { InitialCatalog = $"WarehouseRent_Demo_Test_{Guid.NewGuid():N}" }.ConnectionString;
        using var host = Host(missing);
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
        using var outage = Host("Server=127.0.0.1,1;Database=WarehouseRent_Demo_Test_unavailable;User Id=synthetic;Password=synthetic;Encrypt=True");
        Assert.ThrowsAny<Exception>(() => outage.CreateClient());
        await InDatabase(async connection =>
        {
            using var working = Host(connection);
            using var client = working.CreateClient();
            var protector = working.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("outage-fixture");
            protector.Protect("before-outage");
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            await using var command = new SqlCommand("DROP TABLE dbo.DataProtectionKeys", sql);
            await command.ExecuteNonQueryAsync();
            using var restarted = Host(connection);
            Assert.ThrowsAny<Exception>(() => restarted.CreateClient());
        });
    }

    [Fact]
    public void Encryption_rejects_tampering_and_uses_unique_authenticated_ciphertexts()
    {
        using var encryption = new CloudKeyEncryption(RootKey);
        var secret = new XElement("masterKey", "synthetic-secret");
        var first = encryption.Encrypt(secret).EncryptedElement;
        var second = encryption.Encrypt(secret).EncryptedElement;
        Assert.NotEqual(first.ToString(), second.ToString());
        Assert.Equal(secret.ToString(), encryption.Decrypt(first).ToString());
        var tag = Convert.FromBase64String(first.Element("tag")!.Value);
        tag[0] ^= 1;
        first.Element("tag")!.Value = Convert.ToBase64String(tag);
        Assert.Throws<CryptographicException>(() => encryption.Decrypt(first));
    }

    [Fact]
    public async Task Sql_repository_and_schema_enforce_append_only_bounded_encrypted_records()
    {
        await InDatabase(async connection =>
        {
            using var host = Host(connection);
            using var client = host.CreateClient();
            var repository = host.Services.GetRequiredService<SqlDataProtectionRepository>();
            var key = Assert.Single(repository.GetAllElements());
            Assert.Throws<InvalidOperationException>(() => repository.StoreElement(new XElement("masterKey", "plaintext"), "bad"));
            Assert.Throws<InvalidOperationException>(() => repository.StoreElement(key, new string('x', 201)));
            Assert.Throws<InvalidOperationException>(() => repository.StoreElement(new XElement("key", new string('x', 32769)), "bad"));
            await using var sql = new SqlConnection(connection);
            await sql.OpenAsync();
            foreach (var statement in new[]
            {
                "UPDATE dbo.DataProtectionKeys SET FriendlyName='modified'",
                "DELETE FROM dbo.DataProtectionKeys",
                "INSERT dbo.DataProtectionKeys VALUES (NEWID(), 'oversized', REPLICATE(CAST('x' AS nvarchar(max)), 32769), SYSUTCDATETIME())"
            })
            {
                await using var command = new SqlCommand(statement, sql);
                await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            }
            Assert.Single(repository.GetAllElements());
            await using (var drop = new SqlCommand("DROP TABLE dbo.DataProtectionKeys", sql)) await drop.ExecuteNonQueryAsync();
            Assert.Throws<InvalidOperationException>(() => repository.GetAllElements());
            Assert.Throws<InvalidOperationException>(() => repository.StoreElement(key, "key-fixture"));
        });
    }

    [Fact]
    public async Task Explicit_setup_migrates_before_resolving_identity_token_services()
    {
        // Service/config split is intentional: validated cloud settings are a non-network fixture;
        // the scoped AppDbContext points only to a disposable GUID-named local synthetic catalog.
        await InDatabase(async connection =>
        {
            using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:WarehouseRent", connection);
            });
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var provider = new SchemaFirstServices(scope.ServiceProvider, db);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudHosting:Enabled"] = "true",
                ["CloudHosting:DatabaseName"] = "WarehouseRent_Cloud_fixture",
                ["CloudHosting:OperatorEmail"] = "fixture-owner@example.test",
                ["CloudHosting:OperatorPassword"] = "SyntheticCloudOwner!12345",
                ["ConnectionStrings:WarehouseRent"] = "Server=fixture.database.windows.net;Database=WarehouseRent_Cloud_fixture;Encrypt=True;TrustServerCertificate=False"
            }).Build();
            await CloudDatabaseSetup.ApplyAsync(provider, config, "Production");
            Assert.True(provider.IdentityResolvedAfterSchema);
            Assert.Single(await db.Users.ToListAsync());
            Assert.Empty(new SqlDataProtectionRepository(connection).GetAllElements());
        }, migrate: false);
    }

    private sealed class SchemaFirstServices(IServiceProvider services, AppDbContext db) : IServiceProvider
    {
        public bool IdentityResolvedAfterSchema { get; private set; }
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(UserManager<AppUser>))
            {
                Assert.Empty(new SqlDataProtectionRepository(db.Database.GetConnectionString()!).GetAllElements());
                IdentityResolvedAfterSchema = true;
            }
            return services.GetService(serviceType);
        }
    }

    [Fact]
    public async Task Downgrade_refuses_to_drop_populated_key_ring()
    {
        await InDatabase(async connection =>
        {
            using var host = Host(connection);
            using var client = host.CreateClient();
            var repository = host.Services.GetRequiredService<SqlDataProtectionRepository>();
            var before = Assert.Single(repository.GetAllElements()).ToString();
            await using var db = new AppDbContext(SqlConfiguration.Options(connection));
            await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20261029104500_CalendarMonthTariffs"));
            Assert.Equal(before, Assert.Single(repository.GetAllElements()).ToString());
            Assert.Contains("20261101080000_SqlDataProtectionKeys", await db.Database.GetAppliedMigrationsAsync());
        });
    }

    [Fact]
    public async Task Key_ring_upgrade_and_empty_downgrade_preserve_existing_business_rows()
    {
        await InDatabase(async connection =>
        {
            await using var db = new AppDbContext(SqlConfiguration.Options(connection));
            db.Database.SetCommandTimeout(30);
            await DatabaseSetup.ApplyAsync(db, "Demo", connection, seedDemo: true);
            await db.GetService<IMigrator>().MigrateAsync("20261029104500_CalendarMonthTariffs");
            var beforeUsers = await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var beforeFacilities = await db.Facilities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            Assert.NotEmpty(beforeUsers);
            Assert.NotEmpty(beforeFacilities);
            var before = System.Text.Json.JsonSerializer.Serialize(new { beforeUsers, beforeFacilities });
            await db.Database.MigrateAsync();
            Assert.Empty(new SqlDataProtectionRepository(connection).GetAllElements());
            await db.GetService<IMigrator>().MigrateAsync("20261029104500_CalendarMonthTariffs");
            var afterUsers = await db.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var afterFacilities = await db.Facilities.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(new { beforeUsers = afterUsers, beforeFacilities = afterFacilities }));
        }, migrate: false);
    }

    private static async Task InDatabase(Func<string, Task> action, bool migrate = true)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WAREHOUSERENT_TEST_SQL")
            ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
        { InitialCatalog = $"WarehouseRent_Demo_Test_{Guid.NewGuid():N}" }.ConnectionString;
        await using var db = new AppDbContext(SqlConfiguration.Options(connection));
        try
        {
            if (migrate) await DatabaseSetup.ApplyAsync(db, "Testing", connection, seedDemo: false);
            await action(connection);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}

// These tests generate the persisted format using the actual framework, without SQL or disk keys.
public sealed class CloudKeyMetadataTests
{
    private static readonly string RootKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
    private static readonly XName SecretName = XName.Get("encryptedSecret", "http://schemas.asp.net/2015/03/dataProtection");

    private sealed class MemoryKeys : Microsoft.AspNetCore.DataProtection.Repositories.IXmlRepository
    {
        public readonly List<XElement> Elements = [];
        public IReadOnlyCollection<XElement> GetAllElements() => Elements.Select(x => new XElement(x)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => Elements.Add(new XElement(element));
    }

    private static XElement FrameworkKey(CloudKeyEncryption encryption)
    {
        var repository = new MemoryKeys();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().AddKeyManagementOptions(options =>
        {
            options.XmlRepository = repository;
            options.XmlEncryptor = encryption;
        });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
        return Assert.Single(repository.Elements);
    }

    [Fact]
    public void Actual_framework_encrypted_key_is_valid()
    {
        using var encryption = new CloudKeyEncryption(RootKey);
        var key = FrameworkKey(encryption);
        SqlDataProtectionRepository.ValidateElement(key, encryption);
    }

    [Fact]
    public void Fixed_type_identity_does_not_pin_framework_or_application_assembly_versions()
    {
        using var encryption = new CloudKeyEncryption(RootKey);
        var key = FrameworkKey(encryption);
        foreach (var attribute in new[] { key.Element("descriptor")!.Attribute("deserializerType")!, key.Descendants(SecretName).Single().Attribute("decryptorType")! })
            attribute.Value = System.Text.RegularExpressions.Regex.Replace(attribute.Value, @"Version=\d+\.\d+\.\d+\.\d+", "Version=99.0.0.0");
        SqlDataProtectionRepository.ValidateElement(key, encryption);
    }

    [Fact]
    public void Authenticated_ciphertext_must_decrypt_to_the_expected_master_key_payload()
    {
        using var encryption = new CloudKeyEncryption(RootKey);
        var key = FrameworkKey(encryption);
        var secret = key.Descendants(SecretName).Single();
        secret.ReplaceNodes(encryption.Encrypt(new XElement("not-a-master-key", "fixture")).EncryptedElement);
        Assert.Throws<InvalidOperationException>(() => SqlDataProtectionRepository.ValidateElement(key, encryption));
    }

    [Theory]
    [InlineData("missing-id")]
    [InlineData("invalid-id")]
    [InlineData("empty-guid")]
    [InlineData("missing-version")]
    [InlineData("unsupported-version")]
    [InlineData("namespaced-root")]
    [InlineData("missing-creationDate")]
    [InlineData("missing-activationDate")]
    [InlineData("missing-expirationDate")]
    [InlineData("invalid-creationDate")]
    [InlineData("invalid-activationDate")]
    [InlineData("invalid-expirationDate")]
    [InlineData("duplicate-date")]
    [InlineData("missing-deserializer")]
    [InlineData("foreign-deserializer")]
    [InlineData("foreign-deserializer-assembly")]
    [InlineData("duplicate-descriptor")]
    [InlineData("extra-payload")]
    [InlineData("duplicate-secret")]
    [InlineData("wrong-secret-namespace")]
    [InlineData("foreign-decryptor")]
    [InlineData("duplicate-cipher-payload")]
    public void Malformed_metadata_is_rejected_before_framework_can_skip_key(string mutation)
    {
        using var encryption = new CloudKeyEncryption(RootKey);
        var key = FrameworkKey(encryption);
        var descriptor = key.Element("descriptor")!;
        var secret = key.Descendants(SecretName).Single();
        switch (mutation)
        {
            case "missing-id": key.Attribute("id")!.Remove(); break;
            case "invalid-id": key.SetAttributeValue("id", "synthetic-invalid-guid"); break;
            case "empty-guid": key.SetAttributeValue("id", Guid.Empty); break;
            case "missing-version": key.Attribute("version")!.Remove(); break;
            case "unsupported-version": key.SetAttributeValue("version", "2"); break;
            case "namespaced-root": key.Name = XName.Get("key", "urn:fixture"); break;
            case "missing-creationDate": key.Element("creationDate")!.Remove(); break;
            case "missing-activationDate": key.Element("activationDate")!.Remove(); break;
            case "missing-expirationDate": key.Element("expirationDate")!.Remove(); break;
            case "invalid-creationDate": key.Element("creationDate")!.Value = "not-a-date"; break;
            case "invalid-activationDate": key.Element("activationDate")!.Value = "not-a-date"; break;
            case "invalid-expirationDate": key.Element("expirationDate")!.Value = "not-a-date"; break;
            case "duplicate-date": key.Add(new XElement(key.Element("creationDate")!)); break;
            case "missing-deserializer": descriptor.Attribute("deserializerType")!.Remove(); break;
            case "foreign-deserializer": descriptor.SetAttributeValue("deserializerType", typeof(string).AssemblyQualifiedName); break;
            case "foreign-deserializer-assembly": descriptor.SetAttributeValue("deserializerType", "Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel.AuthenticatedEncryptorDescriptorDeserializer, SyntheticAssembly"); break;
            case "duplicate-descriptor": key.Add(new XElement(descriptor)); break;
            case "extra-payload": descriptor.Add(new XElement("descriptor")); break;
            case "duplicate-secret": secret.Parent!.Add(new XElement(secret)); break;
            case "wrong-secret-namespace": secret.Name = "encryptedSecret"; break;
            case "foreign-decryptor": secret.SetAttributeValue("decryptorType", typeof(string).AssemblyQualifiedName); break;
            case "duplicate-cipher-payload": secret.Add(new XElement(secret.Elements().Single())); break;
        }
        Assert.Throws<InvalidOperationException>(() => SqlDataProtectionRepository.ValidateElement(key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_framework_single_and_mass_revocations_are_valid(bool mass)
    {
        var repository = new MemoryKeys();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().AddKeyManagementOptions(options => options.XmlRepository = repository);
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>();
        if (mass) manager.RevokeAllKeys(DateTimeOffset.UtcNow, "synthetic fixture");
        else manager.RevokeKey(Guid.NewGuid(), "synthetic fixture");
        SqlDataProtectionRepository.ValidateElement(Assert.Single(repository.Elements));
    }

    [Theory]
    [InlineData("missing-version")]
    [InlineData("missing-date")]
    [InlineData("invalid-date")]
    [InlineData("missing-key")]
    [InlineData("invalid-id")]
    [InlineData("duplicate-key")]
    [InlineData("namespaced-root")]
    public void Malformed_revocations_are_rejected(string mutation)
    {
        var revocation = new XElement("revocation", new XAttribute("version", "1"),
            new XElement("revocationDate", DateTimeOffset.UtcNow),
            new XElement("key", new XAttribute("id", "*")), new XElement("reason", "fixture"));
        switch (mutation)
        {
            case "missing-version": revocation.Attribute("version")!.Remove(); break;
            case "missing-date": revocation.Element("revocationDate")!.Remove(); break;
            case "invalid-date": revocation.Element("revocationDate")!.Value = "not-a-date"; break;
            case "missing-key": revocation.Element("key")!.Remove(); break;
            case "invalid-id": revocation.Element("key")!.SetAttributeValue("id", "not-a-guid"); break;
            case "duplicate-key": revocation.Add(new XElement(revocation.Element("key")!)); break;
            case "namespaced-root": revocation.Name = XName.Get("revocation", "urn:fixture"); break;
        }
        Assert.Throws<InvalidOperationException>(() => SqlDataProtectionRepository.ValidateElement(revocation));
    }
}
