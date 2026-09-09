using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddGovernedSkillRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_audit_events",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceIp = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    BeforeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AfterState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_packages",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceRepository = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourcePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Publisher = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LifecycleState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsFirstParty = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeprecatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SemanticVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImmutableGitRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ManifestDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    License = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Compatibility = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reviewer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    SupersedesVersionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_skill_versions_gcc_v2_skill_packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_skill_packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_applicabilities",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ConflictsJson = table.Column<string>(type: "text", nullable: false),
                    RequiredToolsJson = table.Column<string>(type: "text", nullable: false),
                    ActivationMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_applicabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_skill_applicabilities_gcc_v2_skill_versions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_skill_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_files",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_skill_files_gcc_v2_skill_versions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_skill_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_skill_review_findings",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Scanner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Rule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Line = table.Column<int>(type: "integer", nullable: true),
                    Message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewerRationale = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    PermanentRejection = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_skill_review_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_skill_review_findings_gcc_v2_skill_versions_VersionId",
                        column: x => x.VersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_skill_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_skill_applicability_scope",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_applicabilities",
                columns: new[] { "VersionId", "Stage", "ContentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_skill_audit_package_created",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_audit_events",
                columns: new[] { "PackageId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_skill_files_version_path",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_files",
                columns: new[] { "VersionId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_skill_packages_slug",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_packages",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_skill_review_findings_VersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_review_findings",
                column: "VersionId");

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_skill_versions_package_semver",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_versions",
                columns: new[] { "PackageId", "SemanticVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_skill_versions_package_sha256",
                schema: "content_creator_v2",
                table: "gcc_v2_skill_versions",
                column: "PackageSha256",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_skill_applicabilities",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_skill_audit_events",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_skill_files",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_skill_review_findings",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_skill_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_skill_packages",
                schema: "content_creator_v2");

        }
    }
}
