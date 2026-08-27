using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260827180000_AddPartnerResearchRecords")]
public partial class AddPartnerResearchRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "gcc_v2_partner_research_records",
            schema: "content_creator_v2",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreateId = table.Column<Guid>(type: "uuid", nullable: false),
                JobId = table.Column<Guid>(type: "uuid", nullable: true),
                TargetUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                HostDomain = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CrawledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                CrawlStatusLog = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                ExtractedTitle = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                PageJson = table.Column<string>(type: "text", nullable: true),
                FlattenedTextContent = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_gcc_v2_partner_research_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_partner_research_records_create_id",
            schema: "content_creator_v2",
            table: "gcc_v2_partner_research_records",
            column: "CreateId");

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_partner_research_records_target_url_crawled_at",
            schema: "content_creator_v2",
            table: "gcc_v2_partner_research_records",
            columns: new[] { "TargetUrl", "CrawledAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_gcc_v2_partner_research_records_success_url_crawled",
            schema: "content_creator_v2",
            table: "gcc_v2_partner_research_records",
            columns: new[] { "IsSuccess", "TargetUrl", "CrawledAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "gcc_v2_partner_research_records",
            schema: "content_creator_v2");
    }
}
