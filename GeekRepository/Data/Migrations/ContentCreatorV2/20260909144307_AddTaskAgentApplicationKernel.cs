using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddTaskAgentApplicationKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_v2_task_agent_definitions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_agent_definitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_agent_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkflowGroup = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FacetsJson = table.Column<string>(type: "text", nullable: false),
                    InputSchemaJson = table.Column<string>(type: "text", nullable: false),
                    InputSchemaDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OutputSchemaJson = table.Column<string>(type: "text", nullable: false),
                    OutputSchemaDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkflowJson = table.Column<string>(type: "text", nullable: false),
                    WorkflowDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextPolicyJson = table.Column<string>(type: "text", nullable: false),
                    ContextPolicyDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResultRendererJson = table.Column<string>(type: "text", nullable: false),
                    ResultRendererDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompatibleArtifactTypesJson = table.Column<string>(type: "text", nullable: false),
                    AllowedToolsJson = table.Column<string>(type: "text", nullable: false),
                    AllowedModelsJson = table.Column<string>(type: "text", nullable: false),
                    SkillVersionIdsJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationThresholdsJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationThresholdsDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeprecatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_agent_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_agent_versions_gcc_v2_task_agent_definitions_De~",
                        column: x => x.DefinitionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_agent_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_runs",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TaskAgentDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskAgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskAgentVersionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    InputDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContextManifestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContextManifestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ModelSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    ModelSnapshotDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BudgetSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    BudgetSnapshotDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    SourceSnapshotDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Phase = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RecoveryCount = table.Column<int>(type: "integer", nullable: false),
                    RootRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetryOfRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedByInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminalError = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_runs_gcc_v2_run_context_manifests_ContextManife~",
                        column: x => x.ContextManifestId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_run_context_manifests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_runs_gcc_v2_task_agent_definitions_TaskAgentDef~",
                        column: x => x.TaskAgentDefinitionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_agent_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_runs_gcc_v2_task_agent_versions_TaskAgentVersio~",
                        column: x => x.TaskAgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_runs_gcc_v2_task_runs_RetryOfRunId",
                        column: x => x.RetryOfRunId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_artifacts",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_artifacts_gcc_v2_task_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_run_events",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_run_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_run_events_gcc_v2_task_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_artifact_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CitationsJson = table.Column<string>(type: "text", nullable: false),
                    ValidationState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidationJson = table.Column<string>(type: "text", nullable: false),
                    Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByActor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_artifact_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_artifact_versions_gcc_v2_task_artifacts_Artifac~",
                        column: x => x.ArtifactId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_artifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_task_artifact_lineage",
                schema: "content_creator_v2",
                columns: table => new
                {
                    ParentArtifactVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChildArtifactVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Relationship = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_task_artifact_lineage", x => new { x.ParentArtifactVersionId, x.ChildArtifactVersionId });
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_artifact_lineage_gcc_v2_task_artifact_versions_~",
                        column: x => x.ChildArtifactVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_artifact_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_task_artifact_lineage_gcc_v2_task_artifact_versions~1",
                        column: x => x.ParentArtifactVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_task_artifact_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_agent_definitions_CapabilityId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_agent_definitions",
                column: "CapabilityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_agent_versions_DefinitionId_SemanticVersion",
                schema: "content_creator_v2",
                table: "gcc_v2_task_agent_versions",
                columns: new[] { "DefinitionId", "SemanticVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_agent_versions_VersionDigest",
                schema: "content_creator_v2",
                table: "gcc_v2_task_agent_versions",
                column: "VersionDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_artifact_lineage_ChildArtifactVersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_artifact_lineage",
                column: "ChildArtifactVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_artifact_versions_ArtifactId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_task_artifact_versions",
                columns: new[] { "ArtifactId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_artifact_versions_Digest",
                schema: "content_creator_v2",
                table: "gcc_v2_task_artifact_versions",
                column: "Digest");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_artifacts_OwnerUserId_RunId_ArtifactType",
                schema: "content_creator_v2",
                table: "gcc_v2_task_artifacts",
                columns: new[] { "OwnerUserId", "RunId", "ArtifactType" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_artifacts_RunId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_artifacts",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_run_events_RunId_Seq",
                schema: "content_creator_v2",
                table: "gcc_v2_task_run_events",
                columns: new[] { "RunId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_ContextManifestId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                column: "ContextManifestId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_OwnerUserId_CreatedAtUtc",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_RetryOfRunId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                column: "RetryOfRunId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_RootRunId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                column: "RootRunId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_Status_LeaseUntilUtc",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_TaskAgentDefinitionId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                column: "TaskAgentDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_task_runs_TaskAgentVersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_task_runs",
                column: "TaskAgentVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_task_artifact_lineage",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_run_events",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_artifact_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_artifacts",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_runs",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_agent_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_task_agent_definitions",
                schema: "content_creator_v2");
        }
    }
}
