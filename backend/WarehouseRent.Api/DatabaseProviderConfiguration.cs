using Microsoft.EntityFrameworkCore;
using Npgsql;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api;

public static class DatabaseProviderConfiguration
{
    public const string ProjectRef="jhemdameyrcxqxsffugw";
    public const string ProtectedProjectRef="uwjglyucmyrjxzzryhth";
    public const string RuntimeRole="khospace_api";
    public static bool IsPostgres(IConfiguration configuration)=>configuration["Database:Provider"]=="PostgreSQL";

    public static string PostgresConnection(IConfiguration configuration,bool migration=false)
    {
        if (!IsPostgres(configuration) || !configuration.GetValue<bool>("CloudHosting:Enabled")
            || configuration["CloudHosting:DataProtectionStore"]!="PostgreSQL"
            || configuration["Database:SupabaseProjectRef"]!=ProjectRef
            || configuration["Database:ProtectedProjectRef"]!=ProtectedProjectRef)
            throw new InvalidOperationException("Explicit dedicated KhoSpace PostgreSQL cloud binding is required.");
        var binding=new SupabaseProjectBinding(ProjectRef,ProtectedProjectRef,configuration["Database:ExpectedHost"]??"");
        var cs=PostgresConnectionPolicy.ValidateSupabase(configuration.GetConnectionString(migration?"WarehouseRentMigration":"WarehouseRent")??"",binding);
        var username=new NpgsqlConnectionStringBuilder(cs).Username;
        var role=username?.Split('.')[0];
        if (migration ? role!="postgres" : role!=RuntimeRole)
            throw new InvalidOperationException("Separate migration-owner and least-privilege runtime connections are required.");
        return cs;
    }

    public static DbContextOptions<AppDbContext> Options(IConfiguration configuration)
    {
        var provider=configuration["Database:Provider"]??"SqlServer";
        if (provider=="SqlServer") return SqlConfiguration.Options(configuration.GetConnectionString("WarehouseRent")??"");
        if (provider!="PostgreSQL") throw new InvalidOperationException("Unknown database provider; fallback is forbidden.");
        return PostgresRuntimeConfiguration.ForSupabase(PostgresConnection(configuration),new(ProjectRef,ProtectedProjectRef,configuration["Database:ExpectedHost"]??""));
    }
}
