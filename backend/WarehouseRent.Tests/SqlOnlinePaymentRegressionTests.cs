using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Api.Features.OnlinePayments;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Tests;
public sealed class SqlOnlinePaymentRegressionTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    [Fact]
    public async Task Rolled_back_claim_must_not_dispatch_to_provider()
    {
        var calls = 0;
        using var host = fixture.Host.WithWebHostBuilder(b => {
            b.UseSetting("Payments:PublicOrigin", "https://warehouse.example.test");
            b.UseSetting("Payments:Momo:Mode", "Sandbox"); b.UseSetting("Payments:Momo:MerchantId", "TESTMERCHANT");
            b.UseSetting("Payments:Momo:Secret", SqlOnlinePaymentTests.Secret); b.UseSetting("Payments:Momo:AccessKey", "SYNTHETICACCESS");
            b.ConfigureServices(s => {
                s.RemoveAll<AppDbContext>(); s.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectCheckoutReceipt()).Options));
                s.RemoveAll<PaymentGateway>(); s.AddScoped(_ => new PaymentGateway(new HttpClient(new SyntheticHandler((_, _) => {
                    Interlocked.Increment(ref calls); throw new HttpRequestException("Synthetic unknown external outcome");
                }))));
            });
        });
        using var customer = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        await customer.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = "admin.a@example.test", password = DemoIdentitySeed.Password })).StatusCode);
        await customer.RefreshCsrf();
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, new DateOnly(2047,1,1), new DateOnly(2047,2,1));
        Guid account;
        await using (var db=fixture.Db()) account = await (from g in db.LeaseUnits join l in db.Leases on g.LeaseId equals l.Id where g.Id==grant select l.RentalAccountId).SingleAsync();
        var created = await customer.Send(HttpMethod.Post,"/api/v1/payment-intents",new { rentalAccountId=account,method="MomoWallet",amount="10000",reason="Independent synthetic review" },key:Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id=JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("result").GetProperty("resourceId").GetGuid();
        var key = Guid.NewGuid();
        for (var attempt=0;attempt<2;attempt++) {
            var response=await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/checkout",new{reason="Fault injection"},key:key);
            Assert.Equal(HttpStatusCode.PreconditionFailed,response.StatusCode);
        }
        await using(var db=fixture.Db()) {
            Assert.Equal("Created",(await db.PaymentIntents.SingleAsync(x=>x.Id==id)).State);
            Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==key));
        }
        Assert.Equal(0,calls);
    }
    [Fact]
    public async Task Debited_suspicious_vnpay_payment_must_remain_unresolved()
    {
        using var host=fixture.Host.WithWebHostBuilder(b=>{
            b.UseSetting("Payments:PublicOrigin","https://warehouse.example.test");
            b.UseSetting("Payments:Vnpay:Mode","Sandbox");b.UseSetting("Payments:Vnpay:MerchantId","TESTMERCHANT");b.UseSetting("Payments:Vnpay:Secret",SqlOnlinePaymentTests.Secret);
        });
        using var customer=new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),HandleCookies=false}));
        await customer.RefreshCsrf();await customer.Send(HttpMethod.Post,"/api/v1/auth/login",new{userName="admin.a@example.test",password=DemoIdentitySeed.Password});await customer.RefreshCsrf();
        var grant=await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA,new DateOnly(2048,1,1),new DateOnly(2048,2,1));
        Guid account;await using(var db=fixture.Db())account=await(from g in db.LeaseUnits join l in db.Leases on g.LeaseId equals l.Id where g.Id==grant select l.RentalAccountId).SingleAsync();
        var created=await customer.Send(HttpMethod.Post,"/api/v1/payment-intents",new{rentalAccountId=account,method="VnpayCard",amount="10000",reason="Synthetic suspicious callback"},key:Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK,created.StatusCode);var id=JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("result").GetProperty("resourceId").GetGuid();
        await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/checkout",new{reason="Synthetic local URL"},key:Guid.NewGuid());
        var raw=$"vnp_Amount=1000000&vnp_ResponseCode=07&vnp_TmnCode=TESTMERCHANT&vnp_TransactionNo=738829&vnp_TransactionStatus=07&vnp_TxnRef={id:N}";
        var signature=Convert.ToHexString(System.Security.Cryptography.HMACSHA512.HashData(System.Text.Encoding.UTF8.GetBytes(SqlOnlinePaymentTests.Secret),System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        using var anonymous=host.CreateClient();var response=await anonymous.GetAsync("/api/payment-notifications/vnpay?"+raw+"&vnp_SecureHash="+signature);
        Assert.Equal("00",JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("RspCode").GetString());
        await using(var db=fixture.Db()){
            var payment=await db.PaymentIntents.SingleAsync(x=>x.Id==id);
            Assert.NotEqual("Failed",payment.State);
        }
    }
    private sealed class RejectCheckoutReceipt : SaveChangesInterceptor {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result,CancellationToken ct=default) {
            if(data.Context!.ChangeTracker.Entries<OperationalCommand>().Any(e=>e.State==EntityState.Added && e.Entity.ResultJson.Contains("Creating"))) throw new DbUpdateConcurrencyException("Synthetic caught commit-boundary failure");
            return ValueTask.FromResult(result);
        }
    }
}
