using Microsoft.EntityFrameworkCore.Design;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__WarehouseRent")
            ?? throw new InvalidOperationException("Set ConnectionStrings__WarehouseRent explicitly for EF tooling.");
        return new AppDbContext(SqlConfiguration.Options(connection));
    }
}
