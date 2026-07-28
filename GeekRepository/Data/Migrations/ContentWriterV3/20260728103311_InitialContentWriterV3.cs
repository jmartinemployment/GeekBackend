using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV3
{
    /// <inheritdoc />
    public partial class InitialContentWriterV3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content_writer_v3");

            migrationBuilder.CreateTable(
                name: "approval_events",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "client_brand_voice_links",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandVoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_brand_voice_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "client_profile_versions",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    approved_facts = table.Column<string>(type: "text", nullable: false),
                    prohibited_claims = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_profile_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "client_profiles",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "clients",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_asset_versions",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    body_document_json = table.Column<string>(type: "text", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_asset_versions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_assets",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "content_campaigns",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Keyword = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProfileVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "text", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequeuedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequeuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "keyword_candidates",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SearchVolume = table.Column<int>(type: "integer", nullable: true),
                    Difficulty = table.Column<int>(type: "integer", nullable: true),
                    Intent = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_keyword_candidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pain_point_evidence_links",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PainPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dimension = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pain_point_evidence_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "pain_points",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ReaderSymptom = table.Column<string>(type: "text", nullable: false),
                    CostOfInaction = table.Column<string>(type: "text", nullable: false),
                    OfferTerminology = table.Column<string>(type: "text", nullable: false),
                    objections = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    StaleeSinceUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pain_points", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "publication_events",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publication_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "publications",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    response_snapshot = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reconciliation_proposals",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PainPointId = table.Column<Guid>(type: "uuid", nullable: true),
                    proposed_data = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reconciliation_proposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_evidences",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Statement = table.Column<string>(type: "text", nullable: false),
                    SupportLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedForClaim = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_evidences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_runs",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    Keyword = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DiscoveredSourceCount = table.Column<int>(type: "integer", nullable: false),
                    SpentBudget = table.Column<decimal>(type: "numeric", nullable: false),
                    MaxBudget = table.Column<decimal>(type: "numeric", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_sources",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "review_comments",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionPath = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_comments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "strategy_briefs",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    PainPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceProfile = table.Column<string>(type: "text", nullable: false),
                    BuyingStage = table.Column<string>(type: "text", nullable: false),
                    Angle = table.Column<string>(type: "text", nullable: false),
                    CallToAction = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_strategy_briefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                schema: "content_writer_v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspaces", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_client_keyword",
                schema: "content_writer_v3",
                table: "content_campaigns",
                columns: new[] { "ClientId", "Keyword" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_idempotency_key",
                schema: "content_writer_v3",
                table: "jobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_jobs_status_lease",
                schema: "content_writer_v3",
                table: "jobs",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_events",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "client_brand_voice_links",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "client_profile_versions",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "client_profiles",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "clients",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "content_asset_versions",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "content_assets",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "content_campaigns",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "keyword_candidates",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "pain_point_evidence_links",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "pain_points",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "publication_events",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "publications",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "reconciliation_proposals",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "research_evidences",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "research_runs",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "research_sources",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "review_comments",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "strategy_briefs",
                schema: "content_writer_v3");

            migrationBuilder.DropTable(
                name: "workspaces",
                schema: "content_writer_v3");
        }
    }
}
