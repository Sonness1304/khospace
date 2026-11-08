using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseRent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Role = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessCredentials", x => x.Id);
                    table.CheckConstraint("CK_AccessCredential_Kind", "[Kind] IN ('Invitation','Reset')");
                    table.ForeignKey(
                        name: "FK_AccessCredentials_CustomerOrganizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "CustomerOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityDirectoryStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityDirectoryStates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdentityOperationCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityOperationCommands", x => new { x.ActorId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCooldowns",
                columns: table => new
                {
                    AddressHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCooldowns", x => x.AddressHash);
                });

            migrationBuilder.CreateTable(
                name: "CredentialDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ProtectedToken = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CredentialDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CredentialDeliveries_AccessCredentials_CredentialId",
                        column: x => x.CredentialId,
                        principalTable: "AccessCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "IdentityDirectoryStates",
                columns: new[] { "Id", "ChangeId" },
                values: new object[] { 1, new Guid("00000000-0000-0000-0000-000000000000") });

            migrationBuilder.CreateIndex(
                name: "IX_AccessCredentials_Email_Kind_ExpiresAt",
                table: "AccessCredentials",
                columns: new[] { "Email", "Kind", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessCredentials_OrganizationId",
                table: "AccessCredentials",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessCredentials_TokenHash",
                table: "AccessCredentials",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CredentialDeliveries_CredentialId",
                table: "CredentialDeliveries",
                column: "CredentialId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AccessCredentials]) OR EXISTS (SELECT 1 FROM [IdentityOperationCommands])
                    THROW 51011, 'Identity history exists. Restore a validated backup instead of dropping credential history.', 1;
                """);
            migrationBuilder.DropTable(
                name: "CredentialDeliveries");

            migrationBuilder.DropTable(
                name: "IdentityDirectoryStates");

            migrationBuilder.DropTable(
                name: "IdentityOperationCommands");

            migrationBuilder.DropTable(
                name: "RecoveryCooldowns");

            migrationBuilder.DropTable(
                name: "AccessCredentials");
        }
    }
}
