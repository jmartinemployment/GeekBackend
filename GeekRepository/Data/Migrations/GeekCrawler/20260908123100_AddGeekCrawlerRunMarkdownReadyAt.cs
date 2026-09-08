using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260908123100_AddGeekCrawlerRunMarkdownReadyAt")]
public partial class AddGeekCrawlerRunMarkdownReadyAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MarkdownReadyAt",
            schema: "geek_crawler",
            table: "crawl_runs",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MarkdownReadyAt",
            schema: "geek_crawler",
            table: "crawl_runs");
    }
}
