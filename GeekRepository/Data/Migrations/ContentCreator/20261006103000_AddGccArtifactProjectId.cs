using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Drafts keyed to the project (fix-project-persistence GR4): <c>gcc_artifacts.project_id</c>,
    /// filled from each page's create, indexed, and RESTRICT to <c>gcc_projects</c> like every
    /// content_creator key.
    /// </summary>
    /// <remarks>
    /// Until now a project's pages were found through its creates, and a new page was stored under the
    /// create the run named. The project is the unit (J1), so the page carries the project itself. The
    /// column is nullable because a page under a create that was never assigned a project belongs to no
    /// project, and such creates are left alone and reported, never deleted (J11); it becomes NOT NULL
    /// when the create table goes (GR6). <c>CreateId</c> stays until then.
    /// </remarks>
    public partial class AddGccArtifactProjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                schema: "content_creator",
                table: "gcc_artifacts",
                type: "uuid",
                nullable: true);

            // Every page stored under a create that has a project is that project's page. A page under
            // an unassigned create is left null, as the create is left unassigned.
            migrationBuilder.Sql("""
                UPDATE content_creator.gcc_artifacts AS a
                SET project_id = c.project_id
                FROM content_creator.gcc_creates AS c
                WHERE c."Id" = a."CreateId"
                  AND c.project_id IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_artifacts_project_id",
                schema: "content_creator",
                table: "gcc_artifacts",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_gcc_artifacts_gcc_projects_project_id",
                schema: "content_creator",
                table: "gcc_artifacts",
                column: "project_id",
                principalSchema: "content_creator",
                principalTable: "gcc_projects",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gcc_artifacts_gcc_projects_project_id",
                schema: "content_creator",
                table: "gcc_artifacts");

            migrationBuilder.DropIndex(
                name: "ix_gcc_artifacts_project_id",
                schema: "content_creator",
                table: "gcc_artifacts");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "content_creator",
                table: "gcc_artifacts");
        }
    }
}
