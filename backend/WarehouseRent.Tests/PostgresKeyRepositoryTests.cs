using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WarehouseRent.Api;

namespace WarehouseRent.Tests;

public sealed class PostgresKeyRepositoryTests
{
    private static ServiceProvider Services(string connection, string rootKey)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton(new CloudKeyEncryption(rootKey));
        services.AddSingleton(new PostgresDataProtectionRepository(connection));
        services.AddDataProtection().SetApplicationName("WarehouseRent.Cloud");
        services.AddOptions<KeyManagementOptions>().Configure<PostgresDataProtectionRepository, CloudKeyEncryption>(
            (options, repository, encryption) => { options.XmlRepository=repository; options.XmlEncryptor=encryption; });
        return services.BuildServiceProvider();
    }
    [Fact]
    public async Task Encrypted_key_ring_survives_independent_postgres_hosts_and_has_no_plaintext_master_key()
    {
        await using var fixture=await PostgresRuntimeFixture.Create();
        await using(var sql=new NpgsqlConnection(fixture.Connection)) { await sql.OpenAsync(); await using var ddl=new NpgsqlCommand(PostgresDataProtectionRepository.SchemaSql,sql); await ddl.ExecuteNonQueryAsync(); }
        var root=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); string token;
        using(var first=Services(fixture.Connection,root)) {
            var repository=first.GetRequiredService<PostgresDataProtectionRepository>();
            repository.Validate(first.GetRequiredService<CloudKeyEncryption>());
            token=first.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Protect("synthetic-key-restart");
            Assert.NotEmpty(repository.GetAllElements());
            Assert.All(repository.GetAllElements(),x=>Assert.DoesNotContain("<masterKey",x.ToString()));
        }
        using(var second=Services(fixture.Connection,root)) {
            second.GetRequiredService<PostgresDataProtectionRepository>().Validate(second.GetRequiredService<CloudKeyEncryption>());
            Assert.Equal("synthetic-key-restart",second.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture").Unprotect(token));
        }
        using(var wrong=Services(fixture.Connection,Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))))
            Assert.ThrowsAny<Exception>(()=>wrong.GetRequiredService<PostgresDataProtectionRepository>().Validate(wrong.GetRequiredService<CloudKeyEncryption>()));
        await using var connection=new NpgsqlConnection(fixture.Connection); await connection.OpenAsync();
        await using var mutate=new NpgsqlCommand("UPDATE khospace.\"DataProtectionKeys\" SET \"FriendlyName\"='mutated'",connection);
        var error=await Assert.ThrowsAsync<PostgresException>(()=>mutate.ExecuteNonQueryAsync()); Assert.Equal("23514",error.SqlState);
        await using var remove=new NpgsqlCommand("DELETE FROM khospace.\"DataProtectionKeys\"",connection);
        var deleteError=await Assert.ThrowsAsync<PostgresException>(()=>remove.ExecuteNonQueryAsync()); Assert.Equal("23514",deleteError.SqlState);
    }
    [Fact]
    public async Task Missing_schema_and_plaintext_or_malformed_elements_are_refused()
    {
        await using var fixture=await PostgresRuntimeFixture.Create();
        var repository=new PostgresDataProtectionRepository(fixture.Connection);
        Assert.Throws<InvalidOperationException>(()=>repository.GetAllElements());
        Assert.Throws<InvalidOperationException>(()=>repository.StoreElement(new XElement("key",new XElement("masterKey","secret")),"fixture"));
        Assert.Throws<InvalidOperationException>(()=>repository.StoreElement(new XElement("key",new XAttribute("version","1")),"fixture"));
    }
}
