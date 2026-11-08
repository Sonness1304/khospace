using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WarehouseRent.Infrastructure.Facilities;
using WarehouseRent.Infrastructure.Persistence;

namespace WarehouseRent.Tests;

public sealed class DatabaseDialectTests
{
    private static AppDbContext Postgres() => new(PostgresPreviewConfiguration.ForSyntheticFixture(new(
        "Host=127.0.0.1;Database=warehouse_rent_synthetic;Username=fixture;Password=fixture;SSL Mode=Disable", "PostgresSchemaPreviewTests", true)));
    private static AppDbContext SqlServer() => new(SqlConfiguration.Options("Server=(local);Database=Fixture;Integrated Security=true;TrustServerCertificate=true"));

    [Fact]
    public void Runtime_dialect_exists_without_enabling_postgres_runtime() =>
        Assert.NotNull(typeof(AppDbContext).Assembly.GetType("WarehouseRent.Infrastructure.Persistence.DatabaseDialect"));

    public static IEnumerable<object[]> ReviewedShapes()
    {
        foreach (var table in new[] { "Bookings", "GateLanes", "GateVisits", "RenewalAcceptances", "StockRequests", "Facilities", "PaymentIntents", "PaymentReceipts", "ChargeStatements", "StockCounts", "CustomerOrganizations", "RentableUnits" })
            yield return [table, new[] { "Id" }];
        yield return ["RentalAccounts", new[] { "BookingId" }];
        yield return ["Leases", new[] { "Id" }];
        yield return ["Leases", new[] { "BookingId" }];
        yield return ["Leases", new[] { "RentalAccountId" }];
        yield return ["StockBalances", new[] { "OrganizationId", "ItemId", "LeaseUnitId" }];
    }

    [Theory]
    [MemberData(nameof(ReviewedShapes))]
    public void Postgres_translates_only_reviewed_shapes_to_private_schema_parameterized_row_locks(string table, string[] columns)
    {
        using var db = Postgres();
        var arguments = columns.Select(_ => (object)Guid.NewGuid()).ToArray();
        var predicate = string.Join(" AND ", columns.Select((column, index) => $"[{column}]={{{index}}}"));
        var sql = FormattableStringFactory.Create($"SELECT * FROM [{table}] WITH (UPDLOCK,HOLDLOCK) WHERE {predicate}", arguments);
        var entity = db.Model.GetEntityTypes().Single(e => e.GetTableName() == table).ClrType;
        var query = (IQueryable)typeof(DatabaseDialect).GetMethod(nameof(DatabaseDialect.Rows))!.MakeGenericMethod(entity).Invoke(null, [db, sql])!;
        var (format, retained) = SqlArguments(query);
        Assert.Equal($"SELECT * FROM \"khospace\".\"{table}\" WHERE {string.Join(" AND ", columns.Select((column, index) => $"\"{column}\"={{{index}}}"))} FOR UPDATE", format);
        Assert.Equal(arguments.Length, retained.Length);
        for (var i = 0; i < arguments.Length; i++) Assert.Same(arguments[i], retained[i]);
        var generated = query.ToQueryString();
        Assert.Contains("FOR UPDATE", generated);
        Assert.DoesNotContain("UPDLOCK", generated);
    }

    [Theory]
    [InlineData("SELECT * FROM [RentableUnits] WHERE [Id]={0}")]
    [InlineData("SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={0} OR 1=1")]
    [InlineData("SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={0}; DROP TABLE x")]
    [InlineData("SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [FacilityId]={0}")]
    [InlineData("SELECT * FROM [Unknown] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={0}")]
    [InlineData("SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={0:N}")]
    public void Postgres_rejects_unreviewed_sql_instead_of_general_sql_rewriting(string format)
    {
        using var db = Postgres();
        Assert.Throws<InvalidOperationException>(() => DatabaseDialect.Rows<RentableUnit>(db, FormattableStringFactory.Create(format, Guid.NewGuid())));
    }

    [Fact]
    public void Postgres_rejects_entity_table_mismatch_and_missing_or_unused_arguments()
    {
        using var db = Postgres();
        Assert.Throws<InvalidOperationException>(() => DatabaseDialect.Rows<RentableUnit>(db, $"SELECT * FROM [Bookings] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={Guid.NewGuid()}"));
        const string format = "SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={0}";
        Assert.Throws<InvalidOperationException>(() => DatabaseDialect.Rows<RentableUnit>(db, FormattableStringFactory.Create(format, [])));
        Assert.Throws<InvalidOperationException>(() => DatabaseDialect.Rows<RentableUnit>(db, FormattableStringFactory.Create(format, Guid.NewGuid(), Guid.NewGuid())));
    }

    [Fact]
    public void Sql_server_keeps_original_query_and_parameter_objects()
    {
        using var db = SqlServer();
        FormattableString original = $"SELECT * FROM [RentableUnits] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={Guid.NewGuid()}";
        var query = DatabaseDialect.Rows<RentableUnit>(db, original);
        var (format, retained) = SqlArguments(query);
        Assert.False(DatabaseDialect.IsPostgres(db));
        Assert.Equal(original.Format, format);
        Assert.Same(original.GetArgument(0), Assert.Single(retained));
        Assert.Contains("WITH (UPDLOCK,HOLDLOCK)", query.ToQueryString());
    }

    [Theory]
    [InlineData("23505", true)]
    [InlineData("23503", false)]
    [InlineData("23514", false)]
    [InlineData("40001", false)]
    [InlineData("40P01", false)]
    public void Unique_violation_mapping_does_not_swallow_other_postgres_guards(string state, bool expected) =>
        Assert.Equal(expected, DatabaseDialect.IsUniqueViolation(new DbUpdateException("synthetic", new PostgresException("synthetic", "ERROR", "ERROR", state))));

    [Fact]
    public void Unique_violation_mapping_rejects_non_provider_errors() =>
        Assert.False(DatabaseDialect.IsUniqueViolation(new DbUpdateException("synthetic", new InvalidOperationException("23505"))));

    [Fact]
    public async Task Mutex_and_services_require_callers_transaction_before_database_io()
    {
        foreach (var db in new[] { Postgres(), SqlServer() })
        {
            using (db)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseDialect.MutexAsync(db, "WarehouseRent.synthetic", default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitLockService(db).LockAsync(Guid.NewGuid(), [Guid.NewGuid()]));
                await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitLockService(db).DatabaseUtcAsync());
                await Assert.ThrowsAsync<InvalidOperationException>(() => new StockLockService(db).LockAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default));
            }
        }
    }

    [Fact]
    public void Runtime_dialect_keeps_preview_offline()
    {
        using var db = Postgres();
        Assert.True(DatabaseDialect.IsPostgres(db));
        Assert.Throws<InvalidOperationException>(() => db.Database.OpenConnection());
    }

    private static (string Format, object[] Arguments) SqlArguments(IQueryable query)
    {
        // Inspect parameter identity without compiling/executing a command or embedding values in assertions.
        var expression = query.Expression;
        var format = (string)expression.GetType().GetProperty("Sql")!.GetValue(expression)!;
        var argument = Assert.IsType<ConstantExpression>(expression.GetType().GetProperty("Argument")!.GetValue(expression));
        return (format, Assert.IsType<object[]>(argument.Value));
    }
}
