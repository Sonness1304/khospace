using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StockCorrectionsPartialFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockRequestLine_Quantity",
                table: "StockRequestLines");

            migrationBuilder.DropIndex(
                name: "IX_StockPostings_StockRequestId",
                table: "StockPostings");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_StockPostingId_RequestLineId",
                table: "StockLedger");

            migrationBuilder.AddColumn<decimal>(
                name: "FulfilledQuantity",
                table: "StockRequestLines",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "StockRequestId",
                table: "StockPostings",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentId",
                table: "StockPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFullRequest",
                table: "StockPostings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "StockPostings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "StockPostings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            // Preserve every P0 ID and delta: the original full posting is its first fulfillment.
            migrationBuilder.Sql("UPDATE [StockPostings] SET [IsFullRequest]=1, [FulfillmentId]=[Id]; UPDATE l SET [FulfilledQuantity]=ABS(e.[Delta]) FROM [StockRequestLines] l JOIN [StockLedger] e ON e.[RequestLineId]=l.[Id];");

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesPostingId",
                table: "StockPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StockCountId",
                table: "StockPostings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestLineId",
                table: "StockLedger",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "StockBalances",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ExpectedBalanceVersion = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                    table.CheckConstraint("CK_Count_Quantity", "[ExpectedQuantity]>=0 AND [CountedQuantity]>=0");
                    table.CheckConstraint("CK_Count_State", "[State] IN ('Pending','Posted','Cancelled') AND ([State]<>'Posted' OR ([ApprovedAt] IS NOT NULL AND [ApprovedBy] IS NOT NULL AND [ApprovedBy]<>[CreatedBy]))");
                    table.ForeignKey(
                        name: "FK_StockCounts_Items_OrganizationId_ItemId",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCounts_LeaseUnits_LeaseUnitId",
                        column: x => x.LeaseUnitId,
                        principalTable: "LeaseUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockRequestLine_Quantity",
                table: "StockRequestLines",
                sql: "[Quantity]>0 AND [FulfilledQuantity]>=0 AND [FulfilledQuantity]<=[Quantity]");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_FulfillmentId",
                table: "StockPostings",
                column: "FulfillmentId",
                unique: true,
                filter: "[FulfillmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_ReversesPostingId",
                table: "StockPostings",
                column: "ReversesPostingId",
                unique: true,
                filter: "[ReversesPostingId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_StockCountId",
                table: "StockPostings",
                column: "StockCountId",
                unique: true,
                filter: "[StockCountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_StockRequestId",
                table: "StockPostings",
                column: "StockRequestId",
                unique: true,
                filter: "[StockRequestId] IS NOT NULL AND [IsFullRequest] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Posting_Source",
                table: "StockPostings",
                sql: "([Type] IN ('Receipt','Issue') AND [StockRequestId] IS NOT NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NULL) OR ([Type]='Transfer' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NULL) OR ([Type]='Reversal' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NOT NULL AND [StockCountId] IS NULL) OR ([Type]='Adjustment' AND [StockRequestId] IS NULL AND [ReversesPostingId] IS NULL AND [StockCountId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_StockPostingId_LeaseUnitId_ItemId",
                table: "StockLedger",
                columns: new[] { "StockPostingId", "LeaseUnitId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_StockPostingId_RequestLineId",
                table: "StockLedger",
                columns: new[] { "StockPostingId", "RequestLineId" },
                unique: true,
                filter: "[RequestLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_LeaseUnitId_State",
                table: "StockCounts",
                columns: new[] { "LeaseUnitId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_OrganizationId_ItemId",
                table: "StockCounts",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StockPostings_StockCounts_StockCountId",
                table: "StockPostings",
                column: "StockCountId",
                principalTable: "StockCounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockPostings_StockPostings_ReversesPostingId",
                table: "StockPostings",
                column: "ReversesPostingId",
                principalTable: "StockPostings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM [StockPostings] WHERE [IsFullRequest]=0 OR [StockRequestId] IS NULL) OR EXISTS(SELECT 1 FROM [StockCounts]) THROW 51001, N'P1 stock history exists; rollback requires an explicit data-preserving migration.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_StockPostings_StockCounts_StockCountId",
                table: "StockPostings");

            migrationBuilder.DropForeignKey(
                name: "FK_StockPostings_StockPostings_ReversesPostingId",
                table: "StockPostings");

            migrationBuilder.DropTable(
                name: "StockCounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockRequestLine_Quantity",
                table: "StockRequestLines");

            migrationBuilder.DropIndex(
                name: "IX_StockPostings_FulfillmentId",
                table: "StockPostings");

            migrationBuilder.DropIndex(
                name: "IX_StockPostings_ReversesPostingId",
                table: "StockPostings");

            migrationBuilder.DropIndex(
                name: "IX_StockPostings_StockCountId",
                table: "StockPostings");

            migrationBuilder.DropIndex(
                name: "IX_StockPostings_StockRequestId",
                table: "StockPostings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Posting_Source",
                table: "StockPostings");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_StockPostingId_LeaseUnitId_ItemId",
                table: "StockLedger");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_StockPostingId_RequestLineId",
                table: "StockLedger");

            migrationBuilder.DropColumn(
                name: "FulfilledQuantity",
                table: "StockRequestLines");

            migrationBuilder.DropColumn(
                name: "FulfillmentId",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "IsFullRequest",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "ReversesPostingId",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "StockCountId",
                table: "StockPostings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "StockBalances");

            migrationBuilder.AlterColumn<Guid>(
                name: "StockRequestId",
                table: "StockPostings",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "RequestLineId",
                table: "StockLedger",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockRequestLine_Quantity",
                table: "StockRequestLines",
                sql: "[Quantity]>0");

            migrationBuilder.CreateIndex(
                name: "IX_StockPostings_StockRequestId",
                table: "StockPostings",
                column: "StockRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_StockPostingId_RequestLineId",
                table: "StockLedger",
                columns: new[] { "StockPostingId", "RequestLineId" },
                unique: true);
        }
    }
}
