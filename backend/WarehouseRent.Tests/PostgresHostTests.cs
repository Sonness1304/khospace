using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using WarehouseRent.Api;
using WarehouseRent.Infrastructure.Persistence;
using Npgsql;
using System.Net;
using System.Security.Cryptography;
using WarehouseRent.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace WarehouseRent.Tests;

public sealed class PostgresHostTests
{
    [Fact]
    public async Task One_shot_schema_role_activation_and_operator_bootstrap_repeats_without_resetting_credentials()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);await using var owner=fixture.Context();
        var role="warehouse_fixture_role_"+Guid.NewGuid().ToString("N");const string password="SyntheticPrivateRole!123456789",email="bootstrap@example.test";
        var runtime=new NpgsqlConnectionStringBuilder(fixture.Connection){Username=role,Password=password}.ConnectionString;
        await using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseEnvironment("Testing");builder.UseSetting("ConnectionStrings:WarehouseRent","Server=localhost;Database=Unused;Integrated Security=true;TrustServerCertificate=true");
            builder.ConfigureServices(services=>{services.RemoveAll<AppDbContext>();services.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(runtime).Options));});
        });
        try {
            await using var scope=host.Services.CreateAsyncScope();
            await PostgresCloudBootstrap.InitializeAsync(owner,scope.ServiceProvider,role,password,email,password,default);
            await PostgresCloudBootstrap.InitializeAsync(owner,scope.ServiceProvider,role,password,email,password,default);
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Database.SqlQuery<bool>($"SELECT rolcanlogin AS \"Value\" FROM pg_roles WHERE rolname=current_user").SingleAsync());
            Assert.True(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,default));Assert.True(await PostgresMigrations.IsReadyAsync(db,default));
            Assert.Single(await db.Users.ToListAsync());Assert.Single(await db.SecurityAudits.ToListAsync());Assert.Empty(await db.Facilities.ToListAsync());
            var verifier=await owner.Database.SqlQuery<string>($"SELECT rolpassword AS \"Value\" FROM pg_authid WHERE rolname={role}").SingleAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(()=>PostgresCloudBootstrap.InitializeAsync(owner,scope.ServiceProvider,role,"DifferentPrivatePassword!123456789",email,"WrongOperatorPassword!123456789",default));
            Assert.Equal(verifier,await owner.Database.SqlQuery<string>($"SELECT rolpassword AS \"Value\" FROM pg_authid WHERE rolname={role}").SingleAsync());
        } finally {
            var cleanup="DROP OWNED BY \""+role+"\"; DROP ROLE IF EXISTS \""+role+"\"";await owner.Database.ExecuteSqlRawAsync(cleanup);
        }
    }

    [Fact]
    public async Task Migrated_least_privilege_cloud_host_bootstraps_and_login_cookie_survives_independent_host_restart()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var owner=fixture.Context();await PostgresMigrations.ApplyAsync(owner,default);
        var role="warehouse_fixture_role_"+Guid.NewGuid().ToString("N");
        await PostgresRuntimePrivileges.ProvisionAsync(owner,role,default);
        var runtime=new NpgsqlConnectionStringBuilder(fixture.Connection){Username=role}.ConnectionString;
        var root=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        const string proxy="Synthetic_Postgres_Proxy_Only_1234567890",email="owner@example.test",password="SyntheticOperator!123456789";
        WebApplicationFactory<Program> Cloud()=>new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseEnvironment("Testing");
            foreach(var pair in new Dictionary<string,string> {
                ["Database:Provider"]="PostgreSQL",["CloudHosting:Enabled"]="true",["CloudHosting:DataProtectionStore"]="PostgreSQL",
                ["Database:SupabaseProjectRef"]=DatabaseProviderConfiguration.ProjectRef,["Database:ProtectedProjectRef"]=DatabaseProviderConfiguration.ProtectedProjectRef,
                ["Database:ExpectedHost"]="db.jhemdameyrcxqxsffugw.supabase.co",["CloudHosting:PublicOrigin"]="https://warehouse.example.com",
                ["CloudHosting:BackendHost"]="api.example.com",["CloudHosting:SharedProxyKey"]=proxy,["CloudHosting:DataProtectionEncryptionKey"]=root,
                ["ConnectionStrings:WarehouseRent"]="Host=db.jhemdameyrcxqxsffugw.supabase.co;Database=postgres;Username=khospace_api;Password=synthetic;SSL Mode=VerifyFull"
            })builder.UseSetting(pair.Key,pair.Value);
            builder.ConfigureServices(services=>{
                // Explicit synthetic fixture seams: never a production local-host/TLS bypass.
                services.RemoveAll<AppDbContext>();services.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(runtime).Options));
                services.RemoveAll<PostgresDataProtectionRepository>();services.AddSingleton(new PostgresDataProtectionRepository(runtime));
            });
        });
        HttpClient Client(WebApplicationFactory<Program> host){var client=host.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("http://api.example.com"),HandleCookies=false,AllowAutoRedirect=false});client.DefaultRequestHeaders.Add(CloudHosting.ProxyHeader,proxy);client.DefaultRequestHeaders.Add(CloudHosting.ClientIpHeader,"203.0.113.10");return client;}
        try {
            var enableLogin="ALTER ROLE \""+role+"\" LOGIN";
            await owner.Database.ExecuteSqlRawAsync(enableLogin); // Loopback trust cluster only; no password/secret deployed.
            Dictionary<string,string> cookies;
            await using(var first=Cloud())
            {
                using var session=new BrowserSession(Client(first));
                Assert.Equal(HttpStatusCode.ServiceUnavailable,(await session.Send(HttpMethod.Get,"/health/ready")).StatusCode);
                await using(var scope=first.Services.CreateAsyncScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    Assert.True(await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,default));
                    await CloudDatabaseSetup.InitializeOperatorAsync(db,scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(),email,password,default);
                    Assert.Empty(await db.Facilities.ToListAsync());Assert.Empty(await db.FoundationMarkers.ToListAsync());
                }
                Assert.Equal(HttpStatusCode.OK,(await session.Send(HttpMethod.Get,"/health/ready")).StatusCode);
                await session.RefreshCsrf();
                Assert.Equal(HttpStatusCode.OK,(await session.Send(HttpMethod.Post,"/api/v1/auth/login",new {userName=email,password})).StatusCode);
                cookies=new(session.Cookies);
            }
            await using(var second=Cloud())
            {
                using var session=new BrowserSession(Client(second));foreach(var cookie in cookies)session.Cookies[cookie.Key]=cookie.Value;
                Assert.Equal(HttpStatusCode.OK,(await session.Send(HttpMethod.Get,"/api/v1/auth/me")).StatusCode);
                Assert.Equal(HttpStatusCode.OK,(await session.Send(HttpMethod.Get,"/health/ready")).StatusCode);
            }
        } finally {
            // Role owns no data; DROP OWNED revokes this fixture role's grants/policies on this generated DB only.
            var cleanup="DROP OWNED BY \""+role+"\"; DROP ROLE \""+role+"\"";
            await owner.Database.ExecuteSqlRawAsync(cleanup);
        }
    }

    [Fact]
    public async Task Readiness_does_not_accept_reachable_preview_or_owner_role_and_build_exposes_provider_migration()
    {
        await using var fixture=await PostgresRuntimeFixture.Create(empty:true);
        await using var host=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:WarehouseRent","Server=localhost;Database=Unused;Integrated Security=true;TrustServerCertificate=true");
            builder.ConfigureServices(services=>{services.RemoveAll<AppDbContext>();services.AddScoped(_=>fixture.Context());});
        });
        await using var scope=host.Services.CreateAsyncScope();
        var probe=scope.ServiceProvider.GetRequiredService<ReadinessProbe>();
        Assert.False(await probe.CheckAsync(default));
        await using var db=fixture.Context();await PostgresMigrations.ApplyAsync(db,default);
        Assert.False(await probe.CheckAsync(default)); // Owner/superuser is not a safe public runtime credential.
        var config=new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Database:Provider"]="PostgreSQL" }).Build();
        using var identity=JsonDocument.Parse(JsonSerializer.Serialize(BuildIdentity.Read(config)));
        Assert.Equal("PostgreSQL",identity.RootElement.GetProperty("databaseProvider").GetString());
        Assert.Equal(PostgresMigrations.Latest,identity.RootElement.GetProperty("latestExpectedMigration").GetString());
        Assert.Equal(PostgresMigrations.LatestChecksum,identity.RootElement.GetProperty("migrationChecksum").GetString());
    }
}
