using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarMonthTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Rate_Positive",
                table: "RateVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteLine_Values",
                table: "QuoteLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaseLine_Values",
                table: "LeaseVersionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChargeLine_Values",
                table: "ChargeStatementLines");

            migrationBuilder.AddColumn<string>(
                name: "BillingBasis",
                table: "RateVersions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                table: "RateVersions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingBasis",
                table: "QuoteLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<int>(
                name: "CalendarDays",
                table: "QuoteLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                table: "QuoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingBasis",
                table: "LeaseVersionLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<int>(
                name: "CalendarDays",
                table: "LeaseVersionLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                table: "LeaseVersionLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingBasis",
                table: "ChargeStatementLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Daily");

            migrationBuilder.AddColumn<int>(
                name: "CalendarDays",
                table: "ChargeStatementLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyRate",
                table: "ChargeStatementLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rate_Basis",
                table: "RateVersions",
                sql: "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rate_Positive",
                table: "RateVersions",
                sql: "[DailyRate]>=0 AND [Currency]='VND'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteLine_Basis",
                table: "QuoteLines",
                sql: "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL AND [CalendarDays] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0 AND [CalendarDays] IS NOT NULL AND [CalendarDays] BETWEEN 28 AND 31)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteLine_Values",
                table: "QuoteLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>=0 AND [Amount]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaseLine_Values",
                table: "LeaseVersionLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>=0 AND [Amount]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaseVersionLine_Basis",
                table: "LeaseVersionLines",
                sql: "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL AND [CalendarDays] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0 AND [CalendarDays] IS NOT NULL AND [CalendarDays] BETWEEN 28 AND 31)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChargeLine_Basis",
                table: "ChargeStatementLines",
                sql: "([BillingBasis]='Daily' AND [DailyRate]>0 AND [MonthlyRate] IS NULL AND [CalendarDays] IS NULL) OR ([BillingBasis]='CalendarMonth' AND [DailyRate]=0 AND [MonthlyRate] IS NOT NULL AND [MonthlyRate]>0 AND [CalendarDays] IS NOT NULL AND [CalendarDays] BETWEEN 28 AND 31)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChargeLine_Values",
                table: "ChargeStatementLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [Amount]>=0 AND [AreaM2]>0 AND [DailyRate]>=0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [RateVersions] WHERE [BillingBasis]='CalendarMonth') OR EXISTS (SELECT 1 FROM [QuoteLines] WHERE [BillingBasis]='CalendarMonth') OR EXISTS (SELECT 1 FROM [LeaseVersionLines] WHERE [BillingBasis]='CalendarMonth') OR EXISTS (SELECT 1 FROM [ChargeStatementLines] WHERE [BillingBasis]='CalendarMonth') THROW 51018, 'Calendar-month pricing history prevents downgrade.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Rate_Basis",
                table: "RateVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Rate_Positive",
                table: "RateVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteLine_Basis",
                table: "QuoteLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QuoteLine_Values",
                table: "QuoteLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaseLine_Values",
                table: "LeaseVersionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaseVersionLine_Basis",
                table: "LeaseVersionLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChargeLine_Basis",
                table: "ChargeStatementLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChargeLine_Values",
                table: "ChargeStatementLines");

            migrationBuilder.DropColumn(
                name: "BillingBasis",
                table: "RateVersions");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                table: "RateVersions");

            migrationBuilder.DropColumn(
                name: "BillingBasis",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "CalendarDays",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                table: "QuoteLines");

            migrationBuilder.DropColumn(
                name: "BillingBasis",
                table: "LeaseVersionLines");

            migrationBuilder.DropColumn(
                name: "CalendarDays",
                table: "LeaseVersionLines");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                table: "LeaseVersionLines");

            migrationBuilder.DropColumn(
                name: "BillingBasis",
                table: "ChargeStatementLines");

            migrationBuilder.DropColumn(
                name: "CalendarDays",
                table: "ChargeStatementLines");

            migrationBuilder.DropColumn(
                name: "MonthlyRate",
                table: "ChargeStatementLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Rate_Positive",
                table: "RateVersions",
                sql: "[DailyRate]>0 AND [Currency]='VND'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QuoteLine_Values",
                table: "QuoteLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>0 AND [Amount]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaseLine_Values",
                table: "LeaseVersionLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>0 AND [Amount]>=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChargeLine_Values",
                table: "ChargeStatementLines",
                sql: "[StartDate]<[EndDate] AND [Days]>0 AND [Amount]>=0 AND [AreaM2]>0 AND [DailyRate]>0");
        }
    }
}
