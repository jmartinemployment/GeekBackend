using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260901140000_AddGeekCrawlerSeedKey")]
public partial class AddGeekCrawlerSeedKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SeedKey",
            schema: "geek_crawler",
            table: "crawl_runs",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SeedKey",
            schema: "geek_crawler",
            table: "crawl_runs");
    }
}
