using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Infrastructure.Persistence;
using WarehouseRent.Infrastructure.Rentals;
namespace WarehouseRent.Api.Features.LeaseAmendments;
public sealed record LockedRenewal(Lease Lease, Booking Booking, RentalAccount Account, RentalQuote Quote, List<LeaseUnit> Grants, List<QuoteLine> Lines, DateTimeOffset Utc, string[] Reasons);
public sealed class RenewalService(AppDbContext db, UnitLockService locks, CommandExecutor commands)
{
    public async Task<LockedRenewal> ValidateAsync(Guid id, RenewalPreviewInput input, CancellationToken ct)
    {
        var known = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var grants = await db.LeaseUnits.AsNoTracking().Where(x => x.LeaseId == id).OrderBy(x => x.UnitId).ToListAsync(ct); var ids = grants.Select(x => x.UnitId).ToArray(); var grantIds = grants.Select(x => x.Id).ToArray();
        if (ids.Length == 0) throw new SpaceConflict("LEASE_HAS_NO_UNITS");
        var units = await locks.LockAsync(known.FacilityId, ids, ct);
        var booking = await DatabaseDialect.Rows<Booking>(db, $"SELECT * FROM [Bookings] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={known.BookingId}").SingleAsync(ct);
        var account = await DatabaseDialect.Rows<RentalAccount>(db, $"SELECT * FROM [RentalAccounts] WITH (UPDLOCK,HOLDLOCK) WHERE [BookingId]={booking.Id}").SingleAsync(ct);
        var lease = await DatabaseDialect.Rows<Lease>(db, $"SELECT * FROM [Leases] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}").SingleAsync(ct);
        var quote = await db.Quotes.AsNoTracking().SingleAsync(x => x.Id == input.QuoteId, ct);
        if (quote.OrganizationId != lease.OrganizationId || quote.FacilityId != lease.FacilityId) throw new CommandFailure(404,"NOT_FOUND","Quote not found.");
        if (input.ExpectedLeaseVersion != Convert.ToBase64String(lease.Version) || input.ExpectedQuoteVersion != Convert.ToBase64String(quote.Version)) throw new CommandFailure(412,"VERSION_CONFLICT","Read the current lease and quote.");
        var utc = await locks.DatabaseUtcAsync(ct); var today = CatalogPolicy.Today(utc);
        var lines = await db.QuoteLines.AsNoTracking().Where(x => x.QuoteId == quote.Id).OrderBy(x => x.UnitId).ThenBy(x => x.StartDate).ToListAsync(ct);
        if (quote.StartDate != lease.EndDate || quote.EndDate <= lease.EndDate || !ids.Order().SequenceEqual(lines.Select(x => x.UnitId).Distinct().Order())) throw new CommandFailure(422,"RENEWAL_INTERVAL_REQUIRED","Quote the existing units beginning at the current end date.");
        var reasons = new List<string>();
        if (lease.State is not ("Scheduled" or "Active") || lease.ClearedAt != null || booking.State != "Confirmed" || account.State != "Open") reasons.Add("LEASE_NOT_RENEWABLE");
        if (quote.ExpiresAt <= utc || quote.StartDate < today) reasons.Add("REQUOTE_REQUIRED");
        if (units.Any(x => !x.Published || x.RetiredAt != null) || !await db.Facilities.AnyAsync(x => x.Id == lease.FacilityId && x.Published,ct)) reasons.Add("UNIT_INELIGIBLE");
        var rates = await db.RateVersions.AsNoTracking().Where(x => ids.Contains(x.UnitId) && x.SalesValidFrom <= today && (x.SalesValidTo == null || today < x.SalesValidTo)).ToListAsync(ct);
        if (!Quotes.QuoteEndpoints.SupportedPolicy(quote.PolicyVersion) || rates.Count != units.Count || units.Any(u => lines.Any(l => l.UnitId == u.Id && l.AreaM2 != u.AreaM2) || rates.Count(r => r.UnitId == u.Id && lines.Any(l => l.UnitId == u.Id && l.RateVersionId == r.Id)) != 1)) reasons.Add("REQUOTE_REQUIRED");
        if (await locks.ConflictsAsync(ids,quote.StartDate,quote.EndDate,utc,ct)) reasons.Add("SPACE_CONFLICT");
        if (await db.UnitOccupancies.AnyAsync(x => ids.Contains(x.UnitId) && x.ClearedAt == null && !grantIds.Contains(x.LeaseUnitId),ct)) reasons.Add("OTHER_PHYSICAL_OCCUPANCY");
        // WP15 finance issues added version segments as referenced supplements to an existing month.
        // Existing statement totals remain immutable and unbilled segments block final closure.
        return new(lease,booking,account,quote,grants,lines,utc,reasons.Distinct().ToArray());
    }
    public async Task<object> AcceptAsync(Guid id, RenewalConfirmation input, string actor, HttpContext context,CancellationToken ct)
    {
        var v = await ValidateAsync(id,new(input.QuoteId,input.ExpectedQuoteVersion,input.ExpectedLeaseVersion),ct);
        if (v.Reasons.Length > 0) throw new SpaceConflict(v.Reasons[0]);
        var previous = await db.RenewalAcceptances.SingleOrDefaultAsync(x => x.LeaseId == id && x.QuoteId == input.QuoteId,ct);
        if(previous is not null) return new { resourceId = previous.Id };
        var acceptance = new RenewalAcceptance { Id=Guid.NewGuid(),LeaseId=id,QuoteId=input.QuoteId,ExpectedLeaseVersion=input.ExpectedLeaseVersion,ExpectedQuoteVersion=input.ExpectedQuoteVersion,AcceptedBy=actor,AcceptedAt=v.Utc };
        db.RenewalAcceptances.Add(acceptance); commands.Audit(context,"RenewalAccepted",id,input.Reason,v.Lease.OrganizationId); await db.SaveChangesAsync(ct); return new { resourceId=acceptance.Id };
    }
    public async Task<object> ApplyAsync(Guid id,Guid acceptanceId,string reason,HttpContext context,CancellationToken ct)
    {
        var accepted = await db.RenewalAcceptances.AsNoTracking().SingleAsync(x => x.Id == acceptanceId && x.LeaseId == id,ct);
        // Replays lock the same parents before reading the frozen result; no current-version assertion on an already applied renewal.
        var known = await db.Leases.AsNoTracking().SingleAsync(x => x.Id == id,ct);
        var unitIds = await db.LeaseUnits.Where(x => x.LeaseId == id).Select(x => x.UnitId).ToArrayAsync(ct); await locks.LockAsync(known.FacilityId,unitIds,ct);
        if (accepted.AppliedResultJson is not null) return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(accepted.AppliedResultJson);
        // Refresh after waiting for a concurrent application, which may have committed while acquiring parents.
        accepted = await db.RenewalAcceptances.AsNoTracking().SingleAsync(x => x.Id == acceptanceId,ct);
        if (accepted.AppliedResultJson is not null) return System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(accepted.AppliedResultJson);
        var v = await ValidateAsync(id,new(accepted.QuoteId,accepted.ExpectedQuoteVersion,accepted.ExpectedLeaseVersion),ct); if(v.Reasons.Length > 0) throw new SpaceConflict(v.Reasons[0]);
        var acceptance = await DatabaseDialect.Rows<RenewalAcceptance>(db, $"SELECT * FROM [RenewalAcceptances] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={acceptanceId}").SingleAsync(ct);
        var version = new LeaseVersion { Id=Guid.NewGuid(),LeaseId=id,VersionNo=(await db.LeaseVersions.Where(x=>x.LeaseId==id).MaxAsync(x=>(int?)x.VersionNo,ct) ?? 0)+1,PolicyVersion=v.Quote.PolicyVersion };
        db.LeaseVersions.Add(version);
        foreach(var line in v.Lines) db.LeaseVersionLines.Add(new LeaseVersionLine { Id=Guid.NewGuid(),LeaseVersionId=version.Id,LeaseUnitId=v.Grants.Single(x=>x.UnitId==line.UnitId).Id,SourceQuoteLineId=line.Id,StartDate=line.StartDate,EndDate=line.EndDate,Days=line.Days,AreaM2=line.AreaM2,DailyRate=line.DailyRate,BillingBasis=line.BillingBasis,MonthlyRate=line.MonthlyRate,CalendarDays=line.CalendarDays,Amount=line.Amount });
        var bookingLines = await db.BookingLines.Where(x=>x.BookingId==v.Booking.Id).ToListAsync(ct); var lineIds=bookingLines.Select(x=>x.Id).ToArray();
        foreach(var line in bookingLines) line.EndDate=v.Quote.EndDate;
        foreach(var allocation in await db.SpaceAllocations.Where(x=>x.BookingLineId!=null && lineIds.Contains(x.BookingLineId.Value) && x.State=="Confirmed").ToListAsync(ct)) allocation.EndDate=v.Quote.EndDate;
        var grants=v.Grants.Select(x=>x.Id).ToArray(); foreach(var occupancy in await db.UnitOccupancies.Where(x=>grants.Contains(x.LeaseUnitId) && x.ClearedAt==null).ToListAsync(ct)) occupancy.ScheduledEnd=v.Quote.EndDate;
        v.Lease.EndDate=v.Quote.EndDate; v.Booking.EndDate=v.Quote.EndDate;
        commands.Audit(context,"LeaseRenewed",id,reason,v.Lease.OrganizationId); await db.SaveChangesAsync(ct);
        var result=new { resourceId=id,acceptanceId,leaseVersionId=version.Id,versionNo=version.VersionNo,version=Convert.ToBase64String(v.Lease.Version),endDate=v.Lease.EndDate,requiredDeposit=CatalogPolicy.Money(v.Account.RequiredDeposit),additionalDeposit="0" };
        acceptance.AppliedResultJson=System.Text.Json.JsonSerializer.Serialize(result); await db.SaveChangesAsync(ct); return result;
    }
}
