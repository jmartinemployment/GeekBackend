using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260907120000_AddGeekCrawlerPageMarkdownFields")]
public partial class AddGeekCrawlerPageMarkdownFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Title",
            schema: "geek_crawler",
            table: "crawl_pages",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Markdown",
            schema: "geek_crawler",
            table: "crawl_pages",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Excerpt",
            schema: "geek_crawler",
            table: "crawl_pages",
            type: "character varying(4096)",
            maxLength: 4096,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "MarkdownBackfilledAt",
            schema: "geek_crawler",
            table: "crawl_pages",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Title",
            schema: "geek_crawler",
            table: "crawl_pages");

        migrationBuilder.DropColumn(
            name: "Markdown",
            schema: "geek_crawler",
            table: "crawl_pages");

        migrationBuilder.DropColumn(
            name: "Excerpt",
            schema: "geek_crawler",
            table: "crawl_pages");

        migrationBuilder.DropColumn(
            name: "MarkdownBackfilledAt",
            schema: "geek_crawler",
            table: "crawl_pages");
    }
}
