using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddAgentObjectivesPolicyAndFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelPolicyProfile",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "ModelPolicyVersion",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "content-model-policy.v1");

            migrationBuilder.AddColumn<string>(
                name: "Objective",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE content_creator_v2.gcc_v2_agent_versions
                SET "Objective" = "Instructions"
                WHERE "Objective" = '';
                """);

            migrationBuilder.CreateTable(
                name: "gcc_v2_agent_review_findings",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Rule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewerRationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisposedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_agent_review_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_agent_review_findings_gcc_v2_agent_versions_AgentVer~",
                        column: x => x.AgentVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_agent_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_agent_finding_version_disposition",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_review_findings",
                columns: new[] { "AgentVersionId", "Disposition" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_agent_review_findings",
                schema: "content_creator_v2");

            migrationBuilder.DropColumn(
                name: "ModelPolicyProfile",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions");

            migrationBuilder.DropColumn(
                name: "ModelPolicyVersion",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions");

            migrationBuilder.DropColumn(
                name: "Objective",
                schema: "content_creator_v2",
                table: "gcc_v2_agent_versions");
        }
    }
}
