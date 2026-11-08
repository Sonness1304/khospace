using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace WarehouseRent.Infrastructure.Persistence;

public sealed partial class AppDbContext
{
    private static void ConfigurePostgresPreview(ModelBuilder model)
    {
        var schema = PostgresConnectionPolicy.ApplicationSchema;
        model.HasDefaultSchema(schema);
        model.HasSequence<long>("version_tokens", schema);
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var check in entity.GetCheckConstraints().ToArray())
                model.Entity(entity.ClrType).ToTable(t => t.HasCheckConstraint(check.ModelName, TranslatePostgresExpression(check.Sql, check.ModelName)));
            foreach (var index in entity.GetIndexes())
                if (index.GetFilter() is { } filter)
                    index.SetFilter(TranslatePostgresExpression(filter, "index"));
            foreach (var property in entity.GetProperties())
            {
                if (property.GetCollation() is { } collation)
                {
                    if (collation != "Latin1_General_100_BIN2")
                        throw new InvalidOperationException($"Unreviewed PostgreSQL collation: {collation}");
                    property.SetCollation("C");
                }
                if (property.ClrType != typeof(byte[])) continue;
                if (property.Name == "Version" || property.GetMaxLength() == 8)
                {
                    property.SetColumnType("bytea");
                    property.SetMaxLength(8);
                    model.Entity(entity.ClrType).ToTable(t => t.HasCheckConstraint(
                        $"CK_{entity.GetTableName()}_{property.Name}_8", $"octet_length(\"{property.Name}\") = 8"));
                }
                if (property.Name == "Version" && property.IsConcurrencyToken)
                    property.SetDefaultValueSql($"int8send(nextval('\"{schema}\".\"version_tokens\"'::regclass))");
            }
        }
    }

    // This is deliberately a tiny translator for the known model, not a general SQL converter.
    // Unreviewed functions/SQL syntax fail closed instead of silently weakening a database guard.
    private static string TranslatePostgresExpression(string sql, string name)
    {
        const string gate = "[StartUtc]<[EndUtc] AND DATEDIFF_BIG(SECOND,[StartUtc],[EndUtc]) BETWEEN 300 AND 14400 AND DATEPART(TZOFFSET,[StartUtc])=0 AND DATEPART(TZOFFSET,[EndUtc])=0";
        if (name == "CK_GateVisit_Window")
        {
            if (sql != gate) throw new InvalidOperationException("Unreviewed gate-window constraint.");
            // SQL Server DATEDIFF counts second boundaries, not the fractional elapsed interval.
            // timestamptz does not retain the original offset; UTC input guards still need runtime parity.
            return "\"StartUtc\" < \"EndUtc\" AND (floor(extract(epoch FROM \"EndUtc\")) - floor(extract(epoch FROM \"StartUtc\"))) BETWEEN 300 AND 14400";
        }
        if (name == "CK_PaymentIntent_BankTransfer")
        {
            if (sql != BankTransferConstraint) throw new InvalidOperationException("Unreviewed bank-transfer constraint.");
            return "(\"Provider\"='SePay' AND \"Method\"='SePayQr' AND \"BankTransferReference\" IS NOT NULL AND \"BankTransferReference\" ~ '^SEVQR[A-Z0-9]{10}$' AND \"ExpiresAt\" IS NOT NULL AND \"ExpiresAt\">\"CreatedAt\" AND \"MerchantId\" ~ '^[0-9A-F]{64}$' AND \"CheckoutUrl\" IS NULL) OR (\"Provider\"<>'SePay' AND \"BankTransferReference\" IS NULL AND \"ExpiresAt\" IS NULL)";
        }
        sql = Regex.Replace(sql, @"DATALENGTH\(\[([A-Za-z][A-Za-z0-9_]*)\]\)", "octet_length([$1])", RegexOptions.CultureInvariant);
        // SQL Server LEN ignores trailing U+0020; plain char_length would change accepted values.
        sql = Regex.Replace(sql, @"LEN\(\[([A-Za-z][A-Za-z0-9_]*)\]\)", "char_length(rtrim([$1], ' '))", RegexOptions.CultureInvariant);
        sql = sql.Replace("[IsFullRequest] = 1", "[IsFullRequest] = TRUE", StringComparison.Ordinal);
        sql = Regex.Replace(sql, @"\[([A-Za-z][A-Za-z0-9_]*)\]", "\"$1\"", RegexOptions.CultureInvariant);
        var residue = Regex.Replace(sql, "'([^']|'')*'|\"[A-Za-z][A-Za-z0-9_]*\"|\\b(AND|OR|NOT|IS|NULL|IN|BETWEEN|TRUE|octet_length|char_length|rtrim)\\b|[0-9]+|[()=<>!, .+-]", "", RegexOptions.CultureInvariant);
        if (residue.Length != 0) throw new InvalidOperationException($"Unreviewed PostgreSQL expression: {name}");
        return sql;
    }
}
