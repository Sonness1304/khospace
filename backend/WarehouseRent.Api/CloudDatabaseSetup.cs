using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Invitations;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api;

public static partial class CloudDatabaseSetup
{
    [GeneratedRegex(@"^WarehouseRent_Cloud_[A-Za-z0-9]{1,48}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatabaseName();

    public static void Validate(string environment, string connectionString, string expectedDatabase,
        string? operatorEmail, string? operatorPassword)
    {
        var sql = new SqlConnectionStringBuilder(connectionString);
        // Production initialization cannot reuse the local Demo/Test path or an unconfirmed catalog.
        if (environment != "Production" || !DatabaseName().IsMatch(expectedDatabase)
            || sql.InitialCatalog != expectedDatabase || !string.IsNullOrEmpty(sql.AttachDBFilename)
            || sql.UserInstance || sql.Encrypt == SqlConnectionEncryptOption.Optional
            || sql.TrustServerCertificate && !(sql.DataSource.EndsWith(".railway.internal", StringComparison.OrdinalIgnoreCase)
                && Uri.CheckHostName(sql.DataSource) == UriHostNameType.Dns
                && sql.DataSource.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
            || !CustomerAuthentication.ValidEmail(operatorEmail) || operatorPassword is null
            || operatorPassword.Length is < 20 or > 128)
            throw new InvalidOperationException("Explicit cloud target, encrypted SQL and private bootstrap credentials are required.");
    }

    public static async Task ApplyAsync(IServiceProvider services, IConfiguration configuration, string environment)
    {
        if (DatabaseProviderConfiguration.IsPostgres(configuration))
        {
            await PostgresCloudBootstrap.ApplyAsync(services,configuration,environment);
            return;
        }
        var connection = configuration.GetConnectionString("WarehouseRent") ?? "";
        var database = configuration["CloudHosting:DatabaseName"] ?? "";
        var email = configuration["CloudHosting:OperatorEmail"]?.Trim();
        var password = configuration["CloudHosting:OperatorPassword"];
        if (!configuration.GetValue<bool>("CloudHosting:Enabled"))
            throw new InvalidOperationException("Explicit cloud mode is required.");
        Validate(environment, connection, database, email, password);
        var db = services.GetRequiredService<AppDbContext>();
        // One-shot initialization uses the existing bounded DDL timeout, not runtime query settings.
        db.Database.SetCommandTimeout(30);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await db.Database.MigrateAsync(deadline.Token);
        // Token providers may resolve DataProtection; the SQL key table must already exist.
        var users = services.GetRequiredService<UserManager<AppUser>>();
        await InitializeOperatorAsync(db, users, email!, password!, deadline.Token);
    }

    // Schema/transport validation remains mandatory in ApplyAsync; this seam also supports isolated SQL fixtures.
    public static async Task InitializeOperatorAsync(AppDbContext db, UserManager<AppUser> users,
        string email, string password, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await IdentityWriteLock.Acquire(db, cancellationToken);
        var existing = await users.FindByEmailAsync(email!);
        if (existing is not null)
        {
            // Never promote an existing customer's matching email or reset a live password.
            if (!existing.OperatorAdmin || !existing.Active || existing.UserName != email
                || !await users.CheckPasswordAsync(existing, password!))
                throw new InvalidOperationException("Cloud operator initialization cannot replace an existing identity.");
        }
        else
        {
            // A previously populated installation requires its existing administrative workflow.
            if (await db.Users.AnyAsync(cancellationToken))
                throw new InvalidOperationException("Cloud bootstrap requires an empty identity catalog.");
            var user = new AppUser { UserName = email, Email = email, OperatorAdmin = true, EmailConfirmed = false };
            var result = await users.CreateAsync(user, password!);
            if (!result.Succeeded) throw new InvalidOperationException("Cloud operator did not meet the identity policy.");
            db.SecurityAudits.Add(new SecurityAudit
            {
                Id = Guid.NewGuid(), ActorId = user.Id, AggregateId = Guid.Parse(user.Id),
                Action = "CloudOperatorInitialized", Reason = "Explicit one-shot empty cloud installation bootstrap",
                CorrelationId = Guid.NewGuid().ToString("N"), At = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
