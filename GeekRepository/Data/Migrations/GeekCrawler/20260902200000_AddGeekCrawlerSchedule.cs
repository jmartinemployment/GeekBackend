using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.GeekCrawler;

[DbContext(typeof(GeekCrawlerDbContext))]
[Migration("20260902200000_AddGeekCrawlerSchedule")]
public partial class AddGeekCrawlerSchedule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "crawl_schedules",
            schema: "geek_crawler",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CrawlType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                SeedUrlsJson = table.Column<string>(type: "text", nullable: false),
                SeedKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                IntervalHours = table.Column<int>(type: "integer", nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                NextRunUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastStartedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastRunId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_crawl_schedules", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_schedules_due",
            schema: "geek_crawler",
            table: "crawl_schedules",
            columns: new[] { "Enabled", "NextRunUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_crawl_schedules_owner_slot",
            schema: "geek_crawler",
            table: "crawl_schedules",
            columns: new[] { "OwnerUserId", "CrawlType", "SeedKey" },
            unique: true,
            filter: "\"SeedKey\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "crawl_schedules",
            schema: "geek_crawler");
    }
}
