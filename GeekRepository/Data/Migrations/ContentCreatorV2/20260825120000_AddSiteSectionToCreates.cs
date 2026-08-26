using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreatorV2;

[DbContext(typeof(ContentCreatorV2DbContext))]
[Migration("20260825120000_AddSiteSectionToCreates")]
public partial class AddSiteSectionToCreates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SiteSectionJson",
            schema: "content_creator_v2",
            table: "gcc_v2_creates",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SiteUrl",
            schema: "content_creator_v2",
            table: "gcc_v2_creates",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SiteSectionJson",
            schema: "content_creator_v2",
            table: "gcc_v2_creates");

        migrationBuilder.DropColumn(
            name: "SiteUrl",
            schema: "content_creator_v2",
            table: "gcc_v2_creates");
    }
}
