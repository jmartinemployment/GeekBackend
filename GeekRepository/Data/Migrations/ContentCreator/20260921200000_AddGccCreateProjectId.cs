using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A create belongs to a project, so generation can reach the partner and competitor URLs the
    /// project owns.
    /// </summary>
    /// <remarks>
    /// Partner and competitor URLs live on gcc_projects. gcc_creates carried client_id and
    /// SiteAnalysisId only, and the single create-to-project join was gcc_deliverables, whose rows
    /// are written by an explicit operator action rather than at create time. So at generation time
    /// a create typically had no project, and retrieval had no way to know which hosts to query —
    /// the structural reason drafts never cited or quoted partners and tools. client_id does not
    /// substitute: one client may run several projects over different sites.
    ///
    /// Nullable because existing creates predate the link. A create without a project cannot
    /// resolve partner/competitor evidence, and the content types that require it must refuse
    /// rather than generate ungrounded. RESTRICT matches every other content_creator foreign key:
    /// a project with creates attached is not silently removable.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921200000_AddGccCreateProjectId")]
    public partial class AddGccCreateProjectId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "project_id",
                schema: "content_creator",
                table: "gcc_creates",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_creates_project_id",
                schema: "content_creator",
                table: "gcc_creates",
                column: "project_id");

            migrationBuilder.AddForeignKey(
                name: "FK_gcc_creates_gcc_projects_project_id",
                schema: "content_creator",
                table: "gcc_creates",
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
                name: "FK_gcc_creates_gcc_projects_project_id",
                schema: "content_creator",
                table: "gcc_creates");

            migrationBuilder.DropIndex(
                name: "ix_gcc_creates_project_id",
                schema: "content_creator",
                table: "gcc_creates");

            migrationBuilder.DropColumn(
                name: "project_id",
                schema: "content_creator",
                table: "gcc_creates");
        }
    }
}
