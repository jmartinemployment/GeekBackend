using System;
using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260910183000_AddVisualGuidelines")]
public partial class AddVisualGuidelines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_visual_guidelines",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_visual_guidelines", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_visual_guideline_versions",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VisualGuidelineId = table.Column<Guid>(type: "uuid", nullable: false),
                PolicyJson = table.Column<string>(type: "text", nullable: false),
                Locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                VersionNumber = table.Column<int>(type: "integer", nullable: false),
                SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                CanonicalSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LifecycleState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReviewedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                EffectiveFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EffectiveUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_visual_guideline_versions", x => x.Id);
                table.ForeignKey(
                    name: "FK_gcc_v2_visual_guideline_versions_gcc_v2_visual_guidelines_Vi~",
                    column: x => x.VisualGuidelineId,
                    principalSchema: "content_creator_v2",
                    principalTable: "gcc_v2_visual_guidelines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_visual_guideline_versions_CanonicalSha256",
            schema: "content_creator_v2",
            table: "gcc_v2_visual_guideline_versions",
            column: "CanonicalSha256");

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_visual_guideline_versions_VisualGuidelineId_VersionN~",
            schema: "content_creator_v2",
            table: "gcc_v2_visual_guideline_versions",
            columns: new[] { "VisualGuidelineId", "VersionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_gcc_v2_visual_guidelines_OwnerUserId_Name",
            schema: "content_creator_v2",
            table: "gcc_v2_visual_guidelines",
            columns: new[] { "OwnerUserId", "Name" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_visual_guideline_versions",
            schema: "content_creator_v2");

        migrationBuilder.DropTable(
            name: "gcc_v2_visual_guidelines",
            schema: "content_creator_v2");
    }
}
