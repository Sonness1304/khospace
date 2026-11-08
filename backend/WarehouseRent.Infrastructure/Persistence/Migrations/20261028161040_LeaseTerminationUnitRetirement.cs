using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeaseTerminationUnitRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "RentableUnits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetiredBy",
                table: "RentableUnits",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetirementReason",
                table: "RentableUnits",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TerminatedAt",
                table: "Leases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminatedBy",
                table: "Leases",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                table: "Leases",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "RentableUnits");

            migrationBuilder.DropColumn(
                name: "RetiredBy",
                table: "RentableUnits");

            migrationBuilder.DropColumn(
                name: "RetirementReason",
                table: "RentableUnits");

            migrationBuilder.DropColumn(
                name: "TerminatedAt",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "TerminatedBy",
                table: "Leases");

            migrationBuilder.DropColumn(
                name: "TerminationReason",
                table: "Leases");
        }
    }
}
