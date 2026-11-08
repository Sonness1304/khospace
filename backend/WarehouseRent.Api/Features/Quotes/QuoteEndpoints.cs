using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;

namespace WarehouseRent.Api.Features.Quotes;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteInput(Guid FacilityId, Guid OrganizationId, Guid[] UnitIds, DateOnly StartDate, DateOnly EndDate);
public static class QuoteEndpoints
{
    public const string PolicyVersion = "P0-DAILY-M2-V1";
    public const string MonthlyPolicyVersion = "P2-CALENDAR-M2-V1";
    public static bool SupportedPolicy(string value) => value is PolicyVersion or MonthlyPolicyVersion;
    public static void MapQuotes(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/quotes").RequireAuthorization();
        api.MapPost("/", Create);
        api.MapGet("/{id:guid}", async (Guid id, CurrentActor actor, AppDbContext db, CancellationToken ct) =>
        {
            var quote = await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (quote is null || (await actor.MembershipAsync(quote.OrganizationId, ct) is null && !await actor.HasFacilityAsync(quote.FacilityId, "Sales", ct))) return OrganizationEndpoints.Missing();
            var lines = await db.QuoteLines.AsNoTracking().Where(x => x.QuoteId == id).OrderBy(x => x.UnitId).ThenBy(x => x.StartDate).ToListAsync(ct);
            return Results.Ok(new { quote.Id, quote.OrganizationId, quote.FacilityId, quote.CreatedAt, quote.ExpiresAt, quote.StartDate, quote.EndDate, quote.PolicyVersion,
                version = Convert.ToBase64String(quote.Version), rentalSubtotal = CatalogPolicy.Money(quote.RentalSubtotal), requiredDeposit = CatalogPolicy.Money(quote.RequiredDeposit),
                currency = "VND", reservation = false, lines = lines.Select(x => new { x.Id, x.UnitId, x.UnitCode, x.RateVersionId, x.BillingBasis, monthlyRate = x.MonthlyRate.HasValue ? CatalogPolicy.Money(x.MonthlyRate.Value) : null, x.CalendarDays, x.StartDate, x.EndDate, x.Days,
                    areaM2 = CatalogPolicy.Money(x.AreaM2), dailyRate = CatalogPolicy.Money(x.DailyRate), amount = CatalogPolicy.Money(x.Amount) }) });
        });
    }
    private static async Task<IResult> Create(QuoteInput input, CurrentActor actor, AppDbContext db, UnitLockService locks, CommandExecutor commands, HttpContext context, CancellationToken ct)
    {
        var membership = await actor.MembershipAsync(input.OrganizationId, ct);
        var sales = await actor.HasFacilityAsync(input.FacilityId, "Sales", ct);
        if (membership is null && !sales) return OrganizationEndpoints.Missing();
        if (membership?.Role != "CustomerAdmin" && !sales) return OrganizationEndpoints.Forbidden();
        if (input.UnitIds is not { Length: > 0 and <= 20 } || input.UnitIds.Distinct().Count() != input.UnitIds.Length
            || !CatalogPolicy.Dates(input.StartDate, input.EndDate, DateOnly.MinValue)) return ApiFailure.Result(422, "INVALID_QUOTE", "Select distinct units and a1–365-day rental interval.");
        return await commands.ExecuteAsync(context, input.OrganizationId, "CreateQuote", input, async token =>
        {
            var units = await locks.LockAsync(input.FacilityId, input.UnitIds, token);
            var utc = await locks.DatabaseUtcAsync(token); var today = CatalogPolicy.Today(utc);
            if (!CatalogPolicy.Dates(input.StartDate, input.EndDate, today)) throw new CommandFailure(422, "INVALID_DATES", "Refresh the local rental dates.");
            if (!await db.CustomerOrganizations.AnyAsync(x => x.Id == input.OrganizationId && x.Active, token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The customer organization is unavailable.");
            if (!await db.Facilities.AnyAsync(x => x.Id == input.FacilityId && x.Published, token) || units.Any(x => !x.Published || x.RetiredAt != null)) throw new SpaceConflict("UNIT_INELIGIBLE");
            var rates = await db.RateVersions.AsNoTracking().Where(x => input.UnitIds.Contains(x.UnitId) && x.SalesValidFrom <= today && (x.SalesValidTo == null || today < x.SalesValidTo)).ToListAsync(token);
            if (rates.Count != units.Count || rates.Select(x => x.UnitId).Distinct().Count() != units.Count) throw new SpaceConflict("RATE_UNAVAILABLE");
            QuotePrice price;
            try { price = PriceCalculator.Calculate(units.Select(u => new UnitPrice(u.Id, u.AreaM2, rates.Single(r => r.UnitId == u.Id).DailyRate, rates.Single(r => r.UnitId == u.Id).BillingBasis, rates.Single(r => r.UnitId == u.Id).MonthlyRate)).ToArray(), input.StartDate, input.EndDate); }
            catch (ArgumentException) { throw new CommandFailure(422, "PRICE_OUT_OF_RANGE", "The selected prices exceed supported quote precision."); }
            var quote = new RentalQuote { Id = Guid.NewGuid(), OrganizationId = input.OrganizationId, FacilityId = input.FacilityId, CreatedBy = actor.UserId,
                CreatedAt = utc, ExpiresAt = utc.AddHours(24), StartDate = input.StartDate, EndDate = input.EndDate, RentalSubtotal = price.RentalSubtotal, RequiredDeposit = price.RequiredDeposit, PolicyVersion = rates.Any(r => r.BillingBasis == "CalendarMonth") ? MonthlyPolicyVersion : PolicyVersion };
            db.Quotes.Add(quote);
            foreach (var line in price.Lines)
            {
                var unit = units.Single(x => x.Id == line.UnitId);
                db.QuoteLines.Add(new QuoteLine { Id = Guid.NewGuid(), QuoteId = quote.Id, UnitId = unit.Id, UnitCode = unit.Code, UnitVersion = unit.Version.ToArray(), RateVersionId = rates.Single(x => x.UnitId == unit.Id).Id,
                    StartDate = line.StartDate, EndDate = line.EndDate, Days = line.Days, AreaM2 = line.AreaM2, DailyRate = line.DailyRate, BillingBasis = line.BillingBasis, MonthlyRate = line.MonthlyRate, CalendarDays = line.CalendarDays, Amount = line.Amount });
            }
            commands.Audit(context, "QuoteCreated", quote.Id, "Explicit monthly quote; no capacity reserved", input.OrganizationId);
            await db.SaveChangesAsync(token);
            return new { resourceId = quote.Id, version = Convert.ToBase64String(quote.Version), rentalSubtotal = CatalogPolicy.Money(quote.RentalSubtotal), requiredDeposit = CatalogPolicy.Money(quote.RequiredDeposit), reservation = false };
        }, ct, organizationId: input.OrganizationId, facilityId: input.FacilityId, facilityRole: "Sales");
    }
}
