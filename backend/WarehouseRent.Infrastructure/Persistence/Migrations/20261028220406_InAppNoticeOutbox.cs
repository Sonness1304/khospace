using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InAppNoticeOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoticeOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    BusinessKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeOutbox_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoticeOutbox_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InAppNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboxId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InAppNotices_NoticeOutbox_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "NoticeOutbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotices_OrganizationId_FacilityId_CreatedAt",
                table: "InAppNotices",
                columns: new[] { "OrganizationId", "FacilityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotices_OutboxId",
                table: "InAppNotices",
                column: "OutboxId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeOutbox_BusinessKey",
                table: "NoticeOutbox",
                column: "BusinessKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeOutbox_FacilityId_DeliveredAt_CreatedAt",
                table: "NoticeOutbox",
                columns: new[] { "FacilityId", "DeliveredAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeOutbox_OrganizationId",
                table: "NoticeOutbox",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [NoticeOutbox]) THROW 51014, 'Notice history exists. Restore a validated backup instead of dropping outbox.', 1;");
            migrationBuilder.DropTable(
                name: "InAppNotices");

            migrationBuilder.DropTable(
                name: "NoticeOutbox");
        }
    }
}
