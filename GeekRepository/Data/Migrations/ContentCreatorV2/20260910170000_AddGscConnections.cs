using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260910170000_AddGscConnections")]
public partial class AddGscConnections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_gsc_connections",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                EncryptedRefreshToken = table.Column<byte[]>(type: "bytea", nullable: false),
                EncryptionIv = table.Column<byte[]>(type: "bytea", nullable: false),
                EncryptionTag = table.Column<byte[]>(type: "bytea", nullable: false),
                ConnectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_gsc_connections", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_gsc_connections_OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_gsc_connections",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_gsc_connections_OwnerUserId_SiteUrl",
            schema: "content_creator_v2",
            table: "gcc_v2_gsc_connections",
            columns: new[] { "OwnerUserId", "SiteUrl" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_gsc_connections",
            schema: "content_creator_v2");
    }
}
