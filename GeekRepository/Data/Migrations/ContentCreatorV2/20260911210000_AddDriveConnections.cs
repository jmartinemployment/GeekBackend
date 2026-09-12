using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260911210000_AddDriveConnections")]
public partial class AddDriveConnections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_drive_connections",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                AccountLabel = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                EncryptedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: false),
                EncryptionIv = table.Column<byte[]>(type: "bytea", nullable: false),
                EncryptionTag = table.Column<byte[]>(type: "bytea", nullable: false),
                ConnectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_drive_connections", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_drive_connections_OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_drive_connections",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_drive_connections_OwnerUserId_AccountLabel",
            schema: "content_creator_v2",
            table: "gcc_v2_drive_connections",
            columns: new[] { "OwnerUserId", "AccountLabel" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_drive_connections",
            schema: "content_creator_v2");
    }
}
