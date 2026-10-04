using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// gcc_generate_jobs: a Generate run on a project, as a row (GA2). At most one running per project,
    /// by a partial unique index; each records the brief revision it read (J7) and the create its drafts
    /// are stored under while drafts are keyed by create.
    /// </summary>
    /// <remarks>
    /// Empty when created. Runs used to live only in GeekAPI's memory, where a redeploy lost them and
    /// nothing stopped two at once. RESTRICT on every key, like every content_creator key.
    /// </remarks>
    public partial class AddGccGenerateJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_generate_jobs",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brief_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    requested_types = table.Column<List<string>>(type: "text[]", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    result_json = table.Column<string>(type: "text", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_generate_jobs", x => x.id);
                    table.CheckConstraint("ck_gcc_generate_jobs_status", "status IN ('running', 'ready', 'failed')");
                    table.ForeignKey(
                        name: "FK_gcc_generate_jobs_gcc_creates_create_id",
                        column: x => x.create_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_creates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_generate_jobs_gcc_project_revisions_brief_revision_id",
                        column: x => x.brief_revision_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_project_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_generate_jobs_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_generate_jobs_brief_revision_id",
                schema: "content_creator",
                table: "gcc_generate_jobs",
                column: "brief_revision_id");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_generate_jobs_create_id",
                schema: "content_creator",
                table: "gcc_generate_jobs",
                column: "create_id");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_generate_jobs_project_id",
                schema: "content_creator",
                table: "gcc_generate_jobs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_gcc_generate_jobs_one_running_per_project",
                schema: "content_creator",
                table: "gcc_generate_jobs",
                column: "project_id",
                unique: true,
                filter: "status = 'running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_generate_jobs",
                schema: "content_creator");
        }
    }
}
