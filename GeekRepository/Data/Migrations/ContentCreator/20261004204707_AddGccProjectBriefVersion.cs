using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// gcc_projects.brief_version: the brief editor's concurrency token, a counter moved only by a brief
    /// Save. It replaces the row's xmin for the brief, so saving the Profile no longer makes an open brief
    /// stale. 0 on every existing row, the version before any Save.
    /// </summary>
    public partial class AddGccProjectBriefVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "brief_version",
                schema: "content_creator",
                table: "gcc_projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brief_version",
                schema: "content_creator",
                table: "gcc_projects");
        }
    }
}
