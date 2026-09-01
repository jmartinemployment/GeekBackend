using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260901120000_ProjectSiteCrawlAndDropPartnerResearch")]
public partial class ProjectSiteCrawlAndDropPartnerResearch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_partner_research_records",
            schema: "content_creator_v2");

        migrationBuilder.AddColumn<Guid>(
            name: "ProjectSiteCrawlRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_creates",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ProjectSiteCrawlRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "gcc_v2_project_site_crawl_runs",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SiteUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                SeedUrlsJson = table.Column<string>(type: "text", nullable: false, defaultValue: "[]"),
                HostProgressJson = table.Column<string>(type: "text", nullable: true),
                ErrorSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_project_site_crawl_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_project_site_crawl_pages",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                Origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                FinalUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                RobotsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                Html = table.Column<string>(type: "text", nullable: true),
                CrawledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_project_site_crawl_pages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_project_site_crawl_links",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                PageId = table.Column<Guid>(type: "uuid", nullable: false),
                FromUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                LinkUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                IsSameOrigin = table.Column<bool>(type: "boolean", nullable: false),
                DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_project_site_crawl_links", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_project_site_crawl_runs_owner_site_created",
            schema: "content_creator_v2",
            table: "gcc_v2_project_site_crawl_runs",
            columns: new[] { "OwnerUserId", "SiteUrl", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_project_site_crawl_pages_run_id",
            schema: "content_creator_v2",
            table: "gcc_v2_project_site_crawl_pages",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_project_site_crawl_pages_run_url",
            schema: "content_creator_v2",
            table: "gcc_v2_project_site_crawl_pages",
            columns: new[] { "RunId", "Url" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_project_site_crawl_links_run_id",
            schema: "content_creator_v2",
            table: "gcc_v2_project_site_crawl_links",
            column: "RunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "gcc_v2_project_site_crawl_links", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_project_site_crawl_pages", schema: "content_creator_v2");
        migrationBuilder.DropTable(name: "gcc_v2_project_site_crawl_runs", schema: "content_creator_v2");

        migrationBuilder.DropColumn(
            name: "ProjectSiteCrawlRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_jobs");

        migrationBuilder.DropColumn(
            name: "ProjectSiteCrawlRunId",
            schema: "content_creator_v2",
            table: "gcc_v2_creates");

        migrationBuilder.CreateTable(
            name: "gcc_v2_partner_research_records",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                TargetUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                HostDomain = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CrawledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                CrawlStatusLog = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ExtractedTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                PageJson = table.Column<string>(type: "text", nullable: true),
                FlattenedTextContent = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table => { table.PrimaryKey("PK_gcc_v2_partner_research_records", x => x.Id); });
    }
}
