using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManualFundsAndStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ZeroDepositVerifiedAt",
                table: "RentalAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZeroDepositVerifiedBy",
                table: "RentalAccounts",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChargeStatements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Total = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeStatements", x => x.Id);
                    table.CheckConstraint("CK_Statement_Total", "[Total]>=0");
                    table.ForeignKey(
                        name: "FK_ChargeStatements_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargeStatements_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReceipts", x => x.Id);
                    table.CheckConstraint("CK_Receipt_Amount", "[Amount]>0");
                    table.CheckConstraint("CK_Receipt_State", "[State] IN ('Recorded','Verified')");
                    table.ForeignKey(
                        name: "FK_PaymentReceipts_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentReceipts_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChargeStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeStatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLeaseVersionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    AreaM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargeStatementLines", x => x.Id);
                    table.CheckConstraint("CK_ChargeLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [Amount]>=0 AND [AreaM2]>0 AND [DailyRate]>0");
                    table.ForeignKey(
                        name: "FK_ChargeStatementLines_ChargeStatements_ChargeStatementId",
                        column: x => x.ChargeStatementId,
                        principalTable: "ChargeStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChargeStatementLines_LeaseVersionLines_SourceLeaseVersionLineId",
                        column: x => x.SourceLeaseVersionLineId,
                        principalTable: "LeaseVersionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FundAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ChargeStatementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundAllocations", x => x.Id);
                    table.CheckConstraint("CK_FundAllocation_Amount", "[Amount]>0");
                    table.CheckConstraint("CK_FundAllocation_Target", "([TargetType]='Deposit' AND [ChargeStatementId] IS NULL) OR ([TargetType]='Charge' AND [ChargeStatementId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_FundAllocations_ChargeStatements_ChargeStatementId",
                        column: x => x.ChargeStatementId,
                        principalTable: "ChargeStatements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundAllocations_PaymentReceipts_PaymentReceiptId",
                        column: x => x.PaymentReceiptId,
                        principalTable: "PaymentReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundAllocations_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepositTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositTransactions", x => x.Id);
                    table.CheckConstraint("CK_DepositTransaction_Positive", "[Amount]>0");
                    table.ForeignKey(
                        name: "FK_DepositTransactions_FundAllocations_FundAllocationId",
                        column: x => x.FundAllocationId,
                        principalTable: "FundAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepositTransactions_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargeStatementLines_ChargeStatementId",
                table: "ChargeStatementLines",
                column: "ChargeStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_ChargeStatementLines_SourceLeaseVersionLineId",
                table: "ChargeStatementLines",
                column: "SourceLeaseVersionLineId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargeStatements_LeaseId_PeriodStart",
                table: "ChargeStatements",
                columns: new[] { "LeaseId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargeStatements_RentalAccountId",
                table: "ChargeStatements",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DepositTransactions_FundAllocationId",
                table: "DepositTransactions",
                column: "FundAllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepositTransactions_RentalAccountId",
                table: "DepositTransactions",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_FundAllocations_ChargeStatementId",
                table: "FundAllocations",
                column: "ChargeStatementId");

            migrationBuilder.CreateIndex(
                name: "IX_FundAllocations_PaymentReceiptId_TargetType_ChargeStatementId",
                table: "FundAllocations",
                columns: new[] { "PaymentReceiptId", "TargetType", "ChargeStatementId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundAllocations_RentalAccountId",
                table: "FundAllocations",
                column: "RentalAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_OrganizationId_Method_Reference",
                table: "PaymentReceipts",
                columns: new[] { "OrganizationId", "Method", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReceipts_RentalAccountId",
                table: "PaymentReceipts",
                column: "RentalAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargeStatementLines");

            migrationBuilder.DropTable(
                name: "DepositTransactions");

            migrationBuilder.DropTable(
                name: "FundAllocations");

            migrationBuilder.DropTable(
                name: "ChargeStatements");

            migrationBuilder.DropTable(
                name: "PaymentReceipts");

            migrationBuilder.DropColumn(
                name: "ZeroDepositVerifiedAt",
                table: "RentalAccounts");

            migrationBuilder.DropColumn(
                name: "ZeroDepositVerifiedBy",
                table: "RentalAccounts");
        }
    }
}
