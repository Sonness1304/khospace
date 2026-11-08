using System.Net;
using System.Text.Json;
using WarehouseRent.Infrastructure.Identity;
namespace WarehouseRent.Tests;

[Trait("Category", "Sql")]
public sealed class P0AcceptanceTests(IdentitySqlFixture fixture) : IClassFixture<IdentitySqlFixture>
{
    private static async Task<(Guid Id, string Version, JsonElement Data)> Result(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = doc.RootElement.GetProperty("result");
        return (result.GetProperty("resourceId").GetGuid(), result.TryGetProperty("version", out var v) ? v.GetString()! : "", result.Clone());
    }
    [Fact]
    public async Task Real_roles_complete_rental_stock_and_finance_with_scope_isolation()
    {
        using var customer = await fixture.Login(); using var finance = await fixture.Login("finance@example.test");
        using var sales = await fixture.Login("sales@example.test"); using var manager = await fixture.Login("manager@example.test");
        using var clerk = await fixture.Login("clerk@example.test"); using var foreign = await fixture.Login("admin.b@example.test");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)); var end = today.AddDays(2);
        var available = await customer.Send(HttpMethod.Get, $"/api/v1/availability?facilityId={DemoIdentitySeed.FacilityA}&startDate={today:yyyy-MM-dd}&endDate={end:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, available.StatusCode); Assert.Contains(SqlFacilitiesTests.UnitA.ToString(), await available.Content.ReadAsStringAsync());
        var quote = await Result(await customer.Send(HttpMethod.Post, "/api/v1/quotes", new { organizationId = DemoIdentitySeed.OrganizationA, facilityId = DemoIdentitySeed.FacilityA, unitIds = new[] { SqlFacilitiesTests.UnitA }, startDate = today, endDate = end }, key: Guid.NewGuid()));
        var hold = await Result(await customer.Send(HttpMethod.Post, $"/api/v1/quotes/{quote.Id}/holds", new { expectedQuoteVersion = quote.Version }, key: Guid.NewGuid()));
        var account = hold.Data.GetProperty("rentalAccountId").GetGuid();
        using var booking = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/bookings/{hold.Id}")).Content.ReadAsStringAsync());
        var deposit = booking.RootElement.GetProperty("requiredDeposit").GetString()!;
        async Task<(Guid Id, string Version, JsonElement Data)> Receipt(string amount) => await Result(await finance.Send(HttpMethod.Post, "/api/v1/receipts", new { rentalAccountId = account, amount, reference = "ACCEPT-" + Guid.NewGuid().ToString("N"), method = "BankTransfer", receivedAt = DateTimeOffset.UtcNow.AddMinutes(-1), evidenceReference = "Synthetic funds", reason = "Acceptance funds" }, key: Guid.NewGuid()));
        var receipt = await Receipt(deposit);
        var verified = await Result(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/verify", new { reason = "Verified synthetic deposit" }, receipt.Version, Guid.NewGuid()));
        await Result(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{receipt.Id}/allocations", new { targetType = "Deposit", targetId = account, amount = deposit, reason = "Allocate refundable deposit" }, verified.Version, Guid.NewGuid()));
        var confirm = await Result(await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/confirm", new { reason = "Verified deposit" }, hold.Version, Guid.NewGuid()));
        var lease = await Result(await sales.Send(HttpMethod.Post, $"/api/v1/bookings/{hold.Id}/lease", new { reason = "Create rental" }, confirm.Version, Guid.NewGuid()));
        await Result(await manager.Send(HttpMethod.Post, $"/api/v1/leases/{lease.Id}/handover", new { reason = "Inspected unit", inspectionConfirmed = true }, lease.Version, Guid.NewGuid()));
        using var detail = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/leases/{lease.Id}")).Content.ReadAsStringAsync());
        var grant = detail.RootElement.GetProperty("units")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/leases/{lease.Id}")).StatusCode);
        var item = await Result(await customer.Send(HttpMethod.Post, "/api/v1/items", new { organizationId = DemoIdentitySeed.OrganizationA, sku = "ACCEPT-" + Guid.NewGuid().ToString("N"), description = "Synthetic cartons", baseUom = "box", reason = "Create test item" }, key: Guid.NewGuid()));
        async Task Move(string type, string quantity)
        {
            var draft = await Result(await customer.Send(HttpMethod.Post, "/api/v1/stock-requests", new { organizationId = DemoIdentitySeed.OrganizationA, leaseId = lease.Id, leaseUnitId = grant, type, lines = new[] { new { itemId = item.Id, quantity } }, reason = "Customer instruction" }, key: Guid.NewGuid()));
            var submitted = await Result(await customer.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/submit", new { reason = "Submit instruction" }, draft.Version, Guid.NewGuid()));
            var approved = await Result(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/approve", new { reason = "Clerk approval" }, submitted.Version, Guid.NewGuid()));
            using var request = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock-requests/{draft.Id}")).Content.ReadAsStringAsync());
            var line = request.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid();
            await Result(await clerk.Send(HttpMethod.Post, $"/api/v1/stock-requests/{draft.Id}/post", new { reason = "Observed exact quantity", lines = new[] { new { requestLineId = line, quantity } } }, approved.Version, Guid.NewGuid()));
        }
        await Move("Receipt", "10"); await Move("Issue", "7");
        using var balance = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock/balances?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={grant}")).Content.ReadAsStringAsync());
        Assert.Equal("3", balance.RootElement.GetProperty("items")[0].GetProperty("quantity").GetString());
        using var history = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/stock/ledger?organizationId={DemoIdentitySeed.OrganizationA}&leaseUnitId={grant}")).Content.ReadAsStringAsync());
        Assert.Equal(2, history.RootElement.GetProperty("items").GetArrayLength());
        var month = new DateOnly(today.Year, today.Month, 1);
        var statement = await Result(await finance.Send(HttpMethod.Post, $"/api/v1/leases/{lease.Id}/statements", new { periodStart = month, dueDate = month, reason = "Issue agreed month" }, key: Guid.NewGuid()));
        var rent = await Receipt("1"); var checkedRent = await Result(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{rent.Id}/verify", new { reason = "Verified separate rent" }, rent.Version, Guid.NewGuid()));
        await Result(await finance.Send(HttpMethod.Post, $"/api/v1/receipts/{rent.Id}/allocations", new { targetType = "Charge", targetId = statement.Id, amount = "1", reason = "Partial rent" }, checkedRent.Version, Guid.NewGuid()));
        using var bill = JsonDocument.Parse(await (await customer.Send(HttpMethod.Get, $"/api/v1/statements/{statement.Id}")).Content.ReadAsStringAsync());
        Assert.Equal("PartPaid", bill.RootElement.GetProperty("paymentState").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Send(HttpMethod.Get, $"/api/v1/statements/{statement.Id}")).StatusCode);
    }
}
