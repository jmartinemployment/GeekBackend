using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddSpecialistAgentTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentTeamSnapshotDigest",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentTeamSnapshotJson",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentTeamSnapshotKeyId",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentTeamSnapshotSignature",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedAgentVersionIdsJson",
                schema: "content_creator_v2",
                table: "gcc_v2_creates",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_audit_events",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AfterState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DetailJson = table.Column<string>(type: "text", nullable: true),
                    SourceIp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_agents",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsFirstParty = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ContentTypesJson = table.Column<string>(type: "text", nullable: false),
                    AllowedToolsJson = table.Column<string>(type: "text", nullable: false),
                    AllowedModelsJson = table.Column<string>(type: "text", nullable: false),
                    VersionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeprecatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reviewer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    TestResultJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_versions_gcc_v2_agents_AgentId",
                        column: x => x.AgentId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_stage_participation",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_stage_participation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_stage_participation_gcc_v2_agent_versions_Agen~",
                        column: x => x.AgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_version_skills",
                schema: "content_creator_v2",
                columns: table => new
                {
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_version_skills", x => new { x.AgentVersionId, x.SkillVersionId });
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_version_skills_gcc_v2_agent_versions_AgentVers~",
                        column: x => x.AgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_version_skills_gcc_v2_skill_versions_SkillVers~",
                        column: x => x.SkillVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_skill_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_job_agent_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_job_agent_versions", x => new { x.JobId, x.AgentVersionId });
                    table.ForeignKey(
                        name: "FK_gcc_v2_job_agent_versions_gcc_v2_agent_versions_AgentVersio~",
                        column: x => x.AgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_job_agent_versions_gcc_v2_jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_agent_audit_agent_created",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_audit_events",
                columns: new[] { "AgentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_agent_stage_role",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_stage_participation",
                columns: new[] { "AgentVersionId", "Stage", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_agent_version_skills_SkillVersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_version_skills",
                column: "SkillVersionId");

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_agent_versions_agent_semver",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions",
                columns: new[] { "AgentId", "SemanticVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_agent_versions_digest",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions",
                column: "VersionDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_agents_slug",
                schema: "content_creator_v2",
                table: "gcc_v2_agents",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_job_agent_versions_AgentVersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_job_agent_versions",
                column: "AgentVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_agent_audit_events",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_agent_stage_participation",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_agent_version_skills",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_job_agent_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_agent_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_agents",
                schema: "content_creator_v2");

            migrationBuilder.DropColumn(
                name: "AgentTeamSnapshotDigest",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs");

            migrationBuilder.DropColumn(
                name: "AgentTeamSnapshotJson",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs");

            migrationBuilder.DropColumn(
                name: "AgentTeamSnapshotKeyId",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs");

            migrationBuilder.DropColumn(
                name: "AgentTeamSnapshotSignature",
                schema: "content_creator_v2",
                table: "gcc_v2_jobs");

            migrationBuilder.DropColumn(
                name: "SelectedAgentVersionIdsJson",
                schema: "content_creator_v2",
                table: "gcc_v2_creates");
        }
    }
}
