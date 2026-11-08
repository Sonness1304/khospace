using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenewalAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RenewalAcceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedLeaseVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExpectedQuoteVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AcceptedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RenewalAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RenewalAcceptances_Leases_LeaseId",
                        column: x => x.LeaseId,
                        principalTable: "Leases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RenewalAcceptances_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RenewalAcceptances_LeaseId_QuoteId",
                table: "RenewalAcceptances",
                columns: new[] { "LeaseId", "QuoteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenewalAcceptances_QuoteId",
                table: "RenewalAcceptances",
                column: "QuoteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RenewalAcceptances");
        }
    }
}
