using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

public partial class AddAiVisibilitySnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_ai_visibility_snapshots",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: true),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Score = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                ReportJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_ai_visibility_snapshots", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_ai_visibility_snapshots_create_id",
            schema: "content_creator_v2",
            table: "gcc_v2_ai_visibility_snapshots",
            column: "CreateId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_ai_visibility_snapshots_job_id",
            schema: "content_creator_v2",
            table: "gcc_v2_ai_visibility_snapshots",
            column: "JobId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_ai_visibility_snapshots_owner_user_id",
            schema: "content_creator_v2",
            table: "gcc_v2_ai_visibility_snapshots",
            column: "OwnerUserId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_ai_visibility_snapshots", schema: "content_creator_v2");
    }
}
