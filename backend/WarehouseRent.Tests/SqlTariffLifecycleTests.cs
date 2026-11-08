using System.Net;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Tests;
public sealed partial class SqlTariffTests
{
    [Theory]
    [InlineData("0.0001")]
    [InlineData("3100")]
    public async Task Monthly_hold_converts_to_frozen_contract_and_statement_then_renews_without_new_deposit(string monthlyRate)
    {
        var unit=await Unit();using var manager=await fixture.Login("operator@example.test");using var customer=await fixture.Login();using var sales=await fixture.Login("sales@example.test");
        await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(monthlyRate),unit.Version,Guid.NewGuid()));
        var quote=(await Data(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id,Today.ToString("yyyy-MM-dd"),Today.AddDays(2).ToString("yyyy-MM-dd")),key:Guid.NewGuid()))).GetProperty("result");
        var hold=(await Data(await customer.Send(HttpMethod.Post,$"/api/v1/quotes/{quote.GetProperty("resourceId").GetGuid()}/holds",new{expectedQuoteVersion=quote.GetProperty("version").GetString()},key:Guid.NewGuid()))).GetProperty("result");var booking=hold.GetProperty("resourceId").GetGuid();var account=hold.GetProperty("rentalAccountId").GetGuid();
        var deposit=quote.GetProperty("requiredDeposit").GetString()!;
        if(deposit=="0") await Data(await manager.Send(HttpMethod.Post,$"/api/v1/rental-accounts/{account}/verify-zero-deposit",new{reason="Synthetic zero-rounded deposit verification"},(await Data(await manager.Send(HttpMethod.Get,$"/api/v1/rental-accounts/{account}"))).GetProperty("version").GetString(),Guid.NewGuid()));
        else {
            var receipt=(await Data(await manager.Send(HttpMethod.Post,"/api/v1/receipts",new{rentalAccountId=account,amount=deposit,reference="TARIFF-"+Guid.NewGuid().ToString("N"),method="BankTransfer",receivedAt=DateTimeOffset.UtcNow.AddMinutes(-1),evidenceReference="Synthetic test-only money",reason="Record synthetic deposit"},key:Guid.NewGuid()))).GetProperty("result");var receiptId=receipt.GetProperty("resourceId").GetGuid();
            var verified=(await Data(await manager.Send(HttpMethod.Post,$"/api/v1/receipts/{receiptId}/verify",new{reason="Verify synthetic ledger"},receipt.GetProperty("version").GetString(),Guid.NewGuid()))).GetProperty("result");
            await Data(await manager.Send(HttpMethod.Post,$"/api/v1/receipts/{receiptId}/allocations",new{targetType="Deposit",targetId=account,amount=deposit,reason="Allocate synthetic security"},verified.GetProperty("version").GetString(),Guid.NewGuid()));
        }
        var confirmed=(await Data(await sales.Send(HttpMethod.Post,$"/api/v1/bookings/{booking}/confirm",new{reason="Synthetic monthly booking"},hold.GetProperty("version").GetString(),Guid.NewGuid()))).GetProperty("result");
        var leaseId=await ResultId(await sales.Send(HttpMethod.Post,$"/api/v1/bookings/{booking}/lease",new{reason="Synthetic monthly contract"},confirmed.GetProperty("version").GetString(),Guid.NewGuid()));
        var lease=await Data(await customer.Send(HttpMethod.Get,$"/api/v1/leases/{leaseId}"));
        Assert.All(lease.GetProperty("lines").EnumerateArray(),x=>{Assert.Equal("CalendarMonth",x.GetProperty("billingBasis").GetString());Assert.Equal(monthlyRate,x.GetProperty("monthlyRate").GetString());});
        var active=(await Data(await manager.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/handover",new{reason="Synthetic handover",inspectionConfirmed=true},lease.GetProperty("version").GetString(),Guid.NewGuid()))).GetProperty("result");
        var period=new DateOnly(Today.Year,Today.Month,1);
        var statementId=await ResultId(await manager.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/statements",new{periodStart=period,dueDate=period,reason="Frozen monthly statement"},key:Guid.NewGuid()));
        var statement=await Data(await customer.Send(HttpMethod.Get,$"/api/v1/statements/{statementId}"));
        Assert.Equal(statement.GetProperty("lines").EnumerateArray().Sum(x=>decimal.Parse(x.GetProperty("amount").GetString()!,System.Globalization.CultureInfo.InvariantCulture)),decimal.Parse(statement.GetProperty("total").GetString()!,System.Globalization.CultureInfo.InvariantCulture));Assert.All(statement.GetProperty("lines").EnumerateArray(),x=>Assert.Equal("CalendarMonth",x.GetProperty("billingBasis").GetString()));
        var renewalQuote=(await Data(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id,Today.AddDays(2).ToString("yyyy-MM-dd"),Today.AddDays(4).ToString("yyyy-MM-dd")),key:Guid.NewGuid()))).GetProperty("result");
        var acceptance=await ResultId(await customer.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/renewals",new{quoteId=renewalQuote.GetProperty("resourceId").GetGuid(),expectedLeaseVersion=active.GetProperty("version").GetString(),expectedQuoteVersion=renewalQuote.GetProperty("version").GetString(),reason="Accept monthly renewal"},key:Guid.NewGuid()));
        var applied=(await Data(await sales.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/renewals/{acceptance}/apply",new{reason="Apply monthly renewal"},key:Guid.NewGuid()))).GetProperty("result");
        Assert.Equal("0",applied.GetProperty("additionalDeposit").GetString());Assert.Equal(deposit,applied.GetProperty("requiredDeposit").GetString());
        await using var db=fixture.Db();Assert.Equal(2,await db.LeaseVersions.CountAsync(x=>x.LeaseId==leaseId));
        Assert.All(await db.LeaseVersionLines.Where(x=>db.LeaseVersions.Any(v=>v.Id==x.LeaseVersionId && v.LeaseId==leaseId)).ToListAsync(),x=>Assert.Equal("CalendarMonth",x.BillingBasis));
        var frozen=await db.ChargeStatementLines.SingleAsync(x=>x.ChargeStatementId==statementId);frozen.MonthlyRate=100;await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
    [Fact]
    public async Task Nonzero_monthly_renewal_keeps_existing_security_deposit_and_issues_frozen_rent()
    {
        var unit=await Unit();using var manager=await fixture.Login("operator@example.test");using var customer=await fixture.Login();using var sales=await fixture.Login("sales@example.test");
        await Data(await manager.Send(HttpMethod.Post,$"/api/v1/units/{unit.Id}/rates",Monthly(),unit.Version,Guid.NewGuid()));
        var end=Today.AddDays(3);var grant=await fixture.SeedOccupancyOwner(unit.Id,Today,end);Guid leaseId,accountId;string version;
        await using(var seed=fixture.Db()) {var lu=await seed.LeaseUnits.SingleAsync(x=>x.Id==grant);var lease=await seed.Leases.SingleAsync(x=>x.Id==lu.LeaseId);leaseId=lease.Id;accountId=lease.RentalAccountId;var account=await seed.RentalAccounts.SingleAsync(x=>x.Id==accountId);account.RequiredDeposit=777;await seed.SaveChangesAsync();version=Convert.ToBase64String(lease.Version);}
        var q=(await Data(await customer.Send(HttpMethod.Post,"/api/v1/quotes",Quote(unit.Id,end.ToString("yyyy-MM-dd"),end.AddDays(2).ToString("yyyy-MM-dd")),key:Guid.NewGuid()))).GetProperty("result");
        var accepted=await ResultId(await customer.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/renewals",new{quoteId=q.GetProperty("resourceId").GetGuid(),expectedLeaseVersion=version,expectedQuoteVersion=q.GetProperty("version").GetString(),reason="Keep original deposit"},key:Guid.NewGuid()));
        var result=(await Data(await sales.Send(HttpMethod.Post,$"/api/v1/leases/{leaseId}/renewals/{accepted}/apply",new{reason="Apply priced renewal"},key:Guid.NewGuid()))).GetProperty("result");
        Assert.Equal("777",result.GetProperty("requiredDeposit").GetString());Assert.Equal("0",result.GetProperty("additionalDeposit").GetString());
        await using var db=fixture.Db();Assert.Equal(777,(await db.RentalAccounts.SingleAsync(x=>x.Id==accountId)).RequiredDeposit);Assert.False(await db.DepositTransactions.AnyAsync(x=>x.RentalAccountId==accountId));
        var lines=await db.LeaseVersionLines.Where(x=>db.LeaseVersions.Any(v=>v.Id==x.LeaseVersionId && v.LeaseId==leaseId)).ToListAsync();Assert.NotEmpty(lines);Assert.All(lines,x=>{Assert.Equal("CalendarMonth",x.BillingBasis);Assert.True(x.Amount>0);});
    }
}
