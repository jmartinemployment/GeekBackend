using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

/// <summary>
/// Adds the column the entity has had since the crawl-report work.
///
/// GeekCrawlerRun.CrawlReportJson was added to the entity without a migration, so every EF read of
/// geek_crawler.crawl_runs selected a column Postgres did not have and failed with
/// errorMissingColumn — taking out endpoints that never touch crawl reporting.
/// </summary>
[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260917170000_AddGeekCrawlerRunCrawlReportJson")]
public partial class AddGeekCrawlerRunCrawlReportJson : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CrawlReportJson",
            schema: "geek_crawler",
            table: "crawl_runs",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CrawlReportJson",
            schema: "geek_crawler",
            table: "crawl_runs");
    }
}
