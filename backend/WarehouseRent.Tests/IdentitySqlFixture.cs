using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace WarehouseRent.Tests;

public sealed class IdentitySqlFixture : IAsyncLifetime
{
    private PostgresRuntimeFixture? postgres;
    private string SqlConnection { get; } = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("WAREHOUSERENT_TEST_SQL")
        ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
    { InitialCatalog = $"WarehouseRent_Demo_Test_{Guid.NewGuid():N}" }.ConnectionString;
    public string Connection => postgres?.Connection ?? SqlConnection;
    public DbContextOptions<AppDbContext> Options() => postgres?.Options() ?? SqlConfiguration.Options(Connection);
    public WebApplicationFactory<Program> Host { get; private set; } = null!;
    public AppDbContext Db() => postgres?.Context() ?? new(SqlConfiguration.Options(Connection));
    // Direct fixture construction for allocation predicates only, never an application endpoint.
    public async Task<Guid> SeedAllocationOwner(Guid unitId, DateOnly start, DateOnly end)
    {
        await using var db = Db(); var unit = await db.RentableUnits.SingleAsync(x => x.Id == unitId);
        var quote = new RentalQuote { Id = Guid.NewGuid(), OrganizationId = DemoIdentitySeed.OrganizationA, FacilityId = unit.FacilityId,
            CreatedBy = "synthetic-test-fixture", CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(24), StartDate = start, EndDate = end,
            RentalSubtotal = 100, RequiredDeposit = 20, PolicyVersion = "SyntheticFixture" };
        var booking = new Booking { Id = Guid.NewGuid(), QuoteId = quote.Id, OrganizationId = quote.OrganizationId, FacilityId = unit.FacilityId,
            StartDate = start, EndDate = end, HeldAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15), State = "Held" };
        var line = new BookingLine { Id = Guid.NewGuid(), BookingId = booking.Id, UnitId = unitId, StartDate = start, EndDate = end };
        db.Quotes.Add(quote); db.Bookings.Add(booking); db.BookingLines.Add(line);
        db.RentalAccounts.Add(new RentalAccount { Id = Guid.NewGuid(), BookingId = booking.Id, OrganizationId = quote.OrganizationId, RequiredDeposit = 20 });
        await db.SaveChangesAsync(); return line.Id;
    }
    public async Task<Guid> SeedOccupancyOwner(Guid unitId, DateOnly start, DateOnly end)
    {
        var lineId = await SeedAllocationOwner(unitId, start, end);
        await using var db = Db(); var line = await db.BookingLines.SingleAsync(x => x.Id == lineId);
        var booking = await db.Bookings.SingleAsync(x => x.Id == line.BookingId); booking.State = "Confirmed";
        var account = await db.RentalAccounts.SingleAsync(x => x.BookingId == booking.Id);
        var allocation = new WarehouseRent.Infrastructure.Facilities.SpaceAllocation { Id = Guid.NewGuid(), UnitId = unitId, BookingLineId = line.Id, StartDate = start, EndDate = end, State = "Confirmed" };
        var lease = new Lease { Id = Guid.NewGuid(), BookingId = booking.Id, RentalAccountId = account.Id, OrganizationId = booking.OrganizationId, FacilityId = booking.FacilityId,
            StartDate = start, EndDate = end, State = "Active", ActivatedAt = DateTimeOffset.UtcNow };
        var grant = new LeaseUnit { Id = Guid.NewGuid(), LeaseId = lease.Id, UnitId = unitId, InitialAllocationId = allocation.Id };
        db.SpaceAllocations.Add(allocation); db.Leases.Add(lease); db.LeaseUnits.Add(grant); await db.SaveChangesAsync(); return grant.Id;
    }

    public async Task InitializeAsync()
    {
        var provider=Environment.GetEnvironmentVariable("WAREHOUSERENT_TEST_PROVIDER");
        if(provider is not (null or "PostgreSqlFixture")) throw new InvalidOperationException("Unknown fixture provider.");
        if(provider=="PostgreSqlFixture")
        {
            // No externally supplied PG connection: only the owned loopback cluster and generated catalogs.
            postgres=await PostgresRuntimeFixture.Create(empty:true);
            try {
                await using var pg=postgres.Context();await PostgresMigrations.ApplyAsync(pg,default);
                foreach(var type in new[]{typeof(DemoIdentitySeed),typeof(WarehouseRent.Infrastructure.Facilities.DemoFacilitySeed)})
                    await (Task)type.GetMethod("ApplyAsync",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{pg,CancellationToken.None})!;
                Host=new WebApplicationFactory<Program>().WithWebHostBuilder(builder=>{
                    builder.UseEnvironment("Testing");builder.UseSetting("ConnectionStrings:WarehouseRent",SqlConnection);
                    builder.ConfigureServices(services=>{services.RemoveAll<AppDbContext>();services.AddScoped(_=>postgres.Context());});
                });
                return;
            } catch {await postgres.DisposeAsync();postgres=null;throw;}
        }
        await using var db = Db();
        await DatabaseSetup.ApplyAsync(db, "Demo", Connection, seedDemo: true);
        Host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:WarehouseRent", Connection);
        });
    }

    public HttpClient Client() => Host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    public async Task<BrowserSession> Login(string user = "admin.a@example.test")
    {
        var browser = new BrowserSession(Client());
        await browser.RefreshCsrf();
        var response = await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = user, password = DemoIdentitySeed.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await browser.RefreshCsrf();
        return browser;
    }

    public async Task DisposeAsync()
    {
        if (Host is not null) await Host.DisposeAsync();
        if(postgres is not null){await postgres.DisposeAsync();return;}
        await using var db = Db();
        // Only the GUID-generated synthetic database owned by this fixture is deleted.
        await db.Database.EnsureDeletedAsync();
    }
}

public sealed class BrowserSession(HttpClient client) : IDisposable
{
    public HttpClient Client { get; } = client;
    public Dictionary<string, string> Cookies { get; } = [];
    public string Token { get; private set; } = "";
    public string CookieHeader => string.Join("; ", Cookies.Select(x => $"{x.Key}={x.Value}"));

    public async Task RefreshCsrf()
    {
        var response = await Send(HttpMethod.Get, "/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Token = json.RootElement.GetProperty("token").GetString()!;
    }

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? version = null,
        Guid? key = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, path);
        if (Cookies.Count > 0) request.Headers.Add("Cookie", CookieHeader);
        if (csrf && method != HttpMethod.Get) request.Headers.Add("X-CSRF-Token", Token);
        if (version is not null) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        if (key.HasValue) request.Headers.Add("Idempotency-Key", key.Value.ToString());
        if (body is not null) request.Content = JsonContent.Create(body);
        var response = await Client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            foreach (var cookie in cookies)
            {
                var pair = cookie.Split(';')[0].Split('=', 2);
                Cookies[pair[0]] = pair[1];
            }
        return response;
    }

    public void Dispose() => Client.Dispose();
}
