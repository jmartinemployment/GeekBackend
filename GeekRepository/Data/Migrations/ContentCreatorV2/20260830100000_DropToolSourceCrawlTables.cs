using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260830100000_DropToolSourceCrawlTables")]
public partial class DropToolSourceCrawlTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_tool_source_crawl_pages",
            schema: "content_creator_v2");

        migrationBuilder.DropTable(
            name: "gcc_v2_tool_source_crawl_runs",
            schema: "content_creator_v2");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_tool_source_crawl_runs",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SeedUrlsJson = table.Column<string>(type: "text", nullable: false),
                HostProgressJson = table.Column<string>(type: "text", nullable: true),
                PartnerResearchJson = table.Column<string>(type: "text", nullable: true),
                ErrorSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_tool_source_crawl_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "gcc_v2_tool_source_crawl_pages",
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
                table.PrimaryKey("PK_gcc_v2_tool_source_crawl_pages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_tool_source_crawl_runs_owner_created",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs",
            columns: new[] { "OwnerUserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_tool_source_crawl_pages_run_id",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_pages",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_tool_source_crawl_pages_run_url",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_pages",
            columns: new[] { "RunId", "Url" });
    }
}
