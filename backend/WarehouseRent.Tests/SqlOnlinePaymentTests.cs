using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WarehouseRent.Api.Features.Settlements;
using WarehouseRent.Api.Features.OnlinePayments;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlOnlinePaymentTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    internal const string Secret = "synthetic-payment-signature-key-not-a-real-credential";
    private async Task<Guid> Account()
    {
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, new DateOnly(2045, 1, 1), new DateOnly(2045, 2, 1));
        await using var db = fixture.Db(); return await (from g in db.LeaseUnits join l in db.Leases on g.LeaseId equals l.Id where g.Id == grant select l.RentalAccountId).SingleAsync();
    }
    private WebApplicationFactory<Program> Host(string mode = "Sandbox", bool production = false) => fixture.Host.WithWebHostBuilder(b => {
        b.UseEnvironment(production ? "Production" : "Testing");
        b.UseSetting("Payments:PublicOrigin", "https://warehouse.example.test");
        b.UseSetting("Payments:AllowLive", production ? "true" : "false");
        foreach (var provider in new[] { "Vnpay", "Momo" }) { b.UseSetting($"Payments:{provider}:Mode", mode); b.UseSetting($"Payments:{provider}:MerchantId", "TESTMERCHANT"); b.UseSetting($"Payments:{provider}:Secret", Secret); b.UseSetting($"Payments:{provider}:AccessKey", "SYNTHETICACCESS"); }
    });
    private static async Task<BrowserSession> Login(WebApplicationFactory<Program> host, string user = "admin.a@example.test")
    {
        var browser = new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false })); await browser.RefreshCsrf();
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(HttpMethod.Post, "/api/v1/auth/login", new { userName = user, password = DemoIdentitySeed.Password })).StatusCode); await browser.RefreshCsrf(); return browser;
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<JsonElement> Create(BrowserSession b, Guid account, string method = "VnpayQr", Guid? key = null)
    {
        var r = await b.Send(HttpMethod.Post, "/api/v1/payment-intents", new { rentalAccountId = account, method, amount = "10000", reason = "Synthetic customer top-up" }, key: key ?? Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await Json(r)).GetProperty("result");
    }
    internal static string VnpayQuery(string order, string amount = "1000000", string transaction = "987654", bool success = true)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["vnp_TmnCode"] = "TESTMERCHANT", ["vnp_TxnRef"] = order, ["vnp_Amount"] = amount, ["vnp_ResponseCode"] = success ? "00" : "24", ["vnp_TransactionStatus"] = success ? "00" : "02", ["vnp_TransactionNo"] = transaction, ["vnp_PayDate"] = "20261029120000" };
        var raw = string.Join("&", values.Select(x => WebUtility.UrlEncode(x.Key) + "=" + WebUtility.UrlEncode(x.Value)));
        return raw + "&vnp_SecureHash=" + Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
    [Fact]
    public async Task Customer_can_read_hosted_payment_command_receipt_without_finance_grant()
    {
        using var host = Host(); using var customer = await Login(host); var account = await Account(); var key = Guid.NewGuid();
        await Create(customer, account, key: key);
        var response = await customer.Send(HttpMethod.Get, $"/api/v1/commands/{key}?scopeId={DemoIdentitySeed.OrganizationA}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var receipt = await Json(response);
        Assert.Equal(key, receipt.GetProperty("commandId").GetGuid());
        Assert.False(receipt.TryGetProperty("readbackPolicy", out _));
    }
    [Fact]
    public async Task Notification_during_momo_create_cannot_be_overwritten_by_checkout_finalization()
    {
        HttpClient? callback=null;
        using var host=Host().WithWebHostBuilder(b=>b.ConfigureServices(services=>{services.RemoveAll<PaymentGateway>();services.AddScoped(_=>new PaymentGateway(new HttpClient(new SyntheticHandler(async(request,ct)=>{
            var json=await request.Content!.ReadFromJsonAsync<JsonElement>(ct);var id=Guid.ParseExact(json.GetProperty("orderId").GetString()!,"N");
            Assert.Equal(HttpStatusCode.NoContent,(await callback!.PostAsJsonAsync("/api/payment-notifications/momo",MomoPayload(id,"3344556677"),ct)).StatusCode);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"resultCode\":0}",Encoding.UTF8,"application/json")};
        }))));}));
        using var customer=await Login(host);using var anonymous=host.CreateClient();callback=anonymous;var account=await Account();var created=await Create(customer,account,"MomoWallet");var intent=created.GetProperty("resourceId").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{intent}/checkout",new{reason="Notification race"},key:Guid.NewGuid())).StatusCode);
        var result=await Json(await customer.Send(HttpMethod.Get,$"/api/v1/payment-intents/{intent}"));Assert.Equal("Paid",result.GetProperty("state").GetString());Assert.Equal("3344556677",result.GetProperty("transactionId").GetString());
    }
    [Fact]
    public async Task Intent_receipt_failure_rolls_back_intent_audit_and_account_change()
    {
        using var host=Host().WithWebHostBuilder(b=>b.ConfigureServices(s=>{s.RemoveAll<AppDbContext>();s.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectCatalogReceipt()).Options));}));
        using var customer=await Login(host);var account=await Account();var key=Guid.NewGuid();
        var result=await customer.Send(HttpMethod.Post,"/api/v1/payment-intents",new{rentalAccountId=account,method="VnpayCard",amount="10000",reason="Synthetic rollback"},key:key);Assert.Equal(HttpStatusCode.InternalServerError,result.StatusCode);
        await using var db=fixture.Db();Assert.False(await db.PaymentIntents.AnyAsync(x=>x.RentalAccountId==account));Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==key));Assert.False(await db.SecurityAudits.AnyAsync(x=>x.Action=="PaymentIntentCreated"&&x.Reason=="Synthetic rollback"));
    }
    [Fact]
    public async Task Signed_checkout_query_is_not_a_provider_notification_and_cookie_csrf_is_still_required()
    {
        using var host=Host();using var customer=await Login(host);var account=await Account();var made=await Create(customer,account);var id=made.GetProperty("resourceId").GetGuid();await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/checkout",new{reason="Checkout query replay"},key:Guid.NewGuid());var item=await Json(await customer.Send(HttpMethod.Get,$"/api/v1/payment-intents/{id}"));using var anonymous=host.CreateClient();
        var url=new Uri(item.GetProperty("checkoutUrl").GetString()!);Assert.Equal("97",(await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay"+url.Query))).GetProperty("RspCode").GetString());
        Assert.Equal("Ready",(await Json(await customer.Send(HttpMethod.Get,$"/api/v1/payment-intents/{id}"))).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.BadRequest,(await customer.Send(HttpMethod.Post,"/api/v1/payment-intents",new{rentalAccountId=account,method="VnpayQr",amount="10000",reason="Missing CSRF"},key:Guid.NewGuid(),csrf:false)).StatusCode);
    }
    [Fact]
    public async Task Unsent_abandon_requires_current_version_and_attempted_checkout_cannot_be_abandoned()
    {
        using var host = Host(); using var customer = await Login(host); var account = await Account(); var created = await Create(customer, account); var id = created.GetProperty("resourceId").GetGuid();
        var body = new { reason = "Abandon before any provider call" };
        Assert.Equal(HttpStatusCode.PreconditionRequired,(await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/abandon",body,key:Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed,(await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/abandon",body,version:"stale",key:Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/abandon",body,version:created.GetProperty("version").GetString(),key:Guid.NewGuid())).StatusCode);
        var next=await Create(customer,account);var nextId=next.GetProperty("resourceId").GetGuid();await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{nextId}/checkout",body,key:Guid.NewGuid());
        var ready=await Json(await customer.Send(HttpMethod.Get,$"/api/v1/payment-intents/{nextId}"));Assert.Equal(HttpStatusCode.Conflict,(await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{nextId}/abandon",body,version:ready.GetProperty("version").GetString(),key:Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Pending_checkout_blocks_close_and_late_paid_closed_account_is_preserved_for_review()
    {
        using var host=Host("Live",true);using var customer=await Login(host);using var finance=await Login(host,"finance@example.test");var account=await Account();var created=await Create(customer,account);var id=created.GetProperty("resourceId").GetGuid();
        var readiness=await Json(await finance.Send(HttpMethod.Get,$"/api/v1/rental-accounts/{account}/settlement"));Assert.Contains(readiness.GetProperty("blockers").EnumerateArray(),x=>x.GetString()=="UNRESOLVED_ONLINE_PAYMENTS");
        await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{id}/checkout",new{reason="Synthetic late payment"},key:Guid.NewGuid());using var anonymous=host.CreateClient();
        Assert.Equal("00",(await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?"+VnpayQuery(id.ToString("N"),transaction:"0",success:false)))).GetProperty("RspCode").GetString());
        // Direct fixture closure models a separate completed close after the authoritative failure. No owner account is changed.
        await using(var db=fixture.Db()){var row=await db.RentalAccounts.SingleAsync(x=>x.Id==account);row.State="Closed";await db.SaveChangesAsync();}
        Assert.Equal("00",(await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?"+VnpayQuery(id.ToString("N"),transaction:"1122334455")))).GetProperty("RspCode").GetString());
        await using(var db=fixture.Db()){var row=await db.PaymentIntents.SingleAsync(x=>x.Id==id);Assert.Equal("ReviewRequired",row.State);Assert.Equal("1122334455",row.TransactionId);Assert.Null(row.PaymentReceiptId);Assert.Equal("Closed",(await db.RentalAccounts.SingleAsync(x=>x.Id==account)).State);Assert.False(await db.PaymentReceipts.AnyAsync(x=>x.RentalAccountId==account));}
    }
    [Fact]
    public async Task Notification_transaction_identity_cannot_be_reused_for_another_order_and_history_is_immutable()
    {
        using var host=Host();using var customer=await Login(host);var account=await Account();var first=await Create(customer,account);var second=await Create(customer,account);using var anonymous=host.CreateClient();
        foreach(var created in new[]{first,second})await customer.Send(HttpMethod.Post,$"/api/v1/payment-intents/{created.GetProperty("resourceId").GetGuid()}/checkout",new{reason="Synthetic transaction isolation"},key:Guid.NewGuid());
        var id=first.GetProperty("resourceId").GetGuid();Assert.Equal("00",(await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?"+VnpayQuery(id.ToString("N"),transaction:"8675309")))).GetProperty("RspCode").GetString());
        Assert.Equal("99",(await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?"+VnpayQuery(second.GetProperty("resourceId").GetGuid().ToString("N"),transaction:"8675309")))).GetProperty("RspCode").GetString());
        await using(var db=fixture.Db()){var row=await db.PaymentIntents.SingleAsync(x=>x.Id==id);row.Amount=20000;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());}
        await using(var db=fixture.Db()){if(db.Database.IsNpgsql()){await Assert.ThrowsAsync<InvalidOperationException>(()=>db.GetService<IMigrator>().MigrateAsync("20261029042636_CustomerItemBarcodes"));Assert.True(await PostgresMigrations.IsReadyAsync(db,default));}else{var e=await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20261029042636_CustomerItemBarcodes"));Assert.Equal(51016,e.Number);}Assert.True(await db.PaymentIntents.AnyAsync(x=>x.Id==id));}
    }
    [Fact]
    public async Task Momo_tamper_duplicate_fields_and_authorized_only_status_never_create_paid_funds()
    {
        using var host=Host();using var customer=await Login(host);var account=await Account();var intent=await Create(customer,account,"MomoWallet");var id=intent.GetProperty("resourceId").GetGuid();
        // A network-free failed dispatch leaves Unknown for these signed notification fixtures.
        await using(var db=fixture.Db()){var row=await db.PaymentIntents.SingleAsync(x=>x.Id==id);row.State="Unknown";await db.SaveChangesAsync();}
        using var anonymous=host.CreateClient();var payload=MomoPayload(id,"445566");payload["amount"]="20000";Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/api/payment-notifications/momo",payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await anonymous.PostAsJsonAsync("/api/payment-notifications/momo",MomoPayload(id,"445566","20000"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await anonymous.PostAsJsonAsync("/api/payment-notifications/momo",MomoPayload(id,"445566",result:"9000"))).StatusCode);
        var text=JsonSerializer.Serialize(MomoPayload(id,"445566"));text=text[..^1]+",\"amount\":10000}";Assert.Equal(HttpStatusCode.UnprocessableEntity,(await anonymous.PostAsync("/api/payment-notifications/momo",new StringContent(text,Encoding.UTF8,"application/json"))).StatusCode);
        Assert.Equal("Unknown",(await Json(await customer.Send(HttpMethod.Get,$"/api/v1/payment-intents/{id}"))).GetProperty("state").GetString());
    }
    [Fact]
    public async Task Live_signed_success_is_recorded_once_and_never_allocates_or_verifies_automatically()
    {
        using var host = Host("Live", true); using var customer = await Login(host); var account = await Account(); var created = await Create(customer, account); var id = created.GetProperty("resourceId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/payment-intents/{id}/checkout", new { reason = "Synthetic live-configuration adapter fixture, no gateway request" }, key: Guid.NewGuid())).StatusCode);
        using var anonymous = host.CreateClient(); var query = VnpayQuery(id.ToString("N"), transaction: Guid.NewGuid().ToString("N").Select(c => (char)('0' + c % 10)).Aggregate("", (s,c)=>s+c));
        var replies = await Task.WhenAll(anonymous.GetAsync("/api/payment-notifications/vnpay?" + query), anonymous.GetAsync("/api/payment-notifications/vnpay?" + query));
        Assert.Contains(replies, r => r.IsSuccessStatusCode); await using var db = fixture.Db(); var receipts = await db.PaymentReceipts.Where(x => x.RentalAccountId == account).ToArrayAsync(); Assert.Single(receipts); Assert.Equal("Recorded", receipts[0].State); Assert.Equal("Vnpay", receipts[0].Method); Assert.Equal(10000, receipts[0].Amount); Assert.Null(receipts[0].VerifiedAt); Assert.False(await db.FundAllocations.AnyAsync(x => x.RentalAccountId == account));
        Assert.Equal(receipts[0].Id, (await db.PaymentIntents.SingleAsync(x => x.Id == id)).PaymentReceiptId);
    }
    [Fact]
    public async Task Momo_timeout_never_reissues_an_uncertain_external_create_and_signed_ipn_reconciles()
    {
        var calls = 0; using var host = Host().WithWebHostBuilder(b => b.ConfigureServices(services => { services.RemoveAll<PaymentGateway>(); services.AddScoped(_ => new PaymentGateway(new HttpClient(new SyntheticHandler(async (_, _) => { Interlocked.Increment(ref calls); await Task.Yield(); throw new HttpRequestException("Synthetic lost response"); })))); }));
        using var customer = await Login(host); var account = await Account(); var created = await Create(customer, account, "MomoWallet"); var id = created.GetProperty("resourceId").GetGuid(); var key = Guid.NewGuid();
        foreach (var commandKey in new[] { key, key, Guid.NewGuid() }) Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/payment-intents/{id}/checkout", new { reason = "Synthetic unknown" }, key: commandKey)).StatusCode);
        Assert.Equal(1, calls); Assert.Equal("Unknown", (await Json(await customer.Send(HttpMethod.Get, $"/api/v1/payment-intents/{id}"))).GetProperty("state").GetString());
        using var anonymous = host.CreateClient(); var payload = MomoPayload(id, "123456789");
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/payment-notifications/momo", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/payment-notifications/momo", payload)).StatusCode);
        Assert.Equal("Paid", (await Json(await customer.Send(HttpMethod.Get, $"/api/v1/payment-intents/{id}"))).GetProperty("state").GetString());
        await using var db = fixture.Db(); Assert.False(await db.PaymentReceipts.AnyAsync(x => x.RentalAccountId == account));
    }
    internal static Dictionary<string, string> MomoPayload(Guid id, string transaction, string amount = "10000", string result = "0")
    {
        var values = new Dictionary<string, string> { ["partnerCode"] = "TESTMERCHANT", ["requestId"] = id.ToString("N"), ["orderId"] = id.ToString("N"), ["amount"] = amount, ["orderInfo"] = "WarehouseRent " + id.ToString("N"), ["orderType"] = "momo_wallet", ["transId"] = transaction, ["resultCode"] = result, ["message"] = "Synthetic result", ["payType"] = "qr", ["responseTime"] = "1793246400000", ["extraData"] = "" };
        var keys = new[] { "amount", "extraData", "message", "orderId", "orderInfo", "orderType", "partnerCode", "payType", "requestId", "responseTime", "resultCode", "transId" };
        var raw = "accessKey=SYNTHETICACCESS&" + string.Join("&", keys.Select(k => k + "=" + values[k])); values["signature"] = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(); return values;
    }
    [Theory]
    [InlineData("Vnpay", "07", "07")]
    [InlineData("Vnpay", "00", "01")]
    [InlineData("Vnpay", "00", "04")]
    [InlineData("Vnpay", "00", "05")]
    [InlineData("Vnpay", "00", "06")]
    [InlineData("Momo", "1000", "")]
    [InlineData("Momo", "7000", "")]
    [InlineData("Momo", "7002", "")]
    [InlineData("Momo", "9000", "")]
    public async Task Pending_notifications_preserve_evidence_block_closure_and_reconcile_once(string provider, string code, string status)
    {
        using var host = Host().WithWebHostBuilder(b => b.ConfigureServices(services => {
            services.RemoveAll<PaymentGateway>();
            services.AddScoped(_ => new PaymentGateway(new HttpClient(new SyntheticHandler((_, _) => throw new HttpRequestException("Synthetic pending provider")))));
        }));
        using var customer = await Login(host); using var finance = await Login(host, "finance@example.test");
        using var anonymous = host.CreateClient(); var account = await Account();
        var made = await Create(customer, account, provider == "Momo" ? "MomoWallet" : "VnpayCard");
        var id = made.GetProperty("resourceId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await customer.Send(HttpMethod.Post, $"/api/v1/payment-intents/{id}/checkout", new { reason = "Prepare synthetic checkout" }, key: Guid.NewGuid())).StatusCode);
        var transaction = string.Concat(Guid.NewGuid().ToByteArray().Select(x => (x % 10).ToString())) + "1";
        async Task Notify(string result, string transactionStatus)
        {
            if (provider == "Momo") Assert.Equal(HttpStatusCode.NoContent, (await anonymous.PostAsJsonAsync("/api/payment-notifications/momo", MomoPayload(id, transaction, result: result))).StatusCode);
            else {
                var raw = $"vnp_Amount=1000000&vnp_ResponseCode={result}&vnp_TmnCode=TESTMERCHANT&vnp_TransactionNo={transaction}&vnp_TransactionStatus={transactionStatus}&vnp_TxnRef={id:N}";
                var signature = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
                var response = await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?" + raw + "&vnp_SecureHash=" + signature));
                Assert.Contains(response.GetProperty("RspCode").GetString(), new[] { "00", "02" });
            }
        }
        await Notify(code, status);
        await using (var db = fixture.Db()) {
            var pending = await db.PaymentIntents.SingleAsync(x => x.Id == id);
            Assert.Equal("Unknown", pending.State); Assert.Equal(transaction, pending.TransactionId);
            Assert.Null(pending.PaymentReceiptId); Assert.Null(pending.PaidAt);
            Assert.Contains(await db.SecurityAudits.Where(x => x.AggregateId == id && x.Action == "PaymentNotification").Select(x => x.Reason).ToListAsync(), x => x.Contains(code) && x.Contains(transaction));
        }
        var readiness = await Json(await finance.Send(HttpMethod.Get, $"/api/v1/rental-accounts/{account}/settlement"));
        Assert.Contains(readiness.GetProperty("blockers").EnumerateArray(), x => x.GetString() == "UNRESOLVED_ONLINE_PAYMENTS");
        await Notify(provider == "Momo" ? "0" : "00", "00");
        // An older pending callback must not undo captured success or create more money.
        await Notify(code, status);
        await using (var db = fixture.Db()) {
            var paid = await db.PaymentIntents.SingleAsync(x => x.Id == id);
            Assert.Equal("Paid", paid.State); Assert.NotNull(paid.PaidAt);
            Assert.False(await db.PaymentReceipts.AnyAsync(x => x.RentalAccountId == account));
        }
    }

    [Fact]
    public async Task Multiple_methods_require_current_customer_scope_and_original_command_replays()
    {
        using var host = Host(); using var customer = await Login(host); using var foreign = await Login(host, "admin.b@example.test"); var account = await Account(); var key = Guid.NewGuid();
        var first = await Create(customer, account, key: key); var replay = await Create(customer, account, key: key); Assert.Equal(first.GetProperty("resourceId").GetGuid(), replay.GetProperty("resourceId").GetGuid());
        foreach (var method in new[] { "VnpayCard", "VnpayBank", "MomoWallet" }) Assert.Equal(method, (await Create(customer, account, method)).GetProperty("method").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, "/api/v1/payment-intents", new { rentalAccountId = account, method = "VnpayQr", amount = "10000", reason = "Foreign scope" }, key: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, "/api/v1/payment-intents/" + first.GetProperty("resourceId").GetString())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await customer.Send(HttpMethod.Post, "/api/v1/payment-intents", new { rentalAccountId = account, method = "VisaNumber", amount = "0.1", reason = "Invalid" }, key: Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Hosted_vnpay_uses_explicit_method_and_only_signed_ipn_records_sandbox_status_without_money()
    {
        using var host = Host(); using var customer = await Login(host); var account = await Account(); var intent = await Create(customer, account, "VnpayCard"); var id = intent.GetProperty("resourceId").GetGuid();
        var checkout = await customer.Send(HttpMethod.Post, $"/api/v1/payment-intents/{id}/checkout", new { reason = "Open hosted checkout" }, key: Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var read = await Json(await customer.Send(HttpMethod.Get, $"/api/v1/payment-intents/{id}")); var url = read.GetProperty("checkoutUrl").GetString()!; Assert.StartsWith("https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?", url); Assert.Contains("vnp_BankCode=INTCARD", url); Assert.Contains("vnp_Amount=1000000", url);
        using var anonymous = host.CreateClient();
        var query = VnpayQuery(read.GetProperty("orderId").GetString()!);
        var tampered = await anonymous.GetAsync("/api/payment-notifications/vnpay?" + query.Replace("1000000", "2000000")); Assert.Equal("97", (await Json(tampered)).GetProperty("RspCode").GetString());
        Assert.Equal("04", (await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?" + VnpayQuery(read.GetProperty("orderId").GetString()!, "2000000")))).GetProperty("RspCode").GetString());
        Assert.Equal("00", (await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?" + query))).GetProperty("RspCode").GetString());
        Assert.Equal("02", (await Json(await anonymous.GetAsync("/api/payment-notifications/vnpay?" + query))).GetProperty("RspCode").GetString());
        var paid = await Json(await customer.Send(HttpMethod.Get, $"/api/v1/payment-intents/{id}")); Assert.Equal("Paid", paid.GetProperty("state").GetString()); Assert.Equal("Sandbox", paid.GetProperty("mode").GetString());
        await using var db = fixture.Db(); Assert.False(await db.PaymentReceipts.AnyAsync(x => x.RentalAccountId == account)); Assert.False(await db.FundAllocations.AnyAsync(x => x.RentalAccountId == account));
    }
    [Fact]
    public async Task Disabled_and_unguarded_live_configuration_never_offer_checkout()
    {
        using var disabled = Host("Disabled"); using var b = await Login(disabled); var account = await Account();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await b.Send(HttpMethod.Post, "/api/v1/payment-intents", new { rentalAccountId = account, method = "VnpayBank", amount = "10000", reason = "Disabled" }, key: Guid.NewGuid())).StatusCode);
        using var live = Host("Live"); using var c = await Login(live);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.Send(HttpMethod.Post, "/api/v1/payment-intents", new { rentalAccountId = account, method = "VnpayBank", amount = "10000", reason = "Live refused outside Production" }, key: Guid.NewGuid())).StatusCode);
    }
}
internal sealed class SyntheticHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
