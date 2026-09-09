using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddCanvasProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_v2_canvas_projects",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivityJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_canvas_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_canvas_assets",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParentAssetIdsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_canvas_assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_canvas_assets_gcc_v2_canvas_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_canvas_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_canvas_asset_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                    ProvenanceJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_canvas_asset_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_canvas_asset_versions_gcc_v2_canvas_assets_AssetId",
                        column: x => x.AssetId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_canvas_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_canvas_asset_versions_asset_version",
                schema: "content_creator_v2",
                table: "gcc_v2_canvas_asset_versions",
                columns: new[] { "AssetId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_canvas_assets_project_id",
                schema: "content_creator_v2",
                table: "gcc_v2_canvas_assets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_canvas_projects_owner_updated",
                schema: "content_creator_v2",
                table: "gcc_v2_canvas_projects",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_canvas_asset_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_canvas_assets",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_canvas_projects",
                schema: "content_creator_v2");
        }
    }
}
