using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacilitiesAndAllocationGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Facilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PermittedGoods = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Published = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalCommands",
                columns: table => new
                {
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalCommands", x => new { x.ActorId, x.ScopeId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceBlocks", x => x.Id);
                    table.CheckConstraint("CK_Maintenance_Dates", "[StartDate]<[EndDate]");
                    table.ForeignKey(
                        name: "FK_MaintenanceBlocks_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.UniqueConstraint("AK_Zones_FacilityId_Id", x => new { x.FacilityId, x.Id });
                    table.ForeignKey(
                        name: "FK_Zones_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentableUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AreaM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Published = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentableUnits", x => x.Id);
                    table.CheckConstraint("CK_Unit_Area", "[AreaM2]>0");
                    table.ForeignKey(
                        name: "FK_RentableUnits_Zones_FacilityId_ZoneId",
                        columns: x => new { x.FacilityId, x.ZoneId },
                        principalTable: "Zones",
                        principalColumns: new[] { "FacilityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    SalesValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    DailyRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateVersions", x => x.Id);
                    table.CheckConstraint("CK_Rate_Dates", "[SalesValidTo] IS NULL OR [SalesValidFrom]<[SalesValidTo]");
                    table.CheckConstraint("CK_Rate_Positive", "[DailyRate]>0 AND [Currency]='VND'");
                    table.ForeignKey(
                        name: "FK_RateVersions_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SpaceAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    HoldExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MaintenanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BookingLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceAllocations", x => x.Id);
                    table.CheckConstraint("CK_Allocation_Dates", "[StartDate]<[EndDate]");
                    table.CheckConstraint("CK_Allocation_Source", "([MaintenanceId] IS NOT NULL AND [BookingLineId] IS NULL) OR ([MaintenanceId] IS NULL AND [BookingLineId] IS NOT NULL)");
                    table.CheckConstraint("CK_Allocation_State", "[State] IN ('Held','Confirmed','Maintenance','Released') AND ([State]<>'Held' OR [HoldExpiresAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SpaceAllocations_MaintenanceBlocks_MaintenanceId",
                        column: x => x.MaintenanceId,
                        principalTable: "MaintenanceBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SpaceAllocations_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UnitOccupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduledEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    HandedOverAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClearedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitOccupancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitOccupancies_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UnitOccupancies_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_Code",
                table: "Facilities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceBlocks_FacilityId",
                table: "MaintenanceBlocks",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_RateVersions_UnitId_SalesValidFrom",
                table: "RateVersions",
                columns: new[] { "UnitId", "SalesValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentableUnits_FacilityId_Code",
                table: "RentableUnits",
                columns: new[] { "FacilityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentableUnits_FacilityId_ZoneId",
                table: "RentableUnits",
                columns: new[] { "FacilityId", "ZoneId" });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceAllocations_MaintenanceId",
                table: "SpaceAllocations",
                column: "MaintenanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SpaceAllocations_UnitId_StartDate_EndDate",
                table: "SpaceAllocations",
                columns: new[] { "UnitId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_UnitOccupancies_OrganizationId",
                table: "UnitOccupancies",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitOccupancies_UnitId",
                table: "UnitOccupancies",
                column: "UnitId",
                unique: true,
                filter: "[ClearedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Zones_FacilityId_Code",
                table: "Zones",
                columns: new[] { "FacilityId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalCommands");

            migrationBuilder.DropTable(
                name: "RateVersions");

            migrationBuilder.DropTable(
                name: "SpaceAllocations");

            migrationBuilder.DropTable(
                name: "UnitOccupancies");

            migrationBuilder.DropTable(
                name: "MaintenanceBlocks");

            migrationBuilder.DropTable(
                name: "RentableUnits");

            migrationBuilder.DropTable(
                name: "Zones");

            migrationBuilder.DropTable(
                name: "Facilities");
        }
    }
}
