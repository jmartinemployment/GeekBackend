using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// gcc_generate_job_events: a Generate run's record, one row per thing that happened -- what it was
    /// grounded on, each model call with what was sent and what came back, each guard verdict with the
    /// draft it judged, each batch shortfall, each piece's outcome, and how the run ended.
    /// </summary>
    /// <remarks>
    /// Jeff, 2026-10-06, after three refused runs with nothing to read but a log line: "implement
    /// detailed comprehensive logging to properly diagnose errors". Written as the run goes, so a run a
    /// redeploy cuts off still has its record up to the cut. Never altered. RESTRICT to the job, like
    /// every content_creator key.
    /// </remarks>
    public partial class AddGccGenerateJobEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_generate_job_events",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    piece = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    payload_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_generate_job_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_gcc_generate_job_events_gcc_generate_jobs_job_id",
                        column: x => x.job_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_generate_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_generate_job_events_job_id_seq",
                schema: "content_creator",
                table: "gcc_generate_job_events",
                columns: new[] { "job_id", "seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_generate_job_events",
                schema: "content_creator");
        }
    }
}
