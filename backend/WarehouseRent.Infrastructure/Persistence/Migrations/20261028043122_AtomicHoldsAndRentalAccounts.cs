using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AtomicHoldsAndRentalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HeldAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.CheckConstraint("CK_Booking_Dates", "[StartDate]<[EndDate] AND [HeldAt]<[ExpiresAt]");
                    table.CheckConstraint("CK_Booking_State", "[State] IN ('Held','Confirmed','Cancelled','Expired')");
                    table.ForeignKey(
                        name: "FK_Bookings_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Bookings_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingLines", x => x.Id);
                    table.UniqueConstraint("AK_BookingLines_UnitId_Id", x => new { x.UnitId, x.Id });
                    table.CheckConstraint("CK_BookingLine_Dates", "[StartDate]<[EndDate]");
                    table.ForeignKey(
                        name: "FK_BookingLines_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookingLines_RentableUnits_UnitId",
                        column: x => x.UnitId,
                        principalTable: "RentableUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RentalAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredDeposit = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RefundNeeded = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalAccounts", x => x.Id);
                    table.CheckConstraint("CK_Account_Deposit", "[RequiredDeposit]>=0");
                    table.ForeignKey(
                        name: "FK_RentalAccounts_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RentalAccounts_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpaceAllocations_UnitId_BookingLineId",
                table: "SpaceAllocations",
                columns: new[] { "UnitId", "BookingLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingLines_BookingId_UnitId",
                table: "BookingLines",
                columns: new[] { "BookingId", "UnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_FacilityId",
                table: "Bookings",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_OrganizationId_HeldAt",
                table: "Bookings",
                columns: new[] { "OrganizationId", "HeldAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_QuoteId_State",
                table: "Bookings",
                columns: new[] { "QuoteId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_RentalAccounts_BookingId",
                table: "RentalAccounts",
                column: "BookingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RentalAccounts_OrganizationId",
                table: "RentalAccounts",
                column: "OrganizationId");

            migrationBuilder.AddForeignKey(
                name: "FK_SpaceAllocations_BookingLines_UnitId_BookingLineId",
                table: "SpaceAllocations",
                columns: new[] { "UnitId", "BookingLineId" },
                principalTable: "BookingLines",
                principalColumns: new[] { "UnitId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SpaceAllocations_BookingLines_UnitId_BookingLineId",
                table: "SpaceAllocations");

            migrationBuilder.DropTable(
                name: "BookingLines");

            migrationBuilder.DropTable(
                name: "RentalAccounts");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_SpaceAllocations_UnitId_BookingLineId",
                table: "SpaceAllocations");
        }
    }
}
