using WarehouseRent.Infrastructure.Persistence;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WarehouseRent.Infrastructure.Identity;

namespace WarehouseRent.Tests;
public sealed partial class SqlTariffTests
{
    [Fact]
    public async Task Concurrent_rate_edits_revalidate_version_and_history_is_manager_only()
    {
        var unit=await Unit();using var a=await fixture.Login("manager@example.test");using var b=await fixture.Login("operator@example.test");
        var responses=await Task.WhenAll(a.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,Guid.NewGuid()),b.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly("3200"),unit.Version,Guid.NewGuid()));
        Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(responses,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
        var history=await Data(await a.Send(HttpMethod.Get,$"/api/v1/units/{unit.Id}/rates"));Assert.Single(history.GetProperty("items").EnumerateArray());
        using var customer=await fixture.Login();Assert.Equal(HttpStatusCode.NotFound,(await customer.Send(HttpMethod.Get,$"/api/v1/units/{unit.Id}/rates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await customer.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await a.Send(HttpMethod.Get,$"/api/v1/units/{unit.Id}/rates?page=-1")).StatusCode);
    }
    [Fact]
    public async Task Future_monthly_rate_does_not_reprice_current_sale_and_new_rate_invalidates_old_hold()
    {
        var unit=await Unit();using var manager=await fixture.Login("operator@example.test");using var customer=await fixture.Login();
        await using(var seed=fixture.Db()){seed.RateVersions.Add(new WarehouseRent.Infrastructure.Facilities.RateVersion {Id=Guid.NewGuid(),UnitId=unit.Id,DailyRate=100,SalesValidFrom=Today.AddDays(-1)});await seed.SaveChangesAsync();}
        var quote=await Data(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id),key:Guid.NewGuid()));var q=quote.GetProperty("result");var quoteId=q.GetProperty("resourceId").GetGuid();
        var before=(await Data(await customer.Send(HttpMethod.Get,$"/api/v1/quotes/{quoteId}"))).GetRawText();
        await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(from:Today.AddDays(1)),unit.Version,Guid.NewGuid()));
        var current=await ResultId(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id),key:Guid.NewGuid()));
        Assert.Equal("2000",(await Data(await customer.Send(HttpMethod.Get,$"/api/v1/quotes/{current}"))).GetProperty("rentalSubtotal").GetString());
        Assert.Equal(before,(await Data(await customer.Send(HttpMethod.Get,$"/api/v1/quotes/{quoteId}"))).GetRawText());
        // The same-day version cannot overlap an existing future rate; its explicit end is required.
        await using var db=fixture.Db();var u=await db.RentableUnits.SingleAsync(x=>x.Id==unit.Id);
        await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",new{dailyRate="0",billingBasis="CalendarMonth",monthlyRate="3000",salesValidFrom=Today,salesValidTo=Today.AddDays(1),reason="Replace today"},Convert.ToBase64String(u.Version),Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict,(await customer.Send(HttpMethod.Post,$"/api/v1/quotes/{quoteId}/holds",new{expectedQuoteVersion=q.GetProperty("version").GetString()},key:Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task Invalid_basis_and_economic_rewrites_and_populated_downgrade_are_refused()
    {
        var unit=await Unit();using var manager=await fixture.Login("operator@example.test");
        foreach(var input in new object[]{new{dailyRate="1",billingBasis="CalendarMonth",monthlyRate="3100",salesValidFrom=Today,salesValidTo=(DateOnly?)null,reason="Bad mixed price"},new{dailyRate="0",billingBasis="CalendarMonth",monthlyRate="0",salesValidFrom=Today,salesValidTo=(DateOnly?)null,reason="Zero price"},new{dailyRate="1",billingBasis="Daily",monthlyRate="3100",salesValidFrom=Today,salesValidTo=(DateOnly?)null,reason="Ambiguous daily"}})
            Assert.Equal(HttpStatusCode.UnprocessableEntity,(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",input,unit.Version,Guid.NewGuid())).StatusCode);
        await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,Guid.NewGuid()));
        await using(var db=fixture.Db()){var rate=await db.RateVersions.SingleAsync(x=>x.UnitId==unit.Id);rate.MonthlyRate=999;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());}
        using var customer=await fixture.Login();var quoteId=await ResultId(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id),key:Guid.NewGuid()));
        await using(var db=fixture.Db()){var line=await db.QuoteLines.FirstAsync(x=>x.QuoteId==quoteId);db.QuoteLines.Remove(line);await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());}
        await using(var db=fixture.Db()){if(db.Database.IsNpgsql()){var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE khospace.\"RateVersions\" SET \"BillingBasis\"='Daily' WHERE \"UnitId\"={unit.Id}"));Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation,error.SqlState);}else{await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE RateVersions SET BillingBasis='Daily' WHERE UnitId={unit.Id}"));}}
        await using(var db=fixture.Db()){if(db.Database.IsNpgsql()){await Assert.ThrowsAsync<InvalidOperationException>(()=>db.GetService<IMigrator>().MigrateAsync("20261029095457_GateVisitScheduling"));Assert.True(await PostgresMigrations.IsReadyAsync(db,default));}else{await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(()=>db.GetService<IMigrator>().MigrateAsync("20261029095457_GateVisitScheduling"));}Assert.True(await db.QuoteLines.AnyAsync(x=>x.QuoteId==quoteId && x.BillingBasis=="CalendarMonth"));}
    }
}
