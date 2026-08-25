using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

public partial class InitialContentCreatorV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "content_creator_v2");

        migrationBuilder.CreateTable(
            name: "gcc_v2_creates",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_creates", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_creates_owner_user_id",
            schema: "content_creator_v2",
            table: "gcc_v2_creates",
            column: "OwnerUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_creates", schema: "content_creator_v2");
    }
}
