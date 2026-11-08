using System.Text;
using Microsoft.EntityFrameworkCore;

namespace WarehouseRent.Infrastructure.Persistence;

/// <summary>Reviewable offline DDL; NOT a migration, runtime activation or business-parity claim.</summary>
public static class PostgresSchemaPreview
{
    public static IReadOnlyList<string> MissingRuntimeGuards { get; } = Array.AsReadOnly(new[]
    {
        "Complete PostgreSQL business/concurrency regression coverage for existing application SaveChanges protections and parent-row locking; no historical business triggers exist to port.",
        "Validate the proposed global 8-byte sequence/trigger tokens under concurrent writes, snapshot readback and rollback.",
        "Prove timezone/UTC input parity: PostgreSQL timestamptz cannot retain the original submitted offset.",
        "Create a separate reviewed PostgreSQL migration history; never apply the SQL Server migrations.",
        "Review least-privilege private-schema roles, Supabase API exposure/RLS, backup/restore and project ownership before activation."
    });

    public static string GenerateScript()
    {
        using var context = new AppDbContext(PostgresPreviewConfiguration.ForSyntheticFixture(new(
            "Host=127.0.0.1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture;SSL Mode=Disable",
            "PostgresSchemaPreviewTests", true)));
        var result = new StringBuilder("-- OFFLINE SCHEMA PREVIEW ONLY. NOT MIGRATION READY. DO NOT APPLY TO AN OWNER DATABASE.\n");
        foreach (var guard in MissingRuntimeGuards) result.AppendLine($"-- INCOMPLETE: {guard}");
        result.AppendLine(context.Database.GenerateCreateScript());
        // Proposed token mechanism preserves the public byte[8] contract; not xmin's 4-byte shape.
        result.AppendLine("CREATE FUNCTION \"khospace\".\"set_version_token\"() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW.\"Version\" := int8send(nextval('\"khospace\".\"version_tokens\"'::regclass)); RETURN NEW; END $$;");
        foreach (var entity in context.Model.GetEntityTypes().Where(e => e.FindProperty("Version") is { IsConcurrencyToken: true }))
            result.AppendLine($"CREATE TRIGGER \"TR_Version\" BEFORE INSERT OR UPDATE ON \"khospace\".\"{entity.GetTableName()}\" FOR EACH ROW EXECUTE FUNCTION \"khospace\".\"set_version_token\"();");
        return result.ToString();
    }
}
