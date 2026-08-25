using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

public partial class AddJobsEventsStages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_jobs",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BriefId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "plan"),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                ResultJson = table.Column<string>(type: "text", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                ClaimedByInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TokensUsed = table.Column<int>(type: "integer", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_jobs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_job_events",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: false),
                Seq = table.Column<int>(type: "integer", nullable: false),
                Type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_job_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_stage_results",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: false),
                Stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SectionKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                OutputJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                TokensUsed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_stage_results", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_briefs",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                TargetKeyword = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RawBriefJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                FrozenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_briefs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_jobs_owner_user_id",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs",
            column: "OwnerUserId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_jobs_create_id",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs",
            column: "CreateId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_jobs_status_lease_until_utc",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs",
            columns: new[] { "Status", "LeaseUntilUtc" });

        migrationBuilder.CreateIndex(
            name: "ux_gcc_v2_job_events_job_id_seq",
            schema: "content_creator_v2",
            table: "gcc_v2_job_events",
            columns: new[] { "JobId", "Seq" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_stage_results_job_id",
            schema: "content_creator_v2",
            table: "gcc_v2_stage_results",
            column: "JobId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_briefs_create_id",
            schema: "content_creator_v2",
            table: "gcc_v2_briefs",
            column: "CreateId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_job_events", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_stage_results", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_briefs", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_jobs", schema: "content_creator_v2");
    }
}
