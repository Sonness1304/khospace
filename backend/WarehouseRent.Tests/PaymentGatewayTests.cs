using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using WarehouseRent.Api.Features.OnlinePayments;
using WarehouseRent.Infrastructure.Finance;
namespace WarehouseRent.Tests;
public sealed class PaymentGatewayTests
{
    private static GatewaySettings Settings(string p="Momo",string mode="Sandbox")=>new(p,mode,"TESTMERCHANT",SqlOnlinePaymentTests.Secret,"SYNTHETICACCESS","https://warehouse.example.test");
    [Fact]
    public void Hmac_matches_external_known_vector_and_rejects_malformed_signature()
    {
        // RFC4231 test case2: key Jefe, data what do ya want for nothing?
        const string expected="5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843";
        Assert.Equal(expected,PaymentGateway.Hmac("what do ya want for nothing?","Jefe")); Assert.True(PaymentGateway.Signature("what do ya want for nothing?","Jefe",expected));
        Assert.False(PaymentGateway.Signature("changed","Jefe",expected)); Assert.False(PaymentGateway.Signature("x","Jefe",new string('z',64))); Assert.False(PaymentGateway.Signature("x","Jefe",null));
        Assert.Equal("a=a+b&z=%2B%26",PaymentGateway.VnpayData(new Dictionary<string,string>{{"z","+&"},{"a","a b"},{"vnp_SecureHash","ignored"},{"empty",""}}));
    }
    [Theory]
    [InlineData("https://test-payment.momo.vn/v2/gateway/pay?token=1",true)]
    [InlineData("https://test-payment.momo.vn.evil.test/v2/gateway/pay",false)]
    [InlineData("http://test-payment.momo.vn/v2/gateway/pay",false)]
    [InlineData("https://payment.momo.vn/v2/gateway/pay",false)]
    [InlineData("https://test-payment.momo.vn/v2/gateway/api/create",false)]
    [InlineData("https://user@test-payment.momo.vn/v2/gateway/pay",false)]
    public void Hosted_link_is_exact_https_mode_host_and_path(string url,bool expected)=>Assert.Equal(expected,Settings().SafeCheckout(url));
    [Fact]
    public void Config_fails_closed_for_missing_secret_bad_origin_and_live_guard()
    {
        var values=new Dictionary<string,string?>{{"Payments:PublicOrigin","https://warehouse.example.test"},{"Payments:Momo:Mode","Sandbox"},{"Payments:Momo:MerchantId","TESTMERCHANT"},{"Payments:Momo:Secret",SqlOnlinePaymentTests.Secret},{"Payments:Momo:AccessKey","SYNTHETIC"}};
        GatewaySettings? Read()=>GatewaySettings.Read(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),new TestEnvironment(),"Momo");
        Assert.NotNull(Read());values["Payments:PublicOrigin"]="https://warehouse.example.test/path";Assert.Null(Read());values["Payments:PublicOrigin"]="http://localhost";Assert.Null(Read());values["Payments:PublicOrigin"]="https://warehouse.example.test";values["Payments:Momo:Mode"]="Live";Assert.Null(Read());values["Payments:Momo:Mode"]="Sandbox";values["Payments:Momo:Secret"]="";Assert.Null(Read());
        values["Payments:Momo:Secret"]=SqlOnlinePaymentTests.Secret;values["Payments:Momo:MerchantId"]="MOMOT5BZ20231213_TEST";Assert.NotNull(Read());
    }
    [Fact]
    public void Duplicate_case_ambiguous_and_complex_notification_json_is_rejected()
    {
        foreach(var raw in new[]{"{\"amount\":1,\"amount\":2}","{\"amount\":1,\"Amount\":1}","{\"amount\":{}}","[]"}){using var doc=JsonDocument.Parse(raw);Assert.Throws<FormatException>(()=>PaymentGateway.ParseObject(doc.RootElement));}
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Momo_request_is_signed_and_response_signature_is_required(bool tamper)
    {
        var intent=new PaymentIntent{Id=Guid.NewGuid(),Provider="Momo",Mode="Sandbox",MerchantId="TESTMERCHANT",Method="MomoWallet",Amount=10000,CreatedBy="synthetic"};
        using var http=new HttpClient(new SyntheticHandler(async(request,ct)=>{
            Assert.Equal("https://test-payment.momo.vn/v2/gateway/api/create",request.RequestUri!.ToString()); var body=await request.Content!.ReadFromJsonAsync<JsonElement>(ct);var id=intent.Id.ToString("N");
            var raw=$"accessKey=SYNTHETICACCESS&amount=10000&extraData=&ipnUrl=https://warehouse.example.test/api/payment-notifications/momo&orderId={id}&orderInfo=WarehouseRent {id}&partnerCode=TESTMERCHANT&redirectUrl=https://warehouse.example.test/payments/return&requestId={id}&requestType=captureWallet";
            Assert.Equal(Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SqlOnlinePaymentTests.Secret),Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(),body.GetProperty("signature").GetString());Assert.True(body.GetProperty("autoCapture").GetBoolean());
            var values=new Dictionary<string,string>{{"amount","10000"},{"message","Success"},{"orderId",id},{"partnerCode","TESTMERCHANT"},{"payUrl","https://test-payment.momo.vn/v2/gateway/pay?fixture=1"},{"requestId",id},{"responseTime","1793246400000"},{"resultCode","0"}};
            var signed=$"accessKey=SYNTHETICACCESS&amount=10000&message=Success&orderId={id}&partnerCode=TESTMERCHANT&payUrl={values["payUrl"]}&requestId={id}&responseTime=1793246400000&resultCode=0";
            values["signature"]=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(SqlOnlinePaymentTests.Secret),Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();if(tamper)values["payUrl"]="https://evil.test/pay";
            return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(values)};
        }));
        var gateway=new PaymentGateway(http);if(tamper)await Assert.ThrowsAsync<FormatException>(()=>gateway.Checkout(Settings(),intent,"127.0.0.1",CancellationToken.None));else Assert.StartsWith("https://test-payment.momo.vn/v2/gateway/pay",await gateway.Checkout(Settings(),intent,"127.0.0.1",CancellationToken.None));
    }
    private sealed class TestEnvironment:IHostEnvironment
    {public string EnvironmentName{get;set;}="Testing";public string ApplicationName{get;set;}="Test";public string ContentRootPath{get;set;}=".";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
