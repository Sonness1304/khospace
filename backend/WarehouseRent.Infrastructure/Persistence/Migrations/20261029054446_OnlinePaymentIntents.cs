using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentIntents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RentalAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    TransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true, collation: "Latin1_General_100_BIN2"),
                    PaymentReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentIntents", x => x.Id);
                    table.CheckConstraint("CK_PaymentIntent_Amount", "[Amount] BETWEEN 1000 AND 50000000");
                    table.CheckConstraint("CK_PaymentIntent_Mode", "[Mode] IN ('Sandbox','Live') AND [Provider] IN ('Vnpay','Momo') AND ([Mode]='Live' OR [PaymentReceiptId] IS NULL)");
                    table.CheckConstraint("CK_PaymentIntent_State", "[State] IN ('Created','Creating','Ready','Unknown','Failed','Paid','ReviewRequired')");
                    table.ForeignKey(
                        name: "FK_PaymentIntents_PaymentReceipts_PaymentReceiptId",
                        column: x => x.PaymentReceiptId,
                        principalTable: "PaymentReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentIntents_RentalAccounts_RentalAccountId",
                        column: x => x.RentalAccountId,
                        principalTable: "RentalAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_PaymentReceiptId",
                table: "PaymentIntents",
                column: "PaymentReceiptId",
                unique: true,
                filter: "[PaymentReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_Mode_MerchantId_TransactionId",
                table: "PaymentIntents",
                columns: new[] { "Provider", "Mode", "MerchantId", "TransactionId" },
                unique: true,
                filter: "[TransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_RentalAccountId_CreatedAt_Id",
                table: "PaymentIntents",
                columns: new[] { "RentalAccountId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [PaymentIntents]) THROW 51016, 'Online payment history exists. Restore a validated backup instead of deleting payment evidence.', 1;");
            migrationBuilder.DropTable(
                name: "PaymentIntents");
        }
    }
}
