using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddDurableAgentTestRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_test_runs",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Scenario = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    QueuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedByInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecoveryCount = table.Column<int>(type: "integer", nullable: false),
                    CancellationRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_test_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_test_runs_gcc_v2_agent_versions_AgentVersionId",
                        column: x => x.AgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_agent_test_status_lease",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_test_runs",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_agent_test_version_queued",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_test_runs",
                columns: new[] { "AgentVersionId", "QueuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_agent_test_runs",
                schema: "content_creator_v2");
        }
    }
}
