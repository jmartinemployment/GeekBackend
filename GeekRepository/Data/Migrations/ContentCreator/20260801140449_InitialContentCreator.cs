using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <inheritdoc />
    public partial class InitialContentCreator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content_creator");

            migrationBuilder.CreateTable(
                name: "gcc_approval_events",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_approval_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_artifact_versions",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    body_json = table.Column<string>(type: "text", nullable: false),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_artifact_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_artifacts",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_artifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_creates",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartingContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Topic = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    SiteAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    SiteSectionJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_creates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_approval_events_artifact_version_id",
                schema: "content_creator",
                table: "gcc_approval_events",
                column: "ArtifactVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_artifact_versions_artifact_id",
                schema: "content_creator",
                table: "gcc_artifact_versions",
                column: "ArtifactId");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_artifacts_create_id",
                schema: "content_creator",
                table: "gcc_artifacts",
                column: "CreateId");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_creates_client_id",
                schema: "content_creator",
                table: "gcc_creates",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_creates_owner_user_id",
                schema: "content_creator",
                table: "gcc_creates",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_approval_events",
                schema: "content_creator");

            migrationBuilder.DropTable(
                name: "gcc_artifact_versions",
                schema: "content_creator");

            migrationBuilder.DropTable(
                name: "gcc_artifacts",
                schema: "content_creator");

            migrationBuilder.DropTable(
                name: "gcc_creates",
                schema: "content_creator");
        }
    }
}
