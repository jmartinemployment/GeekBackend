using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// The project holds its brief, keyword, research and site section (decision J2): a project is one
    /// keyword and one brief, so these leave the create.
    /// </summary>
    /// <remarks>
    /// Nullable, and nothing is backfilled here: copying each create's brief onto its project is GR3,
    /// which runs only after its dry-run report has been read and a backup taken. The concurrency token
    /// is Postgres's xmin system column, which every row already has; EF generates an ADD COLUMN for it
    /// that Postgres refuses, so this migration adds only the four real columns.
    /// </remarks>
    public partial class AddGccProjectBrief : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brief_json",
                schema: "content_creator",
                table: "gcc_projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "research_json",
                schema: "content_creator",
                table: "gcc_projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_section_json",
                schema: "content_creator",
                table: "gcc_projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "topic",
                schema: "content_creator",
                table: "gcc_projects",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brief_json",
                schema: "content_creator",
                table: "gcc_projects");

            migrationBuilder.DropColumn(
                name: "research_json",
                schema: "content_creator",
                table: "gcc_projects");

            migrationBuilder.DropColumn(
                name: "site_section_json",
                schema: "content_creator",
                table: "gcc_projects");

            migrationBuilder.DropColumn(
                name: "topic",
                schema: "content_creator",
                table: "gcc_projects");
        }
    }
}
