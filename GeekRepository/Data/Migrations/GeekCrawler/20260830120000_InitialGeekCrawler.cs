using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260830120000_InitialGeekCrawler")]
public partial class InitialGeekCrawler : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "geek_crawler");

        migrationBuilder.CreateTable(
            name: "crawl_runs",
            schema: "geek_crawler",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CrawlType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SeedUrlsJson = table.Column<string>(type: "text", nullable: false),
                HostProgressJson = table.Column<string>(type: "text", nullable: true),
                ErrorSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crawl_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "crawl_pages",
            schema: "geek_crawler",
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
                table.PrimaryKey("PK_crawl_pages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "crawl_links",
            schema: "geek_crawler",
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
                table.PrimaryKey("PK_crawl_links", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_runs_owner_type_created",
            schema: "geek_crawler",
            table: "crawl_runs",
            columns: new[] { "OwnerUserId", "CrawlType", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_runs_type_status",
            schema: "geek_crawler",
            table: "crawl_runs",
            columns: new[] { "CrawlType", "Status" });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_pages_run_id",
            schema: "geek_crawler",
            table: "crawl_pages",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "ix_crawl_pages_run_url",
            schema: "geek_crawler",
            table: "crawl_pages",
            columns: new[] { "RunId", "Url" });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_links_run_id",
            schema: "geek_crawler",
            table: "crawl_links",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "ix_crawl_links_run_from",
            schema: "geek_crawler",
            table: "crawl_links",
            columns: new[] { "RunId", "FromUrl" });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_links_run_same_origin",
            schema: "geek_crawler",
            table: "crawl_links",
            columns: new[] { "RunId", "IsSameOrigin" });

        migrationBuilder.CreateIndex(
            name: "ux_crawl_links_run_from_link",
            schema: "geek_crawler",
            table: "crawl_links",
            columns: new[] { "RunId", "FromUrl", "LinkUrl" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "crawl_links", schema: "geek_crawler");
        migrationBuilder.DropTable(name: "crawl_pages", schema: "geek_crawler");
        migrationBuilder.DropTable(name: "crawl_runs", schema: "geek_crawler");
    }
}
