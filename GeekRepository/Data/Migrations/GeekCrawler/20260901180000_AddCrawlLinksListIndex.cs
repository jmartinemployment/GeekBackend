using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260901180000_AddCrawlLinksListIndex")]
public partial class AddCrawlLinksListIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_crawl_links_run_same_origin_discovered",
            schema: "geek_crawler",
            table: "crawl_links",
            columns: new[] { "RunId", "IsSameOrigin", "DiscoveredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_crawl_links_run_same_origin_discovered",
            schema: "geek_crawler",
            table: "crawl_links");
    }
}
