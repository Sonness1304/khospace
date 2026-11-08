using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Api.Features.OnlinePayments;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Tests;
[Trait("Category","Sql")]
public sealed class SqlSePayPaymentTests(IdentitySqlFixture fixture): IClassFixture<IdentitySqlFixture>
{
    private static int year=2080;
    private const string Secret="synthetic-sepay-secret-at-least-32-characters";
    private WebApplicationFactory<Program> Host(bool live=false, bool enabled=true) => fixture.Host.WithWebHostBuilder(b=> {
        b.UseEnvironment(live ? "Production":"Testing");
        b.UseSetting("Payments:SePay:Enabled",enabled?"true":"false"); b.UseSetting("Payments:SePay:Mode",live?"Live":"Sandbox");
        b.UseSetting("Payments:SePay:Bank",live?"SYNTHETICBANK":"TESTBANK"); b.UseSetting("Payments:SePay:AccountNumber","TEST0000001");
        b.UseSetting("Payments:SePay:Secret",Secret); b.UseSetting("Payments:SePay:AllowLive",live?"true":"false");
        b.UseSetting("Payments:SePay:ZeroFeeCapVerified",live?"true":"false");
    });
    private static async Task<BrowserSession> Login(WebApplicationFactory<Program> host,string user="admin.a@example.test") {
        var b=new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),HandleCookies=false}));
        await b.RefreshCsrf(); Assert.Equal(HttpStatusCode.OK,(await b.Send(HttpMethod.Post,"/api/v1/auth/login",new{userName=user,password=DemoIdentitySeed.Password})).StatusCode);await b.RefreshCsrf();return b;
    }
    private async Task<Guid> Account() {
        var y=Interlocked.Increment(ref year); var grant=await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA,new DateOnly(y,1,1),new DateOnly(y,2,1));
        await using var db=fixture.Db();return await (from g in db.LeaseUnits join l in db.Leases on g.LeaseId equals l.Id where g.Id==grant select l.RentalAccountId).SingleAsync();
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r)=>JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static Task<HttpResponseMessage> CreateRequest(BrowserSession b,Guid account,Guid key,bool csrf=true,string amount="10000")=>b.Send(HttpMethod.Post,"/api/v1/payment-intents",new{rentalAccountId=account,method="SePayQr",amount,reason="Synthetic SePay integration"},key:key,csrf:csrf);
    private static async Task<JsonElement> Create(BrowserSession b,Guid account) {
        var r=await CreateRequest(b,account,Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK,r.StatusCode); return (await Json(r)).GetProperty("result");
    }
    private static byte[] Payload(JsonElement intent,bool live=false,long eventId=90001,string? amount=null,DateTimeOffset? date=null,string? account=null,string? content=null) => JsonSerializer.SerializeToUtf8Bytes(new {
        id=eventId,gateway=live?"SYNTHETICBANK":"TESTBANK",accountNumber=account??"TEST0000001",transferType="in",
        code=intent.GetProperty("bankTransfer").GetProperty("reference").GetString(),transferAmount=int.Parse(amount??"10000",CultureInfo.InvariantCulture),
        content=content??"Synthetic transfer", transactionDate=(date??DateTimeOffset.UtcNow).ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)
    });
    private static Task<HttpResponseMessage> Notify(HttpClient client,byte[] body,long? time=null,bool badSignature=false,bool duplicateHeader=false) {
        var timestamp=(time??DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture);var prefix=Encoding.ASCII.GetBytes(timestamp+".");var signed=prefix.Concat(body).ToArray();
        var signature="sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret),signed)).ToLowerInvariant();
        var request=new HttpRequestMessage(HttpMethod.Post,"/api/payment-notifications/sepay"){Content=new ByteArrayContent(body)};
        request.Headers.TryAddWithoutValidation("X-SePay-Timestamp",timestamp);request.Headers.TryAddWithoutValidation("X-SePay-Signature",badSignature?"sha256="+new string('0',64):signature);
        if(duplicateHeader)request.Headers.TryAddWithoutValidation("X-SePay-Signature",signature);return client.SendAsync(request);
    }
    [Fact] public async Task Disabled_methods_fail_closed_and_do_not_create_intent() {
        using var host=Host(enabled:false);using var b=await Login(host);var account=await Account();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await CreateRequest(b,account,Guid.NewGuid())).StatusCode);
        var methods=await Json(await b.Send(HttpMethod.Get,"/api/v1/payment-methods"));Assert.False(methods.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("method").GetString()=="SePayQr").GetProperty("available").GetBoolean());
        await using var db=fixture.Db();Assert.False(await db.PaymentIntents.AnyAsync(x=>x.RentalAccountId==account));
    }
    [Fact] public async Task Creation_is_scoped_csrf_bounded_idempotent_and_has_no_sandbox_qr() {
        using var host=Host();using var b=await Login(host);var account=await Account();var key=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest,(await CreateRequest(b,account,key,csrf:false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await CreateRequest(b,account,Guid.NewGuid(),amount:"1000.1")).StatusCode);
        var first=await CreateRequest(b,account,key);Assert.Equal(HttpStatusCode.OK,first.StatusCode);var text=await first.Content.ReadAsStringAsync();Assert.Equal(text,await (await CreateRequest(b,account,key)).Content.ReadAsStringAsync());
        var intent=JsonDocument.Parse(text).RootElement.GetProperty("result");Assert.Equal("Ready",intent.GetProperty("state").GetString());Assert.Equal("Sandbox",intent.GetProperty("mode").GetString());Assert.Equal(JsonValueKind.Null,intent.GetProperty("bankTransfer").GetProperty("qrUrl").ValueKind);
        Assert.Equal(TimeSpan.FromMinutes(15),intent.GetProperty("bankTransfer").GetProperty("expiresAt").GetDateTimeOffset()-intent.GetProperty("createdAt").GetDateTimeOffset());
        using var other=await Login(host,"admin.b@example.test");var id=intent.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await other.Send(HttpMethod.Get,$"/api/v1/payment-intents/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await CreateRequest(other,account,Guid.NewGuid())).StatusCode);
        await using var db=fixture.Db();Assert.Equal(1,await db.PaymentIntents.CountAsync(x=>x.RentalAccountId==account));
    }
    [Fact] public async Task Sandbox_valid_duplicate_and_conflicting_events_are_durable_but_never_real_funds() {
        using var host=Host();using var b=await Login(host);var account=await Account();var intent=await Create(b,account);using var client=host.CreateClient();var body=Payload(intent);
        Assert.Equal(HttpStatusCode.NoContent,(await Notify(client,body)).StatusCode);Assert.Equal(HttpStatusCode.NoContent,(await Notify(client,body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Notify(client,Payload(intent,eventId:90002))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Notify(client,Payload(intent,content:"Changed evidence"))).StatusCode);
        await using var db=fixture.Db();var row=await db.PaymentIntents.SingleAsync(x=>x.RentalAccountId==account);Assert.Equal("Paid",row.State);Assert.Null(row.PaymentReceiptId);Assert.False(await db.PaymentReceipts.AnyAsync(x=>x.RentalAccountId==account));Assert.Equal(1,await db.SecurityAudits.CountAsync(x=>x.AggregateId==row.Id&&x.Action=="SePayPaymentNotification"));
    }
    [Fact] public async Task Live_synthetic_fixture_creates_exactly_one_verified_receipt_without_allocation_under_concurrency() {
        using var host=Host(live:true);using var b=await Login(host);var account=await Account();var intent=await Create(b,account);using var client=host.CreateClient();var body=Payload(intent,live:true,eventId:91001);
        Assert.StartsWith("https://vietqr.app/img?",intent.GetProperty("bankTransfer").GetProperty("qrUrl").GetString());
        var responses=await Task.WhenAll(Notify(client,body),Notify(client,body));Assert.All(responses,r=>Assert.Equal(HttpStatusCode.NoContent,r.StatusCode));
        await using var db=fixture.Db();var row=await db.PaymentIntents.SingleAsync(x=>x.RentalAccountId==account);var receipt=await db.PaymentReceipts.SingleAsync(x=>x.RentalAccountId==account);Assert.Equal(receipt.Id,row.PaymentReceiptId);Assert.Equal("Verified",receipt.State);Assert.Equal("gateway:SePay",receipt.VerifiedBy);Assert.Equal(10000,receipt.Amount);Assert.NotNull(receipt.VerifiedAt);Assert.False(await db.FundAllocations.AnyAsync(x=>x.RentalAccountId==account));
    }
    [Theory][InlineData("signature")][InlineData("replay")][InlineData("header")][InlineData("account")][InlineData("amount")][InlineData("before")][InlineData("future")]
    public async Task Invalid_auth_or_signed_mismatch_never_changes_funds(string kind) {
        using var host=Host();using var b=await Login(host);var account=await Account();var intent=await Create(b,account);using var client=host.CreateClient();
        var body=Payload(intent,eventId:92000+(long)kind.Length,account:kind=="account"?"OTHERACCOUNT":null,amount:kind=="amount"?"10001":null,date:kind=="before"?DateTimeOffset.UtcNow.AddDays(-1):kind=="future"?DateTimeOffset.UtcNow.AddDays(1):null);
        var r=await Notify(client,body,time:kind=="replay"?DateTimeOffset.UtcNow.AddMinutes(-6).ToUnixTimeSeconds():null,badSignature:kind=="signature",duplicateHeader:kind=="header");
        Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.UnprocessableEntity);await using var db=fixture.Db();var row=await db.PaymentIntents.SingleAsync(x=>x.RentalAccountId==account);Assert.Equal("Ready",row.State);Assert.Null(row.TransactionId);Assert.False(await db.PaymentReceipts.AnyAsync(x=>x.RentalAccountId==account));
    }
    [Theory][InlineData("closed")][InlineData("cancelled")][InlineData("expired")][InlineData("booking-cancelled")][InlineData("hold-expired")]
    public async Task Late_closed_cancelled_events_are_review_required_without_receipt(string kind) {
        using var host=Host(live:true);using var b=await Login(host);var account=await Account();var intent=await Create(b,account);var id=intent.GetProperty("id").GetGuid();
        if(kind=="cancelled") Assert.Equal(HttpStatusCode.OK,(await b.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/abandon",new{reason="Cancel synthetic SePay"},version:intent.GetProperty("version").GetString(),key:Guid.NewGuid())).StatusCode);
        await using(var db=fixture.Db()) {
            if(kind=="closed") {var a=await db.RentalAccounts.SingleAsync(x=>x.Id==account);a.State="Closed";await db.SaveChangesAsync();}
            if(kind is "booking-cancelled" or "hold-expired") {var a=await db.RentalAccounts.SingleAsync(x=>x.Id==account);var booking=await db.Bookings.SingleAsync(x=>x.Id==a.BookingId);booking.State=kind=="hold-expired"?"Held":"Cancelled";booking.HeldAt=DateTimeOffset.UtcNow.AddMinutes(-20);booking.ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-5);await db.SaveChangesAsync();}
            if(kind=="expired") {var created=DateTimeOffset.UtcNow.AddMinutes(-20);var expires=created.AddMinutes(15);await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE khospace.\"PaymentIntents\" SET \"CreatedAt\"={created}, \"ExpiresAt\"={expires} WHERE \"Id\"={id}");}
        }
        using var client=host.CreateClient();Assert.Equal(HttpStatusCode.NoContent,(await Notify(client,Payload(intent,live:true,eventId:93000+kind.Length))).StatusCode);
        await using(var db=fixture.Db()) {var row=await db.PaymentIntents.SingleAsync(x=>x.Id==id);Assert.Equal("ReviewRequired",row.State);Assert.NotNull(row.TransactionId);Assert.Null(row.PaymentReceiptId);Assert.False(await db.PaymentReceipts.AnyAsync(x=>x.RentalAccountId==account));}
    }
    [Fact] public async Task Signed_event_cannot_be_reused_for_another_order() {
        using var host=Host();using var b=await Login(host);var account=await Account();var one=await Create(b,account);var two=await Create(b,account);using var client=host.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent,(await Notify(client,Payload(one,eventId:94001))).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await Notify(client,Payload(two,eventId:94001))).StatusCode);
        await using var db=fixture.Db();Assert.Equal("Ready",(await db.PaymentIntents.SingleAsync(x=>x.Id==two.GetProperty("id").GetGuid())).State);
    }
    [Fact] public async Task Changed_recipient_hides_transfer_details_and_identity_fields_are_immutable() {
        using var host=Host();using var b=await Login(host);var account=await Account();var intent=await Create(b,account);var id=intent.GetProperty("id").GetGuid();
        using var changed=host.WithWebHostBuilder(x=>x.UseSetting("Payments:SePay:AccountNumber","TEST0000002"));using var changedBrowser=await Login(changed);
        var read=await Json(await changedBrowser.Send(HttpMethod.Get,$"/api/v1/payment-intents/{id}"));Assert.Equal(JsonValueKind.Null,read.GetProperty("bankTransfer").ValueKind);
        await using var db=fixture.Db();var row=await db.PaymentIntents.SingleAsync(x=>x.Id==id);row.BankTransferReference=SePaySettings.NewReference();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact] public async Task Oversized_callback_is_bounded_and_requires_no_session() {
        using var host=Host();using var client=host.CreateClient();
        var r=await Notify(client,new byte[SePayBankTransferProtocol.MaximumBodyBytes+1]);Assert.Equal(HttpStatusCode.RequestEntityTooLarge,r.StatusCode);
        r=await client.PostAsJsonAsync("/api/payment-notifications/sepay",new{paid=true});Assert.Equal(HttpStatusCode.Unauthorized,r.StatusCode);
    }
    [Fact] public async Task Receipt_failure_rolls_back_event_intent_and_audit_atomically() {
        using var host=Host(live:true).WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<AppDbContext>();s.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectReceipt()).Options));}));
        using var b=await Login(host);var account=await Account();var intent=await Create(b,account);using var client=host.CreateClient();Assert.Equal(HttpStatusCode.Conflict,(await Notify(client,Payload(intent,live:true,eventId:95001))).StatusCode);
        await using var db=fixture.Db();var row=await db.PaymentIntents.SingleAsync(x=>x.RentalAccountId==account);Assert.Equal("Ready",row.State);Assert.Null(row.TransactionId);Assert.False(await db.PaymentReceipts.AnyAsync(x=>x.RentalAccountId==account));Assert.False(await db.SecurityAudits.AnyAsync(x=>x.AggregateId==row.Id&&x.Action=="SePayPaymentNotification"));
    }
    private sealed class RejectReceipt:SaveChangesInterceptor {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int r,CancellationToken ct=default) {
            // Fail after SQL writes, before the callback transaction commits; verify actual rollback.
            if(d.Context!.ChangeTracker.Entries<WarehouseRent.Infrastructure.Finance.PaymentReceipt>().Any(x=>x.Entity.Method=="SePay"))throw new DbUpdateException("Synthetic atomic rollback");return ValueTask.FromResult(r);
        }
    }
}
