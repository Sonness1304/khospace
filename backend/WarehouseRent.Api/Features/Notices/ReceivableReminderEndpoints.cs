using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Common;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Api.Features.Organizations;
using WarehouseRent.Api.Features.Reports;
using WarehouseRent.Infrastructure.Jobs;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Notices;

public static class ReceivableReminderEndpoints
{
    public sealed record ReminderInput(DateOnly StartDate, DateOnly EndDate, int Page);
    public static void MapReceivableReminders(this WebApplication app)
    {
        app.MapPost("/api/v1/organizations/{organizationId:guid}/facilities/{facilityId:guid}/receivable-reminders",
            async (Guid organizationId, Guid facilityId, ReminderInput input, AppDbContext db, CurrentActor actor,
                CommandExecutor commands, HttpContext context, CancellationToken ct) =>
        {
            async Task<bool> Allowed(CancellationToken token) =>
                await db.Users.AnyAsync(x => x.Id == actor.UserId && x.Active, token)
                && await db.CustomerOrganizations.AnyAsync(x => x.Id == organizationId && x.Active, token)
                && await db.Facilities.AnyAsync(x => x.Id == facilityId, token)
                && await actor.HasFacilityAsync(facilityId, "Finance", token);
            if (!await Allowed(ct)) return OrganizationEndpoints.Missing();
            if (input.StartDate == DateOnly.MinValue || input.EndDate <= input.StartDate || input.EndDate.DayNumber - input.StartDate.DayNumber > 366 || input.Page is < 0 or > 99)
                return ApiFailure.Result(422, "RECEIVABLE_RANGE_INVALID", "Choose a due-date interval of at most 366 days and source page 0..99.");
            return await commands.ExecuteAsync(context, organizationId, "ReceivableReminders", new { organizationId, facilityId, input }, async token =>
            {
                ReceivableSnapshot? snapshot;
                try { snapshot = await ReceivablesReport.ReadAsync(db, organizationId, facilityId, input.StartDate, input.EndDate, token); }
                catch (ReceivableIntegrityException) { throw new CommandFailure(409, "ACCOUNTING_RECONCILIATION_REQUIRED", "Resolve the linked financial records before creating reminders."); }
                if (snapshot is null) throw new CommandFailure(422, "RECEIVABLE_RANGE_INVALID", "Narrow the interval to at most 5000 matched statements.");
                var rows = snapshot.Rows.Skip(input.Page * 50).Take(50).ToArray(); var eligible = 0; var created = 0;
                foreach (var row in rows.Where(x => x.DaysOverdue > 0 && x.OutstandingVnd != "0"))
                {
                    eligible++;
                    var date = snapshot.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    var key = $"receivable/{row.StatementId}/{date}";
                    if (await db.NoticeOutbox.AnyAsync(x => x.BusinessKey == key, token)) continue;
                    var message = FormattableString.Invariant($"Statement {row.StatementId} was {row.DaysOverdue} days overdue on {date}; due {row.DueDate:yyyy-MM-dd}; outstanding VND {row.OutstandingVnd}. Observed {snapshot.ObservedAt:O}. Check the current statement before paying.");
                    var outbox = new NoticeOutbox { Id = Guid.NewGuid(), OrganizationId = organizationId, FacilityId = facilityId, AggregateId = row.StatementId,
                        Kind = "PaymentOverdue", BusinessKey = key, Message = message, CreatedAt = snapshot.ObservedAt, DeliveredAt = snapshot.ObservedAt };
                    db.NoticeOutbox.Add(outbox);
                    db.InAppNotices.Add(new InAppNotice { Id = Guid.NewGuid(), OutboxId = outbox.Id, OrganizationId = organizationId, FacilityId = facilityId,
                        Kind = outbox.Kind, AggregateId = row.StatementId, Message = message, CreatedAt = snapshot.ObservedAt }); created++;
                }
                commands.Audit(context, "ReceivableRemindersCreated", organizationId, $"sourcePage={input.Page}; scanned={rows.Length}; eligible={eligible}; created={created}", organizationId, facilityId);
                return new { snapshot.ObservedAt, snapshot.AsOfDate, snapshot.TimeZone, scanned = rows.Length, eligible, created, duplicates = eligible - created,
                    input.Page, pageSize = 50, nextPage = (input.Page + 1) * 50 < snapshot.Rows.Count ? (int?)(input.Page + 1) : null };
            }, ct, organizationId: organizationId, facilityId: facilityId, facilityRole: "Finance", serializableAfterReceipt: true, financeOnlyReadback: true,
                authorize: async token =>
                {
                    // Serialize business reads and daily notice insertion before taking SQL range locks.
                    await DatabaseDialect.MutexAsync(db, "WarehouseRent.Notice." + facilityId, token, errorNumber: 51013, message: "Notice job busy.");
                    if (!await Allowed(token)) throw new CommandFailure(404, "RESOURCE_NOT_FOUND", "The requested resource is unavailable.");
                });
        }).RequireAuthorization();
    }
}
