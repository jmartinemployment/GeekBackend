using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260911200000_AddPipelineStageAttemptTaskRunRefs")]
public partial class AddPipelineStageAttemptTaskRunRefs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TaskRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ArtifactVersionId",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_pipeline_stage_attempts_task_run",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts",
            column: "TaskRunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_gcc_v2_pipeline_stage_attempts_task_run",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts");

        migrationBuilder.DropColumn(
            name: "ArtifactVersionId",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts");

        migrationBuilder.DropColumn(
            name: "TaskRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_pipeline_stage_attempts");
    }
}
