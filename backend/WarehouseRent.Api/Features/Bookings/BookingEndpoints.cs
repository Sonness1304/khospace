using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.Bookings;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record HoldInput(string ExpectedQuoteVersion);
public sealed record BookingAction(string Reason);
public static class BookingEndpoints
{
    public static void MapBookings(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/quotes/{id:guid}/holds", async (Guid id, HoldInput input, CurrentActor actor, AppDbContext db, BookingService bookings, CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (quote is null) return OrganizationEndpoints.Missing();
            var member = await actor.MembershipAsync(quote.OrganizationId, ct); var sales = await actor.HasFacilityAsync(quote.FacilityId, "Sales", ct);
            if (member is null && !sales) return OrganizationEndpoints.Missing();
            if (member?.Role != "CustomerAdmin" && !sales) return OrganizationEndpoints.Forbidden();
            if (!CatalogPolicy.Text(input.ExpectedQuoteVersion, 100)) return ApiFailure.Result(422, "QUOTE_VERSION_REQUIRED", "Read and accept a quote before requesting a hold.");
            return await commands.ExecuteAsync(context, quote.OrganizationId, "CreateHold", new { id, input }, token => bookings.HoldAsync(id, input.ExpectedQuoteVersion, context, token), ct,
                organizationId: quote.OrganizationId, facilityId: quote.FacilityId, facilityRole: "Sales");
        });
        api.MapGet("/bookings/{id:guid}", async (Guid id, CurrentActor actor, AppDbContext db, IDepositVerification deposits, CancellationToken ct) =>
        {
            var item = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null || (await actor.MembershipAsync(item.OrganizationId, ct) is null && !await actor.HasFacilityAsync(item.FacilityId, null, ct))) return OrganizationEndpoints.Missing();
            var account = await db.RentalAccounts.AsNoTracking().SingleAsync(x => x.BookingId == id, ct);
            var lines = await db.BookingLines.AsNoTracking().Where(x => x.BookingId == id).OrderBy(x => x.UnitId).Select(x => new { x.Id, x.UnitId, x.StartDate, x.EndDate }).ToListAsync(ct);
            var utc = await DatabaseDialect.UtcNowAsync(db, ct);
            var deposit = await deposits.ReadAsync(account, ct);
            return Results.Ok(new { item.Id, item.QuoteId, item.OrganizationId, item.FacilityId, item.StartDate, item.EndDate, item.HeldAt, item.ExpiresAt, item.ConfirmedAt,
                state = BookingService.EffectiveState(item, utc), observedAt = utc, version = Convert.ToBase64String(item.Version), rentalAccountId = account.Id,
                requiredDeposit = CatalogPolicy.Money(account.RequiredDeposit), refundNeeded = account.RefundNeeded || deposit.HasFunds && BookingService.EffectiveState(item, utc) == "Expired", depositVerification = deposit.Verified ? "Verified" : "Pending", depositHeld = CatalogPolicy.Money(deposit.Amount), accountVersion = Convert.ToBase64String(account.Version), lines });
        });
        api.MapPost("/bookings/{id:guid}/confirm", (Guid id, BookingAction input, CurrentActor actor, AppDbContext db, BookingService bookings, CommandExecutor commands, HttpContext context, CancellationToken ct)
            => Action(id, input, true, actor, db, bookings, commands, context, ct));
        api.MapPost("/bookings/{id:guid}/cancel", (Guid id, BookingAction input, CurrentActor actor, AppDbContext db, BookingService bookings, CommandExecutor commands, HttpContext context, CancellationToken ct)
            => Action(id, input, false, actor, db, bookings, commands, context, ct));
    }
    private static async Task<IResult> Action(Guid id, BookingAction input, bool confirm, CurrentActor actor, AppDbContext db, BookingService bookings, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        var item = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return OrganizationEndpoints.Missing();
        var member = await actor.MembershipAsync(item.OrganizationId, ct); var sales = await actor.HasFacilityAsync(item.FacilityId, "Sales", ct);
        if (member is null && !sales) return OrganizationEndpoints.Missing();
        if (confirm ? !sales : member?.Role != "CustomerAdmin" && !sales) return OrganizationEndpoints.Forbidden();
        if (!CatalogPolicy.Text(input.Reason, 500)) return ApiFailure.Result(422, "REASON_REQUIRED", "Supply a reason of at most500 characters.");
        return await commands.ExecuteAsync(context, item.OrganizationId, confirm ? "ConfirmBooking" : "CancelRental", new { id, input },
            token => confirm ? bookings.ConfirmAsync(id, input.Reason, context, token) : bookings.CancelAsync(id, input.Reason, context, token), ct,
            organizationId: item.OrganizationId, facilityId: item.FacilityId, facilityRole: "Sales");
    }
}
