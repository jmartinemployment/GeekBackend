using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// gcc_version_evidence (D2): what each generated version was made from -- the model calls, the
    /// drafts a retry replaced, the research, passages, quote candidates, readiness verdicts and
    /// retrieval queries -- one row per version, keyed to its project as well.
    /// </summary>
    /// <remarks>
    /// Empty when created; A10 writes it. Every diagnosis of a bad draft before this was archaeology
    /// on a 60-character excerpt. RESTRICT on both keys, like every content_creator key.
    /// </remarks>
    public partial class AddGccVersionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_version_evidence",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model_ids_json = table.Column<string>(type: "text", nullable: false),
                    calls_json = table.Column<string>(type: "text", nullable: false),
                    discarded_drafts_json = table.Column<string>(type: "text", nullable: true),
                    research_json = table.Column<string>(type: "text", nullable: true),
                    passages_json = table.Column<string>(type: "text", nullable: true),
                    quote_candidates_json = table.Column<string>(type: "text", nullable: true),
                    readiness_json = table.Column<string>(type: "text", nullable: true),
                    bank_digests_json = table.Column<string>(type: "text", nullable: true),
                    rag_queries_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_version_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_gcc_version_evidence_gcc_artifact_versions_version_id",
                        column: x => x.version_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_artifact_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_version_evidence_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_version_evidence_project_id",
                schema: "content_creator",
                table: "gcc_version_evidence",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_gcc_version_evidence_version_id",
                schema: "content_creator",
                table: "gcc_version_evidence",
                column: "version_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_version_evidence",
                schema: "content_creator");
        }
    }
}
