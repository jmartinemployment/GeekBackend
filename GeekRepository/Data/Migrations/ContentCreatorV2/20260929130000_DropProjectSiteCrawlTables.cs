using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

/// <summary>
/// Drop the project-site crawl tables. Crawl data does not go in Postgres.
///
/// Hand-written with no Designer or snapshot file, the same as
/// 20260830100000_DropToolSourceCrawlTables. `ef migrations add` was tried first and produced an
/// unshippable migration: alongside these three drops it scaffolded CreateTable for seven
/// unrelated tables -- customer_outcomes, drive_connections, gsc_connections,
/// pipeline_definitions, sharepoint_connections, task_agent_library_preferences and
/// visual_guidelines -- because the model snapshot had fallen behind the model. Bundling that
/// schema change into a removal nobody asked it to make is how a drop migration fails on an
/// existing database. Writing the three statements by hand changes exactly what it says.
/// </summary>
[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260929130000_DropProjectSiteCrawlTables")]
public partial class DropProjectSiteCrawlTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Links first: it carries RunId and PageId, so it goes before what it points at.
        migrationBuilder.DropTable(
            name: "gcc_v2_project_site_crawl_links",
            schema: "content_creator_v2");

        migrationBuilder.DropTable(
            name: "gcc_v2_project_site_crawl_pages",
            schema: "content_creator_v2");

        migrationBuilder.DropTable(
            name: "gcc_v2_project_site_crawl_runs",
            schema: "content_creator_v2");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
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
}
