using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Every Save of a project's brief is kept (decision J5): gcc_project_revisions, one row per Save
    /// that changed something. Rows are never altered.
    /// </summary>
    /// <remarks>
    /// Empty when created. Two kinds: manual, a Save click, and backfill, which GR3 writes when it copies
    /// each create's brief here as the project's first revision -- and GR3 runs only after its dry-run
    /// report has been read and a backup taken. RESTRICT on the project key, like every content_creator
    /// key.
    /// </remarks>
    public partial class AddGccProjectRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_project_revisions",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    brief_json = table.Column<string>(type: "text", nullable: true),
                    topic = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    saved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_project_revisions", x => x.id);
                    table.CheckConstraint("ck_gcc_project_revisions_kind", "kind IN ('manual', 'backfill')");
                    table.ForeignKey(
                        name: "FK_gcc_project_revisions_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_project_revisions_project_id_saved_at",
                schema: "content_creator",
                table: "gcc_project_revisions",
                columns: new[] { "project_id", "saved_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_project_revisions",
                schema: "content_creator");
        }
    }
}
