using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator;

[DbContext(typeof(ContentCreatorDbContext))]
[Migration("20260802200000_AddGccSiteAnalysisStatusAndFindings")]
public partial class AddGccSiteAnalysisStatusAndFindings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Status",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "ready");
        migrationBuilder.AddColumn<Guid>(
            name: "SeoProjectId",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<Guid>(
            name: "SeoProfileId",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ErrorMessage",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "text",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "SiteModelJson",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "text",
            nullable: false,
            defaultValue: """{"sitePages":[],"topicalNeighbors":[]}""");
        migrationBuilder.AddColumn<DateTime>(
            name: "UpdatedAtUtc",
            schema: "content_creator",
            table: "gcc_site_analyses",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Status", schema: "content_creator", table: "gcc_site_analyses");
        migrationBuilder.DropColumn(name: "SeoProjectId", schema: "content_creator", table: "gcc_site_analyses");
        migrationBuilder.DropColumn(name: "SeoProfileId", schema: "content_creator", table: "gcc_site_analyses");
        migrationBuilder.DropColumn(name: "ErrorMessage", schema: "content_creator", table: "gcc_site_analyses");
        migrationBuilder.DropColumn(name: "SiteModelJson", schema: "content_creator", table: "gcc_site_analyses");
        migrationBuilder.DropColumn(name: "UpdatedAtUtc", schema: "content_creator", table: "gcc_site_analyses");
    }
}
