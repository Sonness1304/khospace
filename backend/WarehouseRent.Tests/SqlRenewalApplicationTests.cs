using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Tests;
[Trait("Category", "Sql")]
public sealed class SqlRenewalApplicationTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return doc.RootElement.Clone();
    }
    private async Task<(Guid Lease, Guid Grant, Guid Quote, string LeaseVersion, string QuoteVersion, DateOnly End)> Setup(int offset)
    {
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).AddDays(offset); var end = start.AddDays(3);
        var grant = await fixture.SeedOccupancyOwner(SqlFacilitiesTests.UnitA, start, end);
        await using var db = fixture.Db(); var lu = await db.LeaseUnits.SingleAsync(x => x.Id == grant); var lease = await db.Leases.SingleAsync(x => x.Id == lu.LeaseId);
        foreach(var old in await db.UnitOccupancies.Where(x=>x.UnitId==lu.UnitId && x.ClearedAt==null).ToListAsync()) old.ClearedAt=DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        db.UnitOccupancies.Add(new WarehouseRent.Infrastructure.Facilities.UnitOccupancy { Id = Guid.NewGuid(), UnitId = lu.UnitId, LeaseUnitId = grant, OrganizationId = lease.OrganizationId, ScheduledEnd = end, HandedOverAt = DateTimeOffset.UtcNow });
        var original=new LeaseVersion { Id = Guid.NewGuid(), LeaseId = lease.Id, VersionNo = 1, PolicyVersion = "SyntheticOriginal" };db.LeaseVersions.Add(original);
        var unit=await db.RentableUnits.SingleAsync(x=>x.Id==lu.UnitId);var rate=await db.RateVersions.FirstAsync(x=>x.UnitId==lu.UnitId);var booking=await db.Bookings.SingleAsync(x=>x.Id==lease.BookingId);
        var source=new QuoteLine{Id=Guid.NewGuid(),QuoteId=booking.QuoteId,UnitId=lu.UnitId,UnitCode=unit.Code,UnitVersion=unit.Version.ToArray(),RateVersionId=rate.Id,StartDate=start,EndDate=end,Days=3,AreaM2=unit.AreaM2,DailyRate=rate.DailyRate,Amount=100};db.QuoteLines.Add(source);
        db.LeaseVersionLines.Add(new LeaseVersionLine{Id=Guid.NewGuid(),LeaseVersionId=original.Id,LeaseUnitId=grant,SourceQuoteLineId=source.Id,StartDate=start,EndDate=end,Days=3,AreaM2=unit.AreaM2,DailyRate=rate.DailyRate,Amount=100});await db.SaveChangesAsync();
        using var customer = await fixture.Login(); var quoted = (await Ok(await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = lease.OrganizationId, facilityId = lease.FacilityId, unitIds = new[] { lu.UnitId }, startDate = end, endDate = end.AddDays(2) }, key: Guid.NewGuid()))).GetProperty("result");
        return (lease.Id, grant, quoted.GetProperty("resourceId").GetGuid(), Convert.ToBase64String(lease.Version), quoted.GetProperty("version").GetString()!, end);
    }
    [Fact]
    public async Task Audit_failure_rolls_back_renewal_version_capacity_and_saved_command()
    {
        var s=await Setup(160);using var customer=await fixture.Login();var path=$"/api/v1/leases/{s.Lease}/renewals";
        var id=(await Ok(await customer.Send(HttpMethod.Post,path,new {quoteId=s.Quote,expectedLeaseVersion=s.LeaseVersion,expectedQuoteVersion=s.QuoteVersion,reason="Accept before fault"},key:Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        await using var host=fixture.Host.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>{services.RemoveAll<AppDbContext>();services.AddScoped(_=>new AppDbContext(new DbContextOptionsBuilder<AppDbContext>(fixture.Options()).AddInterceptors(new RejectRenewalAudit()).Options));}));
        using var sales=new BrowserSession(host.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost"),HandleCookies=false}));await sales.RefreshCsrf();await Ok(await sales.Send(HttpMethod.Post,"/api/v1/auth/login",new {userName="sales@example.test",password=DemoIdentitySeed.Password}));await sales.RefreshCsrf();var key=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.InternalServerError,(await sales.Send(HttpMethod.Post,$"{path}/{id}/apply",new {reason="Rollback renewal fault"},key:key)).StatusCode);
        await using var db=fixture.Db();Assert.Equal(s.End,(await db.Leases.SingleAsync(x=>x.Id==s.Lease)).EndDate);Assert.Equal(s.End,(await db.UnitOccupancies.SingleAsync(x=>x.LeaseUnitId==s.Grant)).ScheduledEnd);Assert.Equal(1,await db.LeaseVersions.CountAsync(x=>x.LeaseId==s.Lease));Assert.Null((await db.RenewalAcceptances.SingleAsync(x=>x.Id==id)).AppliedResultJson);Assert.False(await db.OperationalCommands.AnyAsync(x=>x.Id==key));Assert.False(await db.SecurityAudits.AnyAsync(x=>x.Action=="LeaseRenewed"&&x.AggregateId==s.Lease));
    }
    private sealed class RejectRenewalAudit:SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,int result,CancellationToken cancellationToken=default)
        {
            if(eventData.Context!.ChangeTracker.Entries<SecurityAudit>().Any(x=>x.Entity.Action=="LeaseRenewed"))throw new InvalidOperationException("Synthetic renewal audit failure");return ValueTask.FromResult(result);
        }
    }
    [Fact]
    public async Task Two_different_accepted_quotes_cannot_extend_the_same_contract_version_twice()
    {
        var s=await Setup(180);using var customer=await fixture.Login();using var sales=await fixture.Login("sales@example.test");using var secondSales=await fixture.Login("sales@example.test");var path=$"/api/v1/leases/{s.Lease}/renewals";
        var q=(await Ok(await customer.Send(HttpMethod.Post,"/api/v1/quotes",new {organizationId=DemoIdentitySeed.OrganizationA,facilityId=DemoIdentitySeed.FacilityA,unitIds=new[]{SqlFacilitiesTests.UnitA},startDate=s.End,endDate=s.End.AddDays(3)},key:Guid.NewGuid()))).GetProperty("result");
        async Task<Guid> Accept(Guid quote,string version)=>(await Ok(await customer.Send(HttpMethod.Post,path,new{quoteId=quote,expectedLeaseVersion=s.LeaseVersion,expectedQuoteVersion=version,reason="Accept a candidate extension"},key:Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        var one=await Accept(s.Quote,s.QuoteVersion);var two=await Accept(q.GetProperty("resourceId").GetGuid(),q.GetProperty("version").GetString()!);
        var outcomes=await Task.WhenAll(sales.Send(HttpMethod.Post,$"{path}/{one}/apply",new{reason="First candidate"},key:Guid.NewGuid()),secondSales.Send(HttpMethod.Post,$"{path}/{two}/apply",new{reason="Second candidate"},key:Guid.NewGuid()));Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.PreconditionFailed);
        await using var db=fixture.Db();Assert.Equal(2,await db.LeaseVersions.CountAsync(x=>x.LeaseId==s.Lease));Assert.Equal(1,await db.RenewalAcceptances.CountAsync(x=>x.LeaseId==s.Lease&&x.AppliedResultJson!=null));
    }
    [Fact]
    public async Task Renewal_and_future_hold_cannot_both_reserve_added_dates()
    {
        var s=await Setup(140);using var customer=await fixture.Login();using var sales=await fixture.Login("sales@example.test");var path=$"/api/v1/leases/{s.Lease}/renewals";
        var id=(await Ok(await customer.Send(HttpMethod.Post,path,new {quoteId=s.Quote,expectedLeaseVersion=s.LeaseVersion,expectedQuoteVersion=s.QuoteVersion,reason="Accept renewal before competing hold"},key:Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        var outcomes=await Task.WhenAll(sales.Send(HttpMethod.Post,$"{path}/{id}/apply",new {reason="Race renewal"},key:Guid.NewGuid()),customer.Send(HttpMethod.Post,$"/api/v1/quotes/{s.Quote}/holds",new {expectedQuoteVersion=s.QuoteVersion},key:Guid.NewGuid()));
        Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(outcomes,x=>x.StatusCode==HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Conflict_after_acceptance_keeps_contract_and_receipt_unapplied()
    {
        var s=await Setup(60); using var customer=await fixture.Login(); using var sales=await fixture.Login("sales@example.test");
        var path=$"/api/v1/leases/{s.Lease}/renewals"; var accepted=(await Ok(await customer.Send(HttpMethod.Post,path,new { quoteId=s.Quote,expectedLeaseVersion=s.LeaseVersion,expectedQuoteVersion=s.QuoteVersion,reason="Accept extension" },key:Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        var line=await fixture.SeedAllocationOwner(SqlFacilitiesTests.UnitA,s.End,s.End.AddDays(2));
        await using(var db=fixture.Db()){db.SpaceAllocations.Add(new WarehouseRent.Infrastructure.Facilities.SpaceAllocation {Id=Guid.NewGuid(),UnitId=SqlFacilitiesTests.UnitA,BookingLineId=line,StartDate=s.End,EndDate=s.End.AddDays(2),State="Confirmed"});await db.SaveChangesAsync();}
        var key=Guid.NewGuid();Assert.Equal(HttpStatusCode.Conflict,(await sales.Send(HttpMethod.Post,$"{path}/{accepted}/apply",new {reason="Must recheck capacity"},key:key)).StatusCode);
        await using var check=fixture.Db();Assert.Equal(s.End,(await check.Leases.SingleAsync(x=>x.Id==s.Lease)).EndDate);Assert.Null((await check.RenewalAcceptances.SingleAsync(x=>x.Id==accepted)).AppliedResultJson);Assert.Equal(1,await check.LeaseVersions.CountAsync(x=>x.LeaseId==s.Lease));Assert.False(await check.OperationalCommands.AnyAsync(x=>x.Id==key));
    }
    [Theory]
    [InlineData("Expired")]
    [InlineData("Stale")]
    public async Task Application_revalidates_quote_version_and_expiry(string change)
    {
        var s=await Setup(change=="Expired"?80:change=="Stale"?100:120);using var customer=await fixture.Login();using var sales=await fixture.Login("sales@example.test");var path=$"/api/v1/leases/{s.Lease}/renewals";
        var id=(await Ok(await customer.Send(HttpMethod.Post,path,new {quoteId=s.Quote,expectedLeaseVersion=s.LeaseVersion,expectedQuoteVersion=s.QuoteVersion,reason="Accept unchanged deposit"},key:Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        await using(var db=fixture.Db()){
            if(change=="Expired"){/* Explicit fault injection bypasses the immutable snapshot guard in this owned test database. */ if(db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE khospace.\"Quotes\" SET \"CreatedAt\"={DateTimeOffset.UtcNow.AddMinutes(-2)},\"ExpiresAt\"={DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE \"Id\"={s.Quote}"); else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Quotes SET CreatedAt={DateTimeOffset.UtcNow.AddMinutes(-2)},ExpiresAt={DateTimeOffset.UtcNow.AddMinutes(-1)} WHERE Id={s.Quote}");}
            else if(change=="Stale"){var lease=await db.Leases.SingleAsync(x=>x.Id==s.Lease);lease.State="ClearancePending";await db.SaveChangesAsync();}
        }
        var response=await sales.Send(HttpMethod.Post,$"{path}/{id}/apply",new {reason="Recheck accepted renewal"},key:Guid.NewGuid());Assert.True(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed);
        await using var check=fixture.Db();Assert.Equal(s.End,(await check.Leases.SingleAsync(x=>x.Id==s.Lease)).EndDate);Assert.Null((await check.RenewalAcceptances.SingleAsync(x=>x.Id==id)).AppliedResultJson);
    }
    [Fact]
    public async Task Renewal_in_issued_month_keeps_original_statement_and_finance_supplements_added_segments_once()
    {
        var s = await Setup(120); using var customer = await fixture.Login(); using var sales = await fixture.Login("sales@example.test"); using var finance = await fixture.Login("finance@example.test");
        Guid statementId;
        await using (var db = fixture.Db()) { var lease = await db.Leases.SingleAsync(x => x.Id == s.Lease); var month = new DateOnly(s.End.Year, s.End.Month, 1); statementId = Guid.NewGuid(); db.ChargeStatements.Add(new WarehouseRent.Infrastructure.Finance.ChargeStatement { Id = statementId, LeaseId = s.Lease, RentalAccountId = lease.RentalAccountId, OrganizationId = lease.OrganizationId, FacilityId = lease.FacilityId, PeriodStart = month, DueDate = month, IssuedAt = DateTimeOffset.UtcNow, Total = 0 }); await db.SaveChangesAsync(); }
        var path = $"/api/v1/leases/{s.Lease}/renewals";
        var id = (await Ok(await customer.Send(HttpMethod.Post, path, new { quoteId = s.Quote, expectedLeaseVersion = s.LeaseVersion, expectedQuoteVersion = s.QuoteVersion, reason = "Accept renewal in issued month" }, key: Guid.NewGuid()))).GetProperty("result").GetProperty("resourceId").GetGuid();
        await Ok(await sales.Send(HttpMethod.Post, $"{path}/{id}/apply", new { reason = "Preserve statement and add version" }, key: Guid.NewGuid()));
        var body = new { reason = "Issue newly agreed dates separately", evidenceReference = "Customer accepted renewal", approved = true };
        var first = (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{statementId}/supplement-rent", body, key: Guid.NewGuid()))).GetProperty("result"); Assert.True(first.GetProperty("addedSegments").GetInt32() > 0);
        var repeat = (await Ok(await finance.Send(HttpMethod.Post, $"/api/v1/statements/{statementId}/supplement-rent", body, key: Guid.NewGuid()))).GetProperty("result"); Assert.Equal(0, repeat.GetProperty("addedSegments").GetInt32());
        await using var check = fixture.Db(); Assert.Equal(0, (await check.ChargeStatements.SingleAsync(x => x.Id == statementId)).Total); Assert.Equal(20, (await check.RentalAccounts.SingleAsync(x => x.BookingId == check.Leases.Single(l => l.Id == s.Lease).BookingId)).RequiredDeposit);
        Assert.Equal(0, await check.StatementAdjustments.Where(x => x.ChargeStatementId == statementId).GroupBy(x => x.SourceLeaseVersionLineId).Where(x => x.Count() > 1).CountAsync());
    }
    [Fact]
    public async Task Customer_accepts_sales_applies_once_and_retains_deposit_grants_and_original_version()
    {
        var s = await Setup(40); using var customer = await fixture.Login(); using var sales = await fixture.Login("sales@example.test"); using var otherSales = await fixture.Login("sales@example.test"); using var foreign = await fixture.Login("admin.b@example.test");
        var body = new { quoteId = s.Quote, expectedLeaseVersion = s.LeaseVersion, expectedQuoteVersion = s.QuoteVersion, reason = "Accept added rent with unchanged deposit" };
        var path = $"/api/v1/leases/{s.Lease}/renewals";
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Post, path, body, key: Guid.NewGuid())).StatusCode);
        var acceptance = (await Ok(await customer.Send(HttpMethod.Post, path, body, key: Guid.NewGuid()))).GetProperty("result"); var id = acceptance.GetProperty("resourceId").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(HttpMethod.Post, $"{path}/{id}/apply", new { reason = "Customer cannot apply" }, key: Guid.NewGuid())).StatusCode);
        var key = Guid.NewGuid(); var apply = new { reason = "Apply customer accepted renewal" };
        var outcomes = await Task.WhenAll(sales.Send(HttpMethod.Post, $"{path}/{id}/apply", apply, key: key), otherSales.Send(HttpMethod.Post, $"{path}/{id}/apply", apply, key: Guid.NewGuid()));
        var first = (await Ok(outcomes[0])).GetProperty("result"); var second = (await Ok(outcomes[1])).GetProperty("result"); Assert.Equal(first.GetRawText(), second.GetRawText());
        var replay = (await Ok(await sales.Send(HttpMethod.Post, $"{path}/{id}/apply", apply, key: key))).GetProperty("result"); Assert.Equal(first.GetRawText(), replay.GetRawText());
        await using var db = fixture.Db(); var lease = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == s.Lease);
        Assert.Equal(s.End.AddDays(2), lease.EndDate); Assert.Equal(20m, (await db.RentalAccounts.SingleAsync(x => x.Id == lease.RentalAccountId)).RequiredDeposit);
        Assert.Equal(2, await db.LeaseVersions.CountAsync(x => x.LeaseId == s.Lease)); Assert.Equal("SyntheticOriginal", (await db.LeaseVersions.SingleAsync(x => x.LeaseId == s.Lease && x.VersionNo == 1)).PolicyVersion);
        var originalId=await db.LeaseVersions.Where(x=>x.LeaseId==s.Lease && x.VersionNo==1).Select(x=>x.Id).SingleAsync();var originalLine=await db.LeaseVersionLines.SingleAsync(x=>x.LeaseVersionId==originalId);Assert.Equal(100m,originalLine.Amount);Assert.Equal(s.End,originalLine.EndDate);
        var added=await (from line in db.LeaseVersionLines join version in db.LeaseVersions on line.LeaseVersionId equals version.Id where version.LeaseId==s.Lease && version.VersionNo==2 select line).ToListAsync();Assert.NotEmpty(added);Assert.All(added,x=>Assert.Equal(s.Grant,x.LeaseUnitId));Assert.Equal(await db.Quotes.Where(x=>x.Id==s.Quote).Select(x=>x.RentalSubtotal).SingleAsync(),added.Sum(x=>x.Amount));
        Assert.Equal(s.Grant, (await db.LeaseUnits.SingleAsync(x => x.LeaseId == s.Lease)).Id); Assert.Equal(lease.EndDate, (await db.UnitOccupancies.SingleAsync(x => x.LeaseUnitId == s.Grant)).ScheduledEnd);
        Assert.Equal(lease.EndDate, (await db.Bookings.SingleAsync(x => x.Id == lease.BookingId)).EndDate);
        Assert.Single(await db.SecurityAudits.Where(x => x.AggregateId == s.Lease && x.Action == "LeaseRenewed").ToListAsync());
    }
}
