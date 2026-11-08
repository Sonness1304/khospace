using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed partial class SqlTariffTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        using var json = JsonDocument.Parse(text); return json.RootElement.Clone();
    }
    private static async Task<Guid> ResultId(HttpResponseMessage response) => (await Data(response)).GetProperty("result").GetProperty("resourceId").GetGuid();
    private async Task<(Guid Id, string Version)> Unit()
    {
        using var manager = await fixture.Login("operator@example.test");
        var id = await ResultId(await manager.Send(HttpMethod.Post, $"/api/v1/facilities/{DemoIdentitySeed.FacilityA}/units", new { zoneId = Guid.Parse("a0000000-0000-4000-8000-000000000000"), code = "T-" + Guid.NewGuid().ToString("N")[..12], areaM2 = "10", published = true, reason = "Synthetic tariff unit" }, key: Guid.NewGuid()));
        await using var db=fixture.Db(); return (id,Convert.ToBase64String((await db.RentableUnits.SingleAsync(x=>x.Id==id)).Version));
    }
    private static object Monthly(string amount="3100", DateOnly? from=null) => new { dailyRate="0", billingBasis="CalendarMonth", monthlyRate=amount, salesValidFrom=from??Today, salesValidTo=(DateOnly?)null, reason="Synthetic calendar-month tariff" };
    private static object Quote(Guid unit, string start="2028-01-31",string end="2028-02-02") => new { facilityId=DemoIdentitySeed.FacilityA, organizationId=DemoIdentitySeed.OrganizationA, unitIds=new[]{unit}, startDate=start,endDate=end };

    [Fact]
    public async Task Monthly_rate_produces_explicit_leap_month_snapshot_and_preserves_replay()
    {
        var unit=await Unit(); using var manager=await fixture.Login("operator@example.test"); var key=Guid.NewGuid(); var input=Monthly();
        var saved=await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",input,unit.Version,key);
        var original=await Data(saved);
        Assert.Equal(original.GetRawText(),(await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",input,unit.Version,key))).GetRawText());
        using var customer=await fixture.Login(); var quoteId=await ResultId(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id),key:Guid.NewGuid()));
        var quote=await Data(await customer.Send(HttpMethod.Get,$"/api/v1/quotes/{quoteId}"));
        Assert.Equal("2069",quote.GetProperty("rentalSubtotal").GetString()); Assert.Equal("414",quote.GetProperty("requiredDeposit").GetString());
        var lines=quote.GetProperty("lines").EnumerateArray().ToArray(); Assert.Equal(2,lines.Length);
        Assert.Equal(new[]{31,29},lines.Select(x=>x.GetProperty("calendarDays").GetInt32()));
        Assert.All(lines,line=>{Assert.Equal("CalendarMonth",line.GetProperty("billingBasis").GetString());Assert.Equal("3100",line.GetProperty("monthlyRate").GetString());Assert.Equal("0",line.GetProperty("dailyRate").GetString());});
        Assert.False(quote.GetProperty("reservation").GetBoolean());
    }
}
