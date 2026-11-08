using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class SqlCloudBootstrapTests
{
    private const string Email = "cloud-owner@example.test";
    private const string Password = "SyntheticCloudOwner!12345";

    [Fact]
    public Task Empty_catalog_gets_one_operator_no_demo_data_and_repeat_is_idempotent() => InEmptyDatabase(async (db, users) =>
    {
        await CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None);
        await CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None);
        var user = Assert.Single(await db.Users.ToListAsync());
        Assert.True(user.OperatorAdmin && user.Active);
        Assert.False(user.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Single(await db.SecurityAudits.Where(x => x.Action == "CloudOperatorInitialized").ToListAsync());
        Assert.Empty(await db.FoundationMarkers.ToListAsync());
        Assert.Empty(await db.Facilities.ToListAsync());
    });

    [Fact]
    public Task Matching_customer_is_never_promoted_or_password_replaced() => InEmptyDatabase(async (db, users) =>
    {
        var customer = new AppUser { UserName = Email, Email = Email };
        Assert.True((await users.CreateAsync(customer, Password)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None));
        await db.Entry(customer).ReloadAsync();
        Assert.False(customer.OperatorAdmin);
        Assert.True(await users.CheckPasswordAsync(customer, Password));
        Assert.Empty(await db.SecurityAudits.ToListAsync());
    });

    [Fact]
    public Task Populated_catalog_cannot_gain_a_bootstrap_administrator() => InEmptyDatabase(async (db, users) =>
    {
        Assert.True((await users.CreateAsync(new AppUser { UserName = "other@example.test", Email = "other@example.test" }, Password)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None));
        Assert.Null(await users.FindByEmailAsync(Email));
        Assert.Single(await db.Users.ToListAsync());
    });

    [Fact]
    public Task Wrong_credentials_or_disabled_operator_cannot_be_reset() => InEmptyDatabase(async (db, users) =>
    {
        await CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, "WrongSyntheticPassword!12345", CancellationToken.None));
        var user = (await users.FindByEmailAsync(Email))!;
        user.Active = false; Assert.True((await users.UpdateAsync(user)).Succeeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CloudDatabaseSetup.InitializeOperatorAsync(db, users, Email, Password, CancellationToken.None));
        await db.Entry(user).ReloadAsync();
        Assert.False(user.Active);
        Assert.True(await users.CheckPasswordAsync(user, Password));
    });

    private static async Task InEmptyDatabase(Func<AppDbContext, UserManager<AppUser>, Task> action)
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WAREHOUSERENT_TEST_SQL")
            ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
        { InitialCatalog = $"WarehouseRent_Demo_Test_{Guid.NewGuid():N}" }.ConnectionString;
        await using var db = new AppDbContext(SqlConfiguration.Options(connection));
        try
        {
            await DatabaseSetup.ApplyAsync(db, "Testing", connection, seedDemo: false);
            await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:WarehouseRent", connection);
            });
            await using var scope = host.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>());
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
