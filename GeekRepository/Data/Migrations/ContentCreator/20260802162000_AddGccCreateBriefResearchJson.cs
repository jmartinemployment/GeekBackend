using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260802162000_AddGccCreateBriefResearchJson")]
    public partial class AddGccCreateBriefResearchJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "brief_json",
                schema: "content_creator",
                table: "gcc_creates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "research_json",
                schema: "content_creator",
                table: "gcc_creates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "brief_json",
                schema: "content_creator",
                table: "gcc_creates");

            migrationBuilder.DropColumn(
                name: "research_json",
                schema: "content_creator",
                table: "gcc_creates");
        }
    }
}
