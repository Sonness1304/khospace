using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.DataProtection.KeyManagement;

namespace WarehouseRent.Api;

public static class CloudHosting
{
    public const string ProxyHeader = "X-KhoSpace-Proxy-Key";
    public const string ClientIpHeader = "X-Vercel-Forwarded-For";

    private sealed class Policy(Uri? origin, byte[]? key)
    {
        public Uri? Origin { get; } = origin;
        public byte[]? Key { get; } = key;
    }

    public static void AddCloudHosting(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue<bool>("CloudHosting:Enabled"))
        {
            builder.Services.AddSingleton(new Policy(null, null));
            return;
        }

        var publicOrigin = builder.Configuration["CloudHosting:PublicOrigin"];
        var proxyKey = builder.Configuration["CloudHosting:SharedProxyKey"];
        var directory = builder.Configuration["CloudHosting:DataProtectionPath"];
        var store = builder.Configuration["CloudHosting:DataProtectionStore"] ?? "FileSystem";
        if (store is not ("FileSystem" or "SqlServer" or "PostgreSQL")) throw InvalidConfiguration();
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps || !origin.IsDefaultPort || !ValidHost(origin.Host)
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || string.IsNullOrEmpty(proxyKey) || proxyKey.Length is < 32 or > 256
            || proxyKey.Any(character => character is < '!' or > '~')
            || store == "FileSystem" && (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)))
            throw InvalidConfiguration();

        var allowedHosts = new List<string> { origin.IdnHost };
        foreach (var key in new[] { "CloudHosting:BackendHost", "CloudHosting:HealthCheckHost" })
        {
            var host = builder.Configuration[key];
            if (string.IsNullOrEmpty(host)) continue;
            if (!ValidHost(host)) throw InvalidConfiguration();
            allowedHosts.Add(host.ToLowerInvariant());
        }

        if (store is "SqlServer" or "PostgreSQL")
        {
            var encodedKey = builder.Configuration["CloudHosting:DataProtectionEncryptionKey"];
            if (string.IsNullOrWhiteSpace(encodedKey)) throw InvalidConfiguration();
            using (var validation = new CloudKeyEncryption(encodedKey)) { }
            var connection = builder.Configuration.GetConnectionString("WarehouseRent");
            if (string.IsNullOrWhiteSpace(connection)) throw InvalidConfiguration();
            builder.Services.AddSingleton(_ => new CloudKeyEncryption(encodedKey));
            builder.Services.AddDataProtection().SetApplicationName("WarehouseRent.Cloud");
            if(store=="PostgreSQL")
            {
                connection=DatabaseProviderConfiguration.PostgresConnection(builder.Configuration);
                builder.Services.AddSingleton(_=>new PostgresDataProtectionRepository(connection));
                builder.Services.AddOptions<KeyManagementOptions>().Configure<PostgresDataProtectionRepository,CloudKeyEncryption>(
                    (options,repository,encryption)=>{options.XmlRepository=repository;options.XmlEncryptor=encryption;});
            }
            else
            {
                if(DatabaseProviderConfiguration.IsPostgres(builder.Configuration))throw InvalidConfiguration();
                builder.Services.AddSingleton(_ => new SqlDataProtectionRepository(connection));
                builder.Services.AddOptions<KeyManagementOptions>().Configure<SqlDataProtectionRepository, CloudKeyEncryption>(
                    (options, repository, encryption) => { options.XmlRepository = repository; options.XmlEncryptor = encryption; });
            }
        }
        else
        {
            try
            {
                directory = Path.GetFullPath(directory!);
                var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
                if (directory == Path.GetPathRoot(directory) || Within(webRoot, directory)) throw InvalidConfiguration();
                for (var cursor = new DirectoryInfo(directory); cursor is not null; cursor = cursor.Parent)
                    if (cursor.Exists && cursor.LinkTarget is not null) throw InvalidConfiguration();
                if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
                else
                {
                    Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    if ((File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                        throw InvalidConfiguration();
                }
                var probe = Path.Combine(directory, $".cloud-write-{Guid.NewGuid():N}");
                using (var writable = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    writable.WriteByte(0);
                File.Delete(probe);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                throw InvalidConfiguration();
            }

            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(directory!)).SetApplicationName("WarehouseRent.Cloud");
        }

        builder.Configuration["AllowedHosts"] = string.Join(';', allowedHosts.Distinct(StringComparer.OrdinalIgnoreCase));
        builder.Services.Configure<HostFilteringOptions>(options => options.AllowedHosts = allowedHosts);
        builder.Services.AddSingleton(new Policy(origin, Encoding.ASCII.GetBytes(proxyKey)));
    }

    public static void UseCloudHosting(this WebApplication app)
    {
        var policy = app.Services.GetRequiredService<Policy>();
        if (policy.Origin is null) return;
        if(app.Services.GetService<PostgresDataProtectionRepository>() is {} postgresRepository)
        {
            using var scope=app.Services.CreateScope();
            var db=scope.ServiceProvider.GetRequiredService<WarehouseRent.Infrastructure.Persistence.AppDbContext>();
            // A free container's cold EF model build is CPU work, not database I/O.
            // Warm it before the unchanged, fail-closed five-second validation deadline.
            var modelWarmup = System.Diagnostics.Stopwatch.StartNew();
            _ = db.Model;
            app.Logger.LogInformation("PostgreSQL model initialized in {ElapsedMilliseconds} ms.", modelWarmup.ElapsedMilliseconds);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            if(!WarehouseRent.Infrastructure.Persistence.PostgresMigrations.IsReadyAsync(db,deadline.Token).GetAwaiter().GetResult()
                || !WarehouseRent.Infrastructure.Persistence.PostgresRuntimePrivileges.IsSafeRuntimeAsync(db,deadline.Token).GetAwaiter().GetResult())throw InvalidConfiguration();
            postgresRepository.Validate(app.Services.GetRequiredService<CloudKeyEncryption>());
            var protector=app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cloud-startup-probe");
            if(protector.Unprotect(protector.Protect("PostgreSQL.KeyRing.Ready"))!="PostgreSQL.KeyRing.Ready")throw InvalidConfiguration();
        }
        if (app.Services.GetService<SqlDataProtectionRepository>() is { } repository)
        {
            // Explicit initialization exits before this point. Normal startup never migrates/falls back.
            repository.Validate(app.Services.GetRequiredService<CloudKeyEncryption>());
            var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("cloud-startup-probe");
            const string probe = "WarehouseRent.Cloud.KeyRing.Ready";
            if (protector.Unprotect(protector.Protect(probe)) != probe) throw InvalidConfiguration();
        }
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });

            var isLiveness = context.Request.Path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
                && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method));
            var key = context.Request.Headers[ProxyHeader];
            var address = context.Request.Headers[ClientIpHeader];
            var authenticated = key.Count == 1 && key[0] is { } submitted && submitted.Length == policy.Key!.Length
                && submitted.All(character => character is >= '!' and <= '~')
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(submitted), policy.Key);
            var verifiedAddress = address.Count == 1 && IPAddress.TryParse(address[0], out var parsedAddress)
                ? parsedAddress : null;

            // Only the authenticated edge supplies identity metadata; ordinary forwarded headers are never trusted.
            foreach (var name in context.Request.Headers.Keys.Where(name =>
                         name.Equals(ProxyHeader, StringComparison.OrdinalIgnoreCase)
                         || name.Equals(ClientIpHeader, StringComparison.OrdinalIgnoreCase)
                         || name.Equals("X-KhoSpace-Client-IP", StringComparison.OrdinalIgnoreCase)
                         || name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                         || name.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                         || name.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase)).ToArray())
                context.Request.Headers.Remove(name);

            if (!isLiveness && (!authenticated || verifiedAddress is null))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "CLOUD_PROXY_REQUIRED" });
                return;
            }
            if (authenticated && verifiedAddress is not null)
            {
                context.Request.Scheme = Uri.UriSchemeHttps;
                context.Request.Host = new HostString(policy.Origin.IdnHost);
                context.Request.PathBase = PathString.Empty;
                context.Connection.RemoteIpAddress = verifiedAddress;
            }
            await next(context);
        });
    }

    private static bool ValidHost(string host) =>
        Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.') && !host.EndsWith('.')
        && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && !new[] { ".localhost", ".local", ".internal", ".lan", ".home" }.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        && host.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');

    private static bool Within(string root, string child)
    {
        var relative = Path.GetRelativePath(root, child);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static InvalidOperationException InvalidConfiguration() => new("Cloud hosting configuration is invalid.");
}
