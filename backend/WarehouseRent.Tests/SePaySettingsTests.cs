using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WarehouseRent.Api.Features.OnlinePayments;
namespace WarehouseRent.Tests;
public sealed class SePaySettingsTests
{
    internal static Dictionary<string,string?> Values() => new() {
        ["Payments:SePay:Enabled"]="true", ["Payments:SePay:Mode"]="Sandbox", ["Payments:SePay:Bank"]="TESTBANK",
        ["Payments:SePay:AccountNumber"]="TEST0000001", ["Payments:SePay:Secret"]=new string('s',32) };
    [Fact] public void Disabled_missing_or_unsafe_settings_fail_closed()
    {
        var v=Values(); SePaySettings? Read(string env="Testing")=>SePaySettings.Read(new ConfigurationBuilder().AddInMemoryCollection(v).Build(),new EnvironmentFixture(env));
        Assert.NotNull(Read()); v.Remove("Payments:SePay:Enabled"); Assert.Null(Read()); v["Payments:SePay:Enabled"]="true";
        Assert.Null(Read("Production")); v["Payments:SePay:AccountNumber"]="123456"; Assert.Null(Read());
        v["Payments:SePay:Mode"]="Live"; v["Payments:SePay:Bank"]="SyntheticBank"; Assert.Null(Read("Production"));
        v["Payments:SePay:AllowLive"]="true"; Assert.Null(Read("Production")); v["Payments:SePay:ZeroFeeCapVerified"]="true"; Assert.NotNull(Read("Production")); Assert.Null(Read("Testing"));
        v["Payments:SePay:Secret"]="short"; Assert.Null(Read("Production"));
    }
    [Fact] public void Reference_is_crypto_random_bounded_and_recipient_fingerprint_is_unambiguous()
    {
        var refs=Enumerable.Range(0,1000).Select(_=>SePaySettings.NewReference()).ToArray();
        Assert.Equal(1000,refs.Distinct().Count()); Assert.All(refs,r=>Assert.Matches("^SEVQR[A-Z0-9]{10}$",r));
        Assert.NotEqual(SePaySettings.RecipientFingerprint("AB","C"),SePaySettings.RecipientFingerprint("A","BC"));
        Assert.Equal(64,SePaySettings.RecipientFingerprint("AB","C").Length);
    }
    [Theory]
    [InlineData("true","true","Production",true)]
    [InlineData("false","true","Production",false)]
    [InlineData("True","true","Production",false)]
    [InlineData("","true","Production",false)]
    [InlineData("true","false","Production",false)]
    [InlineData("true","true","Testing",false)]
    [InlineData("true","true","Demo",false)]
    public void Explicit_overage_approval_is_a_distinct_budget_gate_not_a_zero_fee_attestation(
        string approval,string allowLive,string environment,bool available)
    {
        var values=Values();
        values["Payments:SePay:Mode"]="Live";
        values["Payments:SePay:Bank"]="SyntheticBank";
        values["Payments:SePay:AllowLive"]=allowLive;
        values["Payments:SePay:ZeroFeeCapVerified"]="false";
        values["Payments:SePay:OverageFeesApproved"]=approval;
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var settings=SePaySettings.Read(configuration,new EnvironmentFixture(environment));
        Assert.Equal(available,settings is not null);
        Assert.Equal("false",configuration["Payments:SePay:ZeroFeeCapVerified"]);
    }
    private sealed class EnvironmentFixture(string name):IHostEnvironment {
        public string EnvironmentName {get;set;}=name; public string ApplicationName {get;set;}="Tests";
        public string ContentRootPath {get;set;}=""; public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
    }
}
