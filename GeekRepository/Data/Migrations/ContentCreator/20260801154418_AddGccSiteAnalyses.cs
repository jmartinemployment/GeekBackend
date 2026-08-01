using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <inheritdoc />
    public partial class AddGccSiteAnalyses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_site_analyses",
                schema: "content_creator",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SeedTopic = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    GapsJson = table.Column<string>(type: "text", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_site_analyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_site_analyses_domain",
                schema: "content_creator",
                table: "gcc_site_analyses",
                column: "Domain");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_site_analyses",
                schema: "content_creator");
        }
    }
}
