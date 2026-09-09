using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2
{
    /// <inheritdoc />
    public partial class AddGovernedContextLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gcc_v2_briefs_create_id",
                schema: "content_creator_v2",
                table: "gcc_v2_briefs");

            migrationBuilder.AddColumn<string>(
                name: "AcceptedByUserId",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerUserId",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "gcc_v2_audiences",
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
                    table.PrimaryKey("PK_gcc_v2_audiences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_context_audit_events",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AfterState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    DetailJson = table.Column<string>(type: "text", nullable: false),
                    RequestId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_context_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_context_findings",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ReviewerRationale = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DisposedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_context_findings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_context_ingestion_jobs",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ClaimedByInstanceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminalError = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_context_ingestion_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_context_selections",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreateId = table.Column<Guid>(type: "uuid", nullable: true),
                    SelectionJson = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_context_selections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_knowledge_assets",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TagsJson = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_knowledge_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_product_schemas",
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
                    table.PrimaryKey("PK_gcc_v2_product_schemas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_products",
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
                    table.PrimaryKey("PK_gcc_v2_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_run_attachments",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SafeFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ByteSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IngestionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UploadExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetainUntilUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_run_attachments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_run_context_manifests",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CanonicalJson = table.Column<string>(type: "text", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Signature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SigningKeyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResolverIdentity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReplacesManifestId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_run_context_manifests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_run_context_manifests_gcc_v2_jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_style_guides",
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
                    table.PrimaryKey("PK_gcc_v2_style_guides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_audience_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefinitionJson = table.Column<string>(type: "text", nullable: false),
                    Locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProvenanceJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_audience_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_audience_versions_gcc_v2_audiences_AudienceId",
                        column: x => x.AudienceId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_audiences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_context_ingestion_events",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_context_ingestion_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_context_ingestion_events_gcc_v2_context_ingestion_jo~",
                        column: x => x.JobId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_context_ingestion_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_knowledge_asset_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDescriptorJson = table.Column<string>(type: "text", nullable: false),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExtractionState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IndexState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProvenanceJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_knowledge_asset_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_knowledge_asset_versions_gcc_v2_knowledge_assets_Ass~",
                        column: x => x.AssetId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_knowledge_assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_product_schema_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductSchemaId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldsJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_product_schema_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_product_schema_versions_gcc_v2_product_schemas_Produ~",
                        column: x => x.ProductSchemaId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_product_schemas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_run_context_manifest_entries",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ManifestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContextKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StableId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    VersionNumber = table.Column<int>(type: "integer", nullable: true),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LifecycleDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PermissionDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FreshnessDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SelectionSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SelectedFieldIdsJson = table.Column<string>(type: "text", nullable: true),
                    SourceModifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_run_context_manifest_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_run_context_manifest_entries_gcc_v2_run_context_mani~",
                        column: x => x.ManifestId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_run_context_manifests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_style_guide_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StyleGuideId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_style_guide_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_style_guide_versions_gcc_v2_style_guides_StyleGuideId",
                        column: x => x.StyleGuideId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_style_guides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_knowledge_resources",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeAssetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ByteSize = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SafeFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ParserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ParserVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExtractionSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CoordinatesJson = table.Column<string>(type: "text", nullable: false),
                    ScanState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_v2_knowledge_resources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_knowledge_resources_gcc_v2_knowledge_asset_versions_~",
                        column: x => x.KnowledgeAssetVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_knowledge_asset_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gcc_v2_product_versions",
                schema: "content_creator_v2",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductSchemaVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldValuesJson = table.Column<string>(type: "text", nullable: false),
                    AttributeProvenanceJson = table.Column<string>(type: "text", nullable: false),
                    ApprovedClaimsJson = table.Column<string>(type: "text", nullable: false),
                    ProhibitedClaimsJson = table.Column<string>(type: "text", nullable: false),
                    MandatoryDisclaimersJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_gcc_v2_product_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gcc_v2_product_versions_gcc_v2_product_schema_versions_Prod~",
                        column: x => x.ProductSchemaVersionId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_product_schema_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gcc_v2_product_versions_gcc_v2_products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "content_creator_v2",
                        principalTable: "gcc_v2_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_briefs_create_version",
                schema: "content_creator_v2",
                table: "gcc_v2_briefs",
                columns: new[] { "CreateId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_gcc_v2_brand_kits_profile_version",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits",
                columns: new[] { "DerivedFromProfileId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_audience_versions_AudienceId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_audience_versions",
                columns: new[] { "AudienceId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_audience_versions_CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_audience_versions",
                column: "CanonicalSha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_audiences_OwnerUserId_Name",
                schema: "content_creator_v2",
                table: "gcc_v2_audiences",
                columns: new[] { "OwnerUserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_audit_events_OwnerUserId_TargetKind_TargetId~",
                schema: "content_creator_v2",
                table: "gcc_v2_context_audit_events",
                columns: new[] { "OwnerUserId", "TargetKind", "TargetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_findings_OwnerUserId_TargetKind_TargetId",
                schema: "content_creator_v2",
                table: "gcc_v2_context_findings",
                columns: new[] { "OwnerUserId", "TargetKind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_ingestion_events_JobId_Seq",
                schema: "content_creator_v2",
                table: "gcc_v2_context_ingestion_events",
                columns: new[] { "JobId", "Seq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_ingestion_jobs_OwnerUserId_TargetKind_Target~",
                schema: "content_creator_v2",
                table: "gcc_v2_context_ingestion_jobs",
                columns: new[] { "OwnerUserId", "TargetKind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_ingestion_jobs_Status_LeaseUntilUtc",
                schema: "content_creator_v2",
                table: "gcc_v2_context_ingestion_jobs",
                columns: new[] { "Status", "LeaseUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_context_selections_OwnerUserId_CreateId_CreatedAtUtc",
                schema: "content_creator_v2",
                table: "gcc_v2_context_selections",
                columns: new[] { "OwnerUserId", "CreateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_knowledge_asset_versions_AssetId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_knowledge_asset_versions",
                columns: new[] { "AssetId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_knowledge_asset_versions_CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_knowledge_asset_versions",
                column: "CanonicalSha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_knowledge_assets_OwnerUserId_Name",
                schema: "content_creator_v2",
                table: "gcc_v2_knowledge_assets",
                columns: new[] { "OwnerUserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_knowledge_resources_KnowledgeAssetVersionId_Resource~",
                schema: "content_creator_v2",
                table: "gcc_v2_knowledge_resources",
                columns: new[] { "KnowledgeAssetVersionId", "ResourceKind", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_schema_versions_CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_product_schema_versions",
                column: "CanonicalSha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_schema_versions_ProductSchemaId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_product_schema_versions",
                columns: new[] { "ProductSchemaId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_schemas_OwnerUserId_Name",
                schema: "content_creator_v2",
                table: "gcc_v2_product_schemas",
                columns: new[] { "OwnerUserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_versions_CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_product_versions",
                column: "CanonicalSha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_versions_ProductId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_product_versions",
                columns: new[] { "ProductId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_product_versions_ProductSchemaVersionId",
                schema: "content_creator_v2",
                table: "gcc_v2_product_versions",
                column: "ProductSchemaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_products_OwnerUserId_Name",
                schema: "content_creator_v2",
                table: "gcc_v2_products",
                columns: new[] { "OwnerUserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_attachments_ObjectKey",
                schema: "content_creator_v2",
                table: "gcc_v2_run_attachments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_attachments_OwnerUserId_CreateId",
                schema: "content_creator_v2",
                table: "gcc_v2_run_attachments",
                columns: new[] { "OwnerUserId", "CreateId" });

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_context_manifest_entries_ManifestId_ContextKind_~",
                schema: "content_creator_v2",
                table: "gcc_v2_run_context_manifest_entries",
                columns: new[] { "ManifestId", "ContextKind", "StableId", "VersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_context_manifests_JobId",
                schema: "content_creator_v2",
                table: "gcc_v2_run_context_manifests",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_context_manifests_JobId_Attempt",
                schema: "content_creator_v2",
                table: "gcc_v2_run_context_manifests",
                columns: new[] { "JobId", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_run_context_manifests_Sha256",
                schema: "content_creator_v2",
                table: "gcc_v2_run_context_manifests",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_style_guide_versions_CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_style_guide_versions",
                column: "CanonicalSha256");

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_style_guide_versions_StyleGuideId_VersionNumber",
                schema: "content_creator_v2",
                table: "gcc_v2_style_guide_versions",
                columns: new[] { "StyleGuideId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gcc_v2_style_guides_OwnerUserId_Name",
                schema: "content_creator_v2",
                table: "gcc_v2_style_guides",
                columns: new[] { "OwnerUserId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_v2_audience_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_context_audit_events",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_context_findings",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_context_ingestion_events",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_context_selections",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_knowledge_resources",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_product_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_run_attachments",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_run_context_manifest_entries",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_style_guide_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_audiences",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_context_ingestion_jobs",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_knowledge_asset_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_product_schema_versions",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_products",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_run_context_manifests",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_style_guides",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_knowledge_assets",
                schema: "content_creator_v2");

            migrationBuilder.DropTable(
                name: "gcc_v2_product_schemas",
                schema: "content_creator_v2");

            migrationBuilder.DropIndex(
                name: "ux_gcc_v2_briefs_create_version",
                schema: "content_creator_v2",
                table: "gcc_v2_briefs");

            migrationBuilder.DropIndex(
                name: "ux_gcc_v2_brand_kits_profile_version",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits");

            migrationBuilder.DropColumn(
                name: "AcceptedByUserId",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits");

            migrationBuilder.DropColumn(
                name: "CanonicalSha256",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                schema: "content_creator_v2",
                table: "gcc_v2_brand_kits");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_v2_briefs_create_id",
                schema: "content_creator_v2",
                table: "gcc_v2_briefs",
                column: "CreateId");
        }
    }
}
