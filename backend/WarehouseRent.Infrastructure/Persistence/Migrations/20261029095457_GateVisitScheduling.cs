using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GateVisitScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leases_OrganizationId",
                table: "Leases");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Leases_OrganizationId_FacilityId_Id",
                table: "Leases",
                columns: new[] { "OrganizationId", "FacilityId", "Id" });

            migrationBuilder.CreateTable(
                name: "GateLanes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GateLanes", x => x.Id);
                    table.UniqueConstraint("AK_GateLanes_FacilityId_Id", x => new { x.FacilityId, x.Id });
                    table.ForeignKey(
                        name: "FK_GateLanes_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GateVisits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LaneId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Vehicle = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ArrivedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ArrivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DepartedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DepartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GateVisits", x => x.Id);
                    table.CheckConstraint("CK_GateVisit_Direction", "[Direction] IN ('Inbound','Outbound')");
                    table.CheckConstraint("CK_GateVisit_Lifecycle", "([State]='Scheduled' AND [ArrivedAt] IS NULL AND [DepartedAt] IS NULL AND [CancelledAt] IS NULL) OR ([State]='CheckedIn' AND [ArrivedAt] IS NOT NULL AND [ArrivedBy] IS NOT NULL AND [DepartedAt] IS NULL AND [CancelledAt] IS NULL) OR ([State]='Completed' AND [ArrivedAt] IS NOT NULL AND [ArrivedBy] IS NOT NULL AND [DepartedAt] IS NOT NULL AND [DepartedAt]>=[ArrivedAt] AND [DepartedBy] IS NOT NULL AND [CancelledAt] IS NULL) OR ([State]='Cancelled' AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [ArrivedAt] IS NULL AND [DepartedAt] IS NULL)");
                    table.CheckConstraint("CK_GateVisit_Window", "[StartUtc]<[EndUtc] AND DATEDIFF_BIG(SECOND,[StartUtc],[EndUtc]) BETWEEN 300 AND 14400 AND DATEPART(TZOFFSET,[StartUtc])=0 AND DATEPART(TZOFFSET,[EndUtc])=0");
                    table.ForeignKey(
                        name: "FK_GateVisits_GateLanes_FacilityId_LaneId",
                        columns: x => new { x.FacilityId, x.LaneId },
                        principalTable: "GateLanes",
                        principalColumns: new[] { "FacilityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GateVisits_Leases_OrganizationId_FacilityId_LeaseId",
                        columns: x => new { x.OrganizationId, x.FacilityId, x.LeaseId },
                        principalTable: "Leases",
                        principalColumns: new[] { "OrganizationId", "FacilityId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GateLanes_FacilityId_Code",
                table: "GateLanes",
                columns: new[] { "FacilityId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GateVisits_FacilityId_LaneId",
                table: "GateVisits",
                columns: new[] { "FacilityId", "LaneId" });

            migrationBuilder.CreateIndex(
                name: "IX_GateVisits_FacilityId_OrganizationId_StartUtc_Id",
                table: "GateVisits",
                columns: new[] { "FacilityId", "OrganizationId", "StartUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GateVisits_LaneId",
                table: "GateVisits",
                column: "LaneId",
                unique: true,
                filter: "[State]='CheckedIn'");

            migrationBuilder.CreateIndex(
                name: "IX_GateVisits_LaneId_StartUtc_EndUtc",
                table: "GateVisits",
                columns: new[] { "LaneId", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GateVisits_OrganizationId_FacilityId_LeaseId",
                table: "GateVisits",
                columns: new[] { "OrganizationId", "FacilityId", "LeaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [GateVisits]) OR EXISTS (SELECT 1 FROM [GateLanes]) THROW 51017, 'Gate history exists. Restore a validated backup instead of deleting logistics evidence.', 1;");
            migrationBuilder.DropTable(
                name: "GateVisits");

            migrationBuilder.DropTable(
                name: "GateLanes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Leases_OrganizationId_FacilityId_Id",
                table: "Leases");

            migrationBuilder.CreateIndex(
                name: "IX_Leases_OrganizationId",
                table: "Leases",
                column: "OrganizationId");
        }
    }
}
