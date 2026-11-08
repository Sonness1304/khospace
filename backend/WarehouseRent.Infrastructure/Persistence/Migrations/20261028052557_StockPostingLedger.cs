using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockPostingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockBalances",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalances", x => new { x.OrganizationId, x.ItemId, x.LeaseUnitId });
                    table.CheckConstraint("CK_StockBalance_Nonnegative", "[Quantity]>=0");
                    table.ForeignKey(
                        name: "FK_StockBalances_Items_OrganizationId_ItemId",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBalances_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockPostings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PostedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPostings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockPostings_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockPostings_StockRequests_OrganizationId_StockRequestId",
                        columns: x => new { x.OrganizationId, x.StockRequestId },
                        principalTable: "StockRequests",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockPostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Delta = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PostedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedger", x => x.Id);
                    table.CheckConstraint("CK_Ledger_Nonzero", "[Delta]<>0");
                    table.ForeignKey(
                        name: "FK_StockLedger_Items_OrganizationId_ItemId",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedger_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedger_StockPostings_StockPostingId",
                        column: x => x.StockPostingId,
                        principalTable: "StockPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockLedger_StockRequestLines_RequestLineId",
                        column: x => x.RequestLineId,
                        principalTable: "StockRequestLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockBalances_LeaseUnitId",
                table: "StockBalances",
                column: "LeaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_LeaseUnitId",
                table: "StockLedger",
                column: "LeaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_OrganizationId_ItemId",
                table: "StockLedger",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_OrganizationId_LeaseUnitId_PostedAt",
                table: "StockLedger",
                columns: new[] { "OrganizationId", "LeaseUnitId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_RequestLineId",
                table: "StockLedger",
                column: "RequestLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_StockPostingId_RequestLineId",
                table: "StockLedger",
                columns: new[] { "StockPostingId", "RequestLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_LeaseUnitId",
                table: "StockPostings",
                column: "LeaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_OrganizationId_StockRequestId",
                table: "StockPostings",
                columns: new[] { "OrganizationId", "StockRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_StockRequestId",
                table: "StockPostings",
                column: "StockRequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockBalances");

            migrationBuilder.DropTable(
                name: "StockLedger");

            migrationBuilder.DropTable(
                name: "StockPostings");
        }
    }
}
