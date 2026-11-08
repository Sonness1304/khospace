using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace WarehouseRent.Infrastructure.Persistence;

public static class SqlConfiguration
{
    public static DbContextOptions<AppDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(BoundedConnection(connectionString), sql => sql.CommandTimeout(3)).Options;

    public static string BoundedConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("SQL connection is not configured.");
        return new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 }.ConnectionString;
    }
}
