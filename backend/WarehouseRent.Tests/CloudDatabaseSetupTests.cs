using Microsoft.Data.SqlClient;
using WarehouseRent.Api;

namespace WarehouseRent.Tests;

public sealed class CloudDatabaseSetupTests
{
    private const string Connection = "Server=sql.railway.internal;Database=WarehouseRent_Cloud_Main;User ID=runtime;Password=synthetic-not-real;Encrypt=True;TrustServerCertificate=True";

    [Fact]
    public void Explicit_cloud_target_validates_without_opening_SQL()
    {
        CloudDatabaseSetup.Validate("Production", Connection, "WarehouseRent_Cloud_Main", "owner@example.test", "SyntheticCloudSetup!12345");
    }

    [Theory]
    [InlineData("Demo", "WarehouseRent_Cloud_Main")]
    [InlineData("Testing", "WarehouseRent_Cloud_Main")]
    [InlineData("Production", "WarehouseRent_Demo")]
    [InlineData("Production", "WarehouseRent_Test_Any")]
    [InlineData("Production", "master")]
    [InlineData("Production", "WarehouseRent_Cloud_Main;drop")]
    public void Initialization_rejects_other_environment_or_catalog(string environment, string name)
    {
        var connection = new SqlConnectionStringBuilder(Connection) { InitialCatalog = name }.ConnectionString;
        Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate(environment, connection, name, "owner@example.test", "SyntheticCloudSetup!12345"));
    }

    [Fact]
    public void Initialization_requires_exact_database_confirmation_and_transport()
    {
        Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate("Production", Connection, "WarehouseRent_Cloud_Other", "owner@example.test", "SyntheticCloudSetup!12345"));
        var clear = new SqlConnectionStringBuilder(Connection) { Encrypt = SqlConnectionEncryptOption.Optional }.ConnectionString;
        Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate("Production", clear, "WarehouseRent_Cloud_Main", "owner@example.test", "SyntheticCloudSetup!12345"));
        var publicUntrusted = new SqlConnectionStringBuilder(Connection) { DataSource = "sql.public.example" }.ConnectionString;
        Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate("Production", publicUntrusted, "WarehouseRent_Cloud_Main", "owner@example.test", "SyntheticCloudSetup!12345"));
    }

    [Theory]
    [InlineData(@"public.example\sql.railway.internal")]
    [InlineData("tcp:sql.railway.internal")]
    [InlineData("sql.railway.internal,1433")]
    public void Certificate_exception_requires_a_bare_private_DNS_hostname(string server)
    {
        var connection = new SqlConnectionStringBuilder(Connection) { DataSource = server }.ConnectionString;
        Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate("Production", connection,
            "WarehouseRent_Cloud_Main", "owner@example.test", "SyntheticCloudSetup!12345"));
    }

    [Theory]
    [InlineData("", "SyntheticCloudSetup!12345")]
    [InlineData("not-email", "SyntheticCloudSetup!12345")]
    [InlineData("owner@example.test", "short")]
    public void Initialization_refuses_missing_private_bootstrap_credentials(string email, string password)
    {
        var error = Assert.Throws<InvalidOperationException>(() => CloudDatabaseSetup.Validate("Production", Connection, "WarehouseRent_Cloud_Main", email, password));
        Assert.DoesNotContain("synthetic-not-real", error.Message);
        Assert.DoesNotContain(Connection, error.Message);
    }
}
