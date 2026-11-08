using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerItemBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemBarcodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CanonicalValue = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBarcodes", x => x.Id);
                    table.CheckConstraint("CK_Barcode_KindValue", "[Kind] IN ('GTIN','Internal') AND LEN([CanonicalValue]) BETWEEN 1 AND 64");
                    table.ForeignKey(
                        name: "FK_ItemBarcodes_Items_OrganizationId_ItemId",
                        columns: x => new { x.OrganizationId, x.ItemId },
                        principalTable: "Items",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBarcodes_OrganizationId_ItemId",
                table: "ItemBarcodes",
                columns: new[] { "OrganizationId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemBarcodes_OrganizationId_Kind_CanonicalValue",
                table: "ItemBarcodes",
                columns: new[] { "OrganizationId", "Kind", "CanonicalValue" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [ItemBarcodes]) THROW 51015, 'Barcode history exists. Restore a validated backup instead of deleting mappings.', 1;");
            migrationBuilder.DropTable(
                name: "ItemBarcodes");
        }
    }
}
