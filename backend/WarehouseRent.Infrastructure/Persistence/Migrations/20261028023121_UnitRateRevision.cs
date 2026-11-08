using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnitRateRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RateRevision",
                table: "RentableUnits",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RateRevision",
                table: "RentableUnits");
        }
    }
}
