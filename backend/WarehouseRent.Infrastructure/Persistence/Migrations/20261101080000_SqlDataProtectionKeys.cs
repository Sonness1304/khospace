using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace WarehouseRent.Infrastructure.Persistence.Migrations;

// Infrastructure-owned key ring is deliberately independent of EF's business entity model.
[DbContext(typeof(AppDbContext))]
[Migration("20261101080000_SqlDataProtectionKeys")]
public sealed class SqlDataProtectionKeys : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.DataProtectionKeys (
                Id uniqueidentifier NOT NULL CONSTRAINT PK_DataProtectionKeys PRIMARY KEY,
                FriendlyName nvarchar(200) NOT NULL,
                Xml nvarchar(max) NOT NULL,
                CreatedAt datetime2 NOT NULL,
                CONSTRAINT CK_DataProtectionKeys_Bounds CHECK (
                    DATALENGTH(FriendlyName) BETWEEN 2 AND 400 AND DATALENGTH(Xml) BETWEEN 2 AND 65536)
            );
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER dbo.TR_DataProtectionKeys_AppendOnly ON dbo.DataProtectionKeys
            INSTEAD OF UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51000, 'Data protection key records are append-only.', 1;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        IF EXISTS (SELECT 1 FROM dbo.DataProtectionKeys WITH (TABLOCKX, HOLDLOCK))
            THROW 51001, 'Cannot remove a populated data protection key ring. Preserve the encrypted key backup.', 1;
        DROP TABLE dbo.DataProtectionKeys;
        """);
}
