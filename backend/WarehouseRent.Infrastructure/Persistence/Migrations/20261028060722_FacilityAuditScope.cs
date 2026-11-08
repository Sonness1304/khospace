using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacilityAuditScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacilityId",
                table: "SecurityAudits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAudits_FacilityId_At",
                table: "SecurityAudits",
                columns: new[] { "FacilityId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SecurityAudits_FacilityId_At",
                table: "SecurityAudits");

            migrationBuilder.DropColumn(
                name: "FacilityId",
                table: "SecurityAudits");
        }
    }
}
