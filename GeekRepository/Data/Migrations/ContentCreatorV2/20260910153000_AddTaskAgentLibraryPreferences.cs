using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260910153000_AddTaskAgentLibraryPreferences")]
public partial class AddTaskAgentLibraryPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_task_agent_library_preferences",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FavoritesJson = table.Column<string>(type: "text", nullable: false),
                SavedConfigsJson = table.Column<string>(type: "text", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_task_agent_library_preferences", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_task_agent_library_preferences_OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_task_agent_library_preferences",
            column: "OwnerUserId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_task_agent_library_preferences",
            schema: "content_creator_v2");
    }
}
