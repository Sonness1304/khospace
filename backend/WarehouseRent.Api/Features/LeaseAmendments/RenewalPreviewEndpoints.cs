using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Facilities;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Infrastructure.Persistence;
namespace WarehouseRent.Api.Features.LeaseAmendments;
public sealed record RenewalPreviewInput(Guid QuoteId,string ExpectedQuoteVersion,string ExpectedLeaseVersion);
public sealed record RenewalConfirmation(Guid QuoteId,string ExpectedQuoteVersion,string ExpectedLeaseVersion,string Reason);
public sealed record RenewalApply(string Reason);
public static class RenewalPreviewEndpoints
{
    public static void MapRenewalPreview(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/leases/{id:guid}").RequireAuthorization();
        api.MapGet("/renewals/{acceptanceId:guid}",async(Guid id,Guid acceptanceId,AppDbContext db,CurrentActor actor,CancellationToken ct)=> {
            var lease=await db.Leases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
            if(lease is null || await actor.MembershipAsync(lease.OrganizationId,ct) is null && !await actor.HasFacilityAsync(lease.FacilityId,null,ct))return OrganizationEndpoints.Missing();
            var item=await db.RenewalAcceptances.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==acceptanceId && x.LeaseId==id,ct);if(item is null)return OrganizationEndpoints.Missing();
            return Results.Ok(new { item.Id,item.LeaseId,item.QuoteId,lease.OrganizationId,lease.FacilityId,item.AcceptedAt,item.AcceptedBy,applied=item.AppliedResultJson!=null,result=item.AppliedResultJson==null?(object?)null:System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(item.AppliedResultJson) });
        });
        api.MapPost("/renewal-preview",async(Guid id,RenewalPreviewInput input,AppDbContext db,CurrentActor actor,RenewalService service,CancellationToken ct)=> {
            var denied=await Authorize(id,input.QuoteId,false,false,db,actor,ct); if(denied is not null)return denied;
            try { await using var tx=await db.Database.BeginTransactionAsync(ct); var v=await service.ValidateAsync(id,input,ct);
                var result=new { leaseId=id,leaseVersion=Convert.ToBase64String(v.Lease.Version),quoteId=v.Quote.Id,quoteVersion=Convert.ToBase64String(v.Quote.Version),currentEnd=v.Lease.EndDate,proposedEnd=v.Quote.EndDate,addedRentalSubtotal=CatalogPolicy.Money(v.Quote.RentalSubtotal),currency="VND",canApply=v.Reasons.Length==0,reasons=v.Reasons,capacityReserved=false,applied=false,depositPolicy="RetainExistingDeposit",requiredDeposit=CatalogPolicy.Money(v.Account.RequiredDeposit),additionalDeposit="0",observedAt=v.Utc,grants=v.Grants.Select(x=>new { leaseUnitId=x.Id,x.UnitId }),addedLines=v.Lines.Select(x=>new { x.UnitId,x.StartDate,x.EndDate,x.Days,amount=CatalogPolicy.Money(x.Amount) }) };
                await tx.CommitAsync(ct);return Results.Ok(result);
            }catch(CommandFailure e){return ApiFailure.Result(e.Status,e.Code,e.SafeMessage);}catch(SpaceConflict e){return ApiFailure.Result(409,e.Code,"Renewal unavailable.");}
        });
        api.MapPost("/renewals",async(Guid id,RenewalConfirmation input,AppDbContext db,CurrentActor actor,RenewalService service,CommandExecutor commands,HttpContext context,CancellationToken ct)=> {
            var denied=await Authorize(id,input.QuoteId,true,false,db,actor,ct);if(denied is not null)return denied;
            if(!CatalogPolicy.Text(input.Reason,500))return ApiFailure.Result(422,"REASON_REQUIRED","Supply a reason.");
            var lease=await db.Leases.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            return await commands.ExecuteAsync(context,lease.OrganizationId,"AcceptRenewal",new { id,input },t=>service.AcceptAsync(id,input,actor.UserId,context,t),ct,organizationId:lease.OrganizationId,facilityId:lease.FacilityId,facilityRole:"Sales");
        });
        api.MapPost("/renewals/{acceptanceId:guid}/apply",async(Guid id,Guid acceptanceId,RenewalApply input,AppDbContext db,CurrentActor actor,RenewalService service,CommandExecutor commands,HttpContext context,CancellationToken ct)=> {
            var denied=await Authorize(id,null,false,true,db,actor,ct);if(denied is not null)return denied;
            if(!await db.RenewalAcceptances.AnyAsync(x=>x.Id==acceptanceId && x.LeaseId==id,ct))return OrganizationEndpoints.Missing();
            if(!CatalogPolicy.Text(input.Reason,500))return ApiFailure.Result(422,"REASON_REQUIRED","Supply a reason.");
            var lease=await db.Leases.AsNoTracking().SingleAsync(x=>x.Id==id,ct);
            return await commands.ExecuteAsync(context,lease.OrganizationId,"ApplyRenewal",new { id,acceptanceId,input },t=>service.ApplyAsync(id,acceptanceId,input.Reason,context,t),ct,organizationId:lease.OrganizationId,facilityId:lease.FacilityId,facilityRole:"Sales");
        });
    }
    private static async Task<IResult?> Authorize(Guid id,Guid? quoteId,bool customerOnly,bool salesOnly,AppDbContext db,CurrentActor actor,CancellationToken ct)
    {
        var lease=await db.Leases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);if(lease is null)return OrganizationEndpoints.Missing();
        var member=await actor.MembershipAsync(lease.OrganizationId,ct);var staff=await actor.HasFacilityAsync(lease.FacilityId,null,ct);
        if(member is null && !staff)return OrganizationEndpoints.Missing();
        var sales=await actor.HasFacilityAsync(lease.FacilityId,"Sales",ct);
        if(customerOnly ? member?.Role!="CustomerAdmin" : salesOnly ? !sales : member?.Role!="CustomerAdmin" && !sales)return OrganizationEndpoints.Forbidden();
        if(quoteId.HasValue && !await db.Quotes.AnyAsync(x=>x.Id==quoteId.Value && x.OrganizationId==lease.OrganizationId && x.FacilityId==lease.FacilityId,ct))return OrganizationEndpoints.Missing();
        return null;
    }
}
