using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SePayBankTransferIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentIntent_Mode",
                table: "PaymentIntents");

            migrationBuilder.AddColumn<string>(
                name: "BankTransferReference",
                table: "PaymentIntents",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "PaymentIntents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_BankTransferReference",
                table: "PaymentIntents",
                column: "BankTransferReference",
                unique: true,
                filter: "[BankTransferReference] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentIntent_BankTransfer",
                table: "PaymentIntents",
                sql: "([Provider]='SePay' AND [Method]='SePayQr' AND [BankTransferReference] IS NOT NULL AND LEN([BankTransferReference])=15 AND LEFT([BankTransferReference],5)='SEVQR' AND [BankTransferReference] NOT LIKE '%[^A-Z0-9]%' COLLATE Latin1_General_100_BIN2 AND [ExpiresAt] IS NOT NULL AND [ExpiresAt]>[CreatedAt] AND LEN([MerchantId])=64 AND [MerchantId] NOT LIKE '%[^0-9A-F]%' COLLATE Latin1_General_100_BIN2 AND [CheckoutUrl] IS NULL) OR ([Provider]<>'SePay' AND [BankTransferReference] IS NULL AND [ExpiresAt] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentIntent_Mode",
                table: "PaymentIntents",
                sql: "[Mode] IN ('Sandbox','Live') AND [Provider] IN ('Vnpay','Momo','SePay') AND ([Mode]='Live' OR [PaymentReceiptId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [PaymentIntents] WITH (TABLOCKX,HOLDLOCK) WHERE [Provider]='SePay' OR [BankTransferReference] IS NOT NULL OR [ExpiresAt] IS NOT NULL) THROW 51017, 'Cannot remove durable SePay payment evidence.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_BankTransferReference",
                table: "PaymentIntents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentIntent_BankTransfer",
                table: "PaymentIntents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentIntent_Mode",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "BankTransferReference",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "PaymentIntents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentIntent_Mode",
                table: "PaymentIntents",
                sql: "[Mode] IN ('Sandbox','Live') AND [Provider] IN ('Vnpay','Momo') AND ([Mode]='Live' OR [PaymentReceiptId] IS NULL)");
        }
    }
}
