using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260910200000_AddContentPipelines")]
public partial class AddContentPipelines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_pipeline_definitions",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                VersionNumber = table.Column<int>(type: "integer", nullable: false),
                Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StagesJson = table.Column<string>(type: "text", nullable: false),
                PolicyJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_pipeline_definitions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_pipeline_runs",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PipelineDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                DefinitionVersionNumber = table.Column<int>(type: "integer", nullable: false),
                DefinitionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ActorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                InputJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                HistoryJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PausedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_pipeline_runs", x => x.Id);
                table.ForeignKey(
                    name: "FK_gcc_v2_pipeline_runs_gcc_v2_pipeline_definitions_PipelineDe~",
                    column: x => x.PipelineDefinitionId,
                    principalSchema: "content_creator_v2",
                    principalTable: "gcc_v2_pipeline_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_pipeline_work_items",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PipelineRunId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkItemIndex = table.Column<int>(type: "integer", nullable: false),
                InputJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Error = table.Column<string>(type: "text", nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_pipeline_work_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_gcc_v2_pipeline_work_items_gcc_v2_pipeline_runs_PipelineRun~",
                    column: x => x.PipelineRunId,
                    principalSchema: "content_creator_v2",
                    principalTable: "gcc_v2_pipeline_runs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_pipeline_stage_attempts",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PipelineWorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                StageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LifecycleStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CapabilityId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Handoff = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OutputJson = table.Column<string>(type: "text", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_pipeline_stage_attempts", x => x.Id);
                table.ForeignKey(
                    name: "FK_gcc_v2_pipeline_stage_attempts_gcc_v2_pipeline_work_items_P~",
                    column: x => x.PipelineWorkItemId,
                    principalSchema: "content_creator_v2",
                    principalTable: "gcc_v2_pipeline_work_items",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_pipeline_definitions_owner_updated",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_definitions",
            columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_pipeline_runs_definition_started",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_runs",
            columns: new[] { "PipelineDefinitionId", "StartedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ux_gcc_v2_pipeline_work_items_run_index",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_work_items",
            columns: new[] { "PipelineRunId", "WorkItemIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_gcc_v2_pipeline_stage_attempts_item_stage_attempt",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts",
            columns: new[] { "PipelineWorkItemId", "StageKey", "AttemptNumber" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_pipeline_stage_attempts", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_pipeline_work_items", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_pipeline_runs", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_pipeline_definitions", schema: "content_creator_v2");
    }
}
