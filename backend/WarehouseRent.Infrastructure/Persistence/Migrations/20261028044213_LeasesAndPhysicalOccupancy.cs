using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeasesAndPhysicalOccupancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClearedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.Id);
                    table.CheckConstraint("CK_Lease_Dates", "[StartDate]<[EndDate]");
                    table.CheckConstraint("CK_Lease_State", "[State] IN ('Scheduled','Active','Cancelled','ClearancePending','SettlementPending','Closed')");
                    table.ForeignKey(
                        name: "FK_Leases_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Leases_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InitialAllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseUnits", x => x.Id);
                    table.UniqueConstraint("AK_LeaseUnits_UnitId_Id", x => new { x.UnitId, x.Id });
                    table.ForeignKey(
                        name: "FK_LeaseUnits_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseUnits_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseUnits_SpaceAllocations_InitialAllocationId",
                        column: x => x.InitialAllocationId,
                        principalTable: "SpaceAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaseVersions_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaseVersionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceQuoteLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Days = table.Column<int>(type: "int", nullable: false),
                    AreaM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DailyRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaseVersionLines", x => x.Id);
                    table.CheckConstraint("CK_LeaseLine_Values", "[StartDate]<[EndDate] AND [Days]>0 AND [AreaM2]>0 AND [DailyRate]>0 AND [Amount]>=0");
                    table.ForeignKey(
                        name: "FK_LeaseVersionLines_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseVersionLines_LeaseVersions_LeaseVersionId",
                        column: x => x.LeaseVersionId,
                        principalTable: "LeaseVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LeaseVersionLines_QuoteLines_SourceQuoteLineId",
                        column: x => x.SourceQuoteLineId,
                        principalTable: "QuoteLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOccupancies_LeaseUnitId",
                table: "UnitOccupancies",
                column: "LeaseUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitOccupancies_UnitId_LeaseUnitId",
                table: "UnitOccupancies",
                columns: new[] { "UnitId", "LeaseUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leases_BookingId",
                table: "Leases",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leases_FacilityId",
                table: "Leases",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId",
                table: "Leases",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_RentalAccountId",
                table: "Leases",
                column: "RentalAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseUnits_InitialAllocationId",
                table: "LeaseUnits",
                column: "InitialAllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseUnits_LeaseId_UnitId",
                table: "LeaseUnits",
                columns: new[] { "LeaseId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseVersionLines_LeaseUnitId",
                table: "LeaseVersionLines",
                column: "LeaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseVersionLines_LeaseVersionId_SourceQuoteLineId",
                table: "LeaseVersionLines",
                columns: new[] { "LeaseVersionId", "SourceQuoteLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaseVersionLines_SourceQuoteLineId",
                table: "LeaseVersionLines",
                column: "SourceQuoteLineId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaseVersions_LeaseId_VersionNo",
                table: "LeaseVersions",
                columns: new[] { "LeaseId", "VersionNo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UnitOccupancies_LeaseUnits_UnitId_LeaseUnitId",
                table: "UnitOccupancies",
                columns: new[] { "UnitId", "LeaseUnitId" },
                principalTable: "LeaseUnits",
                principalColumns: new[] { "UnitId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UnitOccupancies_LeaseUnits_UnitId_LeaseUnitId",
                table: "UnitOccupancies");

            migrationBuilder.DropTable(
                name: "LeaseVersionLines");

            migrationBuilder.DropTable(
                name: "LeaseUnits");

            migrationBuilder.DropTable(
                name: "LeaseVersions");

            migrationBuilder.DropTable(
                name: "Leases");

            migrationBuilder.DropIndex(
                name: "IX_UnitOccupancies_LeaseUnitId",
                table: "UnitOccupancies");

            migrationBuilder.DropIndex(
                name: "IX_UnitOccupancies_UnitId_LeaseUnitId",
                table: "UnitOccupancies");
        }
    }
}
