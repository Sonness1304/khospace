using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerStockRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaseUom = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.UniqueConstraint("AK_Items_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Items_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ReplacesRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClearanceAuthorizedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClearanceAuthorizedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockRequests", x => x.Id);
                    table.UniqueConstraint("AK_StockRequests_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_StockRequest_State", "[State] IN ('Draft','Submitted','Approved','Rejected','Cancelled','Fulfilled')");
                    table.CheckConstraint("CK_StockRequest_Type", "[Type] IN ('Receipt','Issue')");
                    table.ForeignKey(
                        name: "FK_StockRequests_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockRequests_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockRequests_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockRequests_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockRequests_StockRequests_OrganizationId_ReplacesRequestId",
                        columns: x => new { x.OrganizationId, x.ReplacesRequestId },
                        principalTable: "StockRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockRequestLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockRequestLines", x => x.Id);
                    table.CheckConstraint("CK_StockRequestLine_Quantity", "[Quantity]>0");
                    table.ForeignKey(
                        name: "FK_StockRequestLines_Items_OrganizationId_ItemId",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockRequestLines_StockRequests_OrganizationId_StockRequestId",
                        columns: x => new { x.OrganizationId, x.StockRequestId },
                        principalTable: "StockRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_OrganizationId_Sku",
                table: "Items",
                columns: new[] { "OrganizationId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockRequestLines_OrganizationId_ItemId",
                table: "StockRequestLines",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockRequestLines_OrganizationId_StockRequestId",
                table: "StockRequestLines",
                columns: new[] { "OrganizationId", "StockRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockRequestLines_StockRequestId_ItemId",
                table: "StockRequestLines",
                columns: new[] { "StockRequestId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockRequests_FacilityId",
                table: "StockRequests",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_StockRequests_LeaseId",
                table: "StockRequests",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockRequests_LeaseUnitId_State",
                table: "StockRequests",
                columns: new[] { "LeaseUnitId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_StockRequests_OrganizationId_ReplacesRequestId",
                table: "StockRequests",
                columns: new[] { "OrganizationId", "ReplacesRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockRequests_ReplacesRequestId",
                table: "StockRequests",
                column: "ReplacesRequestId",
                unique: true,
                filter: "[ReplacesRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockRequestLines");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "StockRequests");
        }
    }
}
