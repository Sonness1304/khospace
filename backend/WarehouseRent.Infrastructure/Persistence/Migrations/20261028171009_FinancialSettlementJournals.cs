using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinancialSettlementJournals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Receipt_State",
                table: "PaymentReceipts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "RentalAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "RentalAccounts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                table: "RentalAccounts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "RentalAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "Leases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SettlementEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeStatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PerformedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementEntries", x => x.Id);
                    table.CheckConstraint("CK_Settlement_Values", "[Amount]>0 AND (([Kind]='Deduction' AND [ChargeStatementId] IS NOT NULL AND [PaymentReceiptId] IS NULL) OR ([Kind]='RefundDeposit' AND [ChargeStatementId] IS NULL AND [PaymentReceiptId] IS NULL) OR ([Kind]='RefundUnapplied' AND [ChargeStatementId] IS NULL AND [PaymentReceiptId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_SettlementEntries_ChargeStatements_ChargeStatementId",
                        column: x => x.ChargeStatementId,
                        principalTable: "ChargeStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementEntries_PaymentReceipts_PaymentReceiptId",
                        column: x => x.PaymentReceiptId,
                        principalTable: "PaymentReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementEntries_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StatementAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeStatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLeaseVersionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementAdjustments", x => x.Id);
                    table.CheckConstraint("CK_Adjustment_Values", "[Amount]>=0 AND (([Kind] IN ('Credit','Supplement') AND [Amount]>0 AND [SourceLeaseVersionLineId] IS NULL AND [Quantity] IS NULL AND [UnitPrice] IS NULL) OR ([Kind]='Service' AND [Amount]>0 AND [Quantity]>0 AND [UnitPrice]>0 AND [SourceLeaseVersionLineId] IS NULL) OR ([Kind]='RentSupplement' AND [SourceLeaseVersionLineId] IS NOT NULL AND [Quantity] IS NULL AND [UnitPrice] IS NULL))");
                    table.ForeignKey(
                        name: "FK_StatementAdjustments_ChargeStatements_ChargeStatementId",
                        column: x => x.ChargeStatementId,
                        principalTable: "ChargeStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementAdjustments_LeaseVersionLines_SourceLeaseVersionLineId",
                        column: x => x.SourceLeaseVersionLineId,
                        principalTable: "LeaseVersionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatementAdjustments_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StatementAdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettlementEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationReleases", x => x.Id);
                    table.CheckConstraint("CK_Release_Values", "[Amount]>0 AND (([FundAllocationId] IS NOT NULL AND [SettlementEntryId] IS NULL) OR ([FundAllocationId] IS NULL AND [SettlementEntryId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_AllocationReleases_FundAllocations_FundAllocationId",
                        column: x => x.FundAllocationId,
                        principalTable: "FundAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationReleases_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationReleases_SettlementEntries_SettlementEntryId",
                        column: x => x.SettlementEntryId,
                        principalTable: "SettlementEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AllocationReleases_StatementAdjustments_StatementAdjustmentId",
                        column: x => x.StatementAdjustmentId,
                        principalTable: "StatementAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Receipt_State",
                table: "PaymentReceipts",
                sql: "[State] IN ('Recorded','Verified','Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationReleases_FundAllocationId",
                table: "AllocationReleases",
                column: "FundAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationReleases_RentalAccountId",
                table: "AllocationReleases",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationReleases_SettlementEntryId",
                table: "AllocationReleases",
                column: "SettlementEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationReleases_StatementAdjustmentId",
                table: "AllocationReleases",
                column: "StatementAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementEntries_ChargeStatementId",
                table: "SettlementEntries",
                column: "ChargeStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementEntries_OrganizationId_Reference",
                table: "SettlementEntries",
                columns: new[] { "OrganizationId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementEntries_PaymentReceiptId",
                table: "SettlementEntries",
                column: "PaymentReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementEntries_RentalAccountId",
                table: "SettlementEntries",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementAdjustments_ChargeStatementId",
                table: "StatementAdjustments",
                column: "ChargeStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementAdjustments_RentalAccountId",
                table: "StatementAdjustments",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementAdjustments_SourceLeaseVersionLineId",
                table: "StatementAdjustments",
                column: "SourceLeaseVersionLineId",
                unique: true,
                filter: "[SourceLeaseVersionLineId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM [StatementAdjustments]) OR EXISTS(SELECT 1 FROM [SettlementEntries]) OR EXISTS(SELECT 1 FROM [AllocationReleases]) OR EXISTS(SELECT 1 FROM [RentalAccounts] WHERE [ClosedAt] IS NOT NULL) OR EXISTS(SELECT 1 FROM [PaymentReceipts] WHERE [State]='Rejected') THROW 51002, N'Financial settlement history exists; use a data-preserving rollback migration.', 1;");
            migrationBuilder.DropTable(
                name: "AllocationReleases");

            migrationBuilder.DropTable(
                name: "SettlementEntries");

            migrationBuilder.DropTable(
                name: "StatementAdjustments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Receipt_State",
                table: "PaymentReceipts");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "RentalAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "RentalAccounts");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                table: "RentalAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RentalAccounts");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Leases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Receipt_State",
                table: "PaymentReceipts",
                sql: "[State] IN ('Recorded','Verified')");
        }
    }
}
