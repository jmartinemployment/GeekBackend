using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260829150000_ToolSourceCrawlOwnerNotCreate")]
public partial class ToolSourceCrawlOwnerNotCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "unknown");

        migrationBuilder.Sql("""
            UPDATE content_creator_v2.gcc_v2_tool_source_crawl_runs r
            SET "OwnerUserId" = c."OwnerUserId"
            FROM content_creator_v2.gcc_v2_creates c
            WHERE c."Id" = r."CreateId";
            """);

        migrationBuilder.DropIndex(
            name: "ix_gcc_v2_tool_source_crawl_runs_create_id_created",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs");

        migrationBuilder.DropColumn(
            name: "CreateId",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_tool_source_crawl_runs_owner_created",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs",
            columns: new[] { "OwnerUserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_gcc_v2_tool_source_crawl_runs_owner_created",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs");

        migrationBuilder.AddColumn<Guid>(
            name: "CreateId",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs",
            type: "uuid",
            nullable: false,
            defaultValue: Guid.Empty);

        migrationBuilder.DropColumn(
            name: "OwnerUserId",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_tool_source_crawl_runs_create_id_created",
            schema: "content_creator_v2",
            table: "gcc_v2_tool_source_crawl_runs",
            columns: new[] { "CreateId", "CreatedAtUtc" });
    }
}
