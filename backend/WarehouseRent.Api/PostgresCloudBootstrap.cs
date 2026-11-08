using Microsoft.AspNetCore.Identity;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api;

public static class PostgresCloudBootstrap
{
    public static async Task ApplyAsync(IServiceProvider services,IConfiguration configuration,string environment)
    {
        var email=configuration["CloudHosting:OperatorEmail"]?.Trim();var password=configuration["CloudHosting:OperatorPassword"];
        if(environment!="Production" || !CustomerAuthentication.ValidEmail(email) || password is null || password.Length is <20 or >128)
            throw new InvalidOperationException("Explicit Production bootstrap and private operator credentials are required.");
        // Validate BOTH connections before opening either. No local, Demo or IELTS target is accepted here.
        var runtime=DatabaseProviderConfiguration.PostgresConnection(configuration);
        var runtimePassword=new Npgsql.NpgsqlConnectionStringBuilder(runtime).Password;
        if(runtimePassword is null || runtimePassword.Length is <20 or >128)throw new InvalidOperationException("Private runtime bootstrap credentials must meet the role policy.");
        var migration=DatabaseProviderConfiguration.PostgresConnection(configuration,migration:true);
        var binding=new SupabaseProjectBinding(DatabaseProviderConfiguration.ProjectRef,DatabaseProviderConfiguration.ProtectedProjectRef,configuration["Database:ExpectedHost"]??"");
        await using var owner=new AppDbContext(PostgresRuntimeConfiguration.ForSupabase(migration,binding));
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await InitializeAsync(owner,services,DatabaseProviderConfiguration.RuntimeRole,runtimePassword,email!,password,deadline.Token);
    }

    // The transport/environment boundary remains mandatory in ApplyAsync; this seam supports generated local fixtures.
    public static async Task InitializeAsync(AppDbContext owner,IServiceProvider services,string runtimeRole,string runtimePassword,string email,string password,CancellationToken ct)
    {
        if(!CustomerAuthentication.ValidEmail(email) || password.Length is <20 or >128 || runtimePassword.Length is <20 or >128)
            throw new InvalidOperationException("Private bootstrap credentials do not meet the policy.");
        await PostgresMigrations.ApplyAsync(owner,ct);
        await PostgresRuntimePrivileges.ProvisionAsync(owner,runtimeRole,ct,runtimePassword);
        var db=services.GetRequiredService<AppDbContext>();
        if(!await PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,ct))
            throw new InvalidOperationException("A least-privilege PostgreSQL runtime connection is required.");
        await CloudDatabaseSetup.InitializeOperatorAsync(db,services.GetRequiredService<UserManager<AppUser>>(),email,password,ct);
    }
}
