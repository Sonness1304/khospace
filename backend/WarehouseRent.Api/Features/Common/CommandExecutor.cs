using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using WarehouseRent.Api.Features.Identity;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Identity;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Api.Features.Common;

public sealed class CommandExecutor(AppDbContext db, CurrentActor actor)
{
    private Guid? auditFacility;
    public async Task<IResult> ExecuteAsync(HttpContext context, Guid scope, string operation, object input,
        Func<CancellationToken, Task<object>> write, CancellationToken ct, Guid? organizationId = null, Guid? facilityId = null, string facilityRole = "FacilityManager", Action? onCommitted = null,
        bool serializableAfterReceipt = false, Func<CancellationToken, Task>? authorize = null, bool financeOnlyReadback = false)
    {
        if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"], out var key) || key == Guid.Empty)
            return ApiFailure.Result(422, "COMMAND_KEY_REQUIRED", "Supply a UUID Idempotency-Key.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { operation, input, version = context.Request.Headers.IfMatch.ToString() }))));
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                serializableAfterReceipt && db.Database.IsNpgsql() ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, ct);
            // A transaction-owned per-command mutex protects empty receipt keys. It never replaces unit parent locks.
            var resource = "WarehouseRent.Command." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{actor.UserId}/{scope}/{key}")));
            await DatabaseDialect.MutexAsync(db, resource, ct);
            var previous = await db.OperationalCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.UserId && x.ScopeId == scope && x.Id == key, ct);
            // The command mutex protects this immutable receipt. Avoid empty-key range locks before
            // upgrading the authorization/business reads, which may need a separate shared mutex.
            // PostgreSQL must choose Serializable before the receipt read. Its snapshot may abort after a mutex wait.
            // SQL Server retains its existing post-receipt upgrade and range-lock behavior.
            if (serializableAfterReceipt && !db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;", ct);
            if (authorize is not null) await authorize(ct);
            if (previous is not null) return previous.PayloadHash == hash ? Results.Content(previous.ResultJson, "application/json")
                : ApiFailure.Result(409, "COMMAND_CONFLICT", "That command identity was used for a different request.");
            auditFacility = facilityId ?? (organizationId == null && scope != Guid.Empty ? scope : null);
            var result = await write(ct);
            await db.SaveChangesAsync(ct);
            var envelope = new Dictionary<string, object> { ["commandId"] = key, ["status"] = "Completed", ["result"] = result };
            if (financeOnlyReadback) envelope["readbackPolicy"] = "CurrentFinance";
            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            db.OperationalCommands.Add(new OperationalCommand { ActorId = actor.UserId, ScopeId = scope, Id = key, OrganizationId = organizationId,
                FacilityId = facilityId ?? (organizationId == null && scope != Guid.Empty ? scope : null), RequiredFacilityRole = facilityRole, PayloadHash = hash, ResultJson = json });
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            onCommitted?.Invoke();
            return Results.Content(json, "application/json");
        }
        catch (CommandFailure e) { return ApiFailure.Result(e.Status, e.Code, e.SafeMessage); }
        catch (SpaceConflict e) { return ApiFailure.Result(409, e.Code, "The selected units are unavailable for this operation."); }
        catch (DbUpdateConcurrencyException) { return ApiFailure.Result(412, "VERSION_CONFLICT", "Refresh the resource before changing it."); }
        catch (Exception e) when (IsPostgresTransactionAbort(e))
        { return ApiFailure.Result(409, "COMMAND_RETRY_REQUIRED", "The transaction was rolled back. Read the current command result, then retry with the same Idempotency-Key if needed."); }
        catch (DbUpdateException e) when (DatabaseDialect.IsUniqueViolation(e))
        { return ApiFailure.Result(409, "DUPLICATE_RESOURCE", "That code or business resource already exists."); }
    }
    // The non-retrying Npgsql execution strategy can wrap a transient server abort in InvalidOperationException.
    private static bool IsPostgresTransactionAbort(Exception error) =>
        error is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }
        || (error is DbUpdateException or InvalidOperationException && error.InnerException is { } inner && IsPostgresTransactionAbort(inner));
    public void Audit(HttpContext context, string action, Guid aggregate, string reason, Guid? organization = null, Guid? facility = null)
        => db.SecurityAudits.Add(new SecurityAudit { Id = Guid.NewGuid(), ActorId = actor.UserId, OrganizationId = organization, FacilityId = facility ?? auditFacility,
            Action = action, AggregateId = aggregate, Reason = reason, CorrelationId = context.TraceIdentifier, At = DateTimeOffset.UtcNow });
    public static void Version(HttpContext context, byte[] version)
    {
        var expected = context.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrEmpty(expected)) throw new CommandFailure(428, "VERSION_REQUIRED", "Read the current version before changing it.");
        if (expected != $"\"{Convert.ToBase64String(version)}\"") throw new CommandFailure(412, "VERSION_CONFLICT", "Refresh the resource before changing it.");
    }
}
public sealed class CommandFailure(int status, string code, string safeMessage) : Exception(code)
{ public int Status { get; } = status; public string Code { get; } = code; public string SafeMessage { get; } = safeMessage; }
