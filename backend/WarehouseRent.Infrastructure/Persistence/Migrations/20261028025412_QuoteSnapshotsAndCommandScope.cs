using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteSnapshotsAndCommandScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                table: "OperationalCommands",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "OperationalCommands",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RentalSubtotal = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RequiredDeposit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.CheckConstraint("CK_Quote_Amounts", "[RentalSubtotal]>=0 AND [RequiredDeposit]>=0");
                    table.CheckConstraint("CK_Quote_Dates", "[StartDate]<[EndDate] AND [CreatedAt]<[ExpiresAt]");
                    table.ForeignKey(
                        name: "FK_Quotes_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Quotes_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UnitVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    RateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    AreaM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteLines", x => x.Id);
                    table.CheckConstraint("CK_QuoteLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>0 AND [Amount]>=0");
                    table.ForeignKey(
                        name: "FK_QuoteLines_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteLines_RateVersions_RateVersionId",
                        column: x => x.RateVersionId,
                        principalTable: "RateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuoteLines_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_QuoteId_UnitId_StartDate",
                table: "QuoteLines",
                columns: new[] { "QuoteId", "UnitId", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_RateVersionId",
                table: "QuoteLines",
                column: "RateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteLines_UnitId",
                table: "QuoteLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ExpiresAt",
                table: "Quotes",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_FacilityId",
                table: "Quotes",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_OrganizationId_CreatedAt",
                table: "Quotes",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteLines");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                table: "OperationalCommands");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "OperationalCommands");
        }
    }
}
