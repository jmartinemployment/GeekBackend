using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Removes the obsolete site-analysis demo flag from databases that already applied
    /// the earlier CreateTable shape. Idempotent for fresh installs that never had the column.
    /// </summary>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260802190000_DropGccSiteAnalysisIsDemo")]
    public partial class DropGccSiteAnalysisIsDemo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE content_creator.gcc_site_analyses
                DROP COLUMN IF EXISTS "IsDemo";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE content_creator.gcc_site_analyses
                ADD COLUMN IF NOT EXISTS "IsDemo" boolean NOT NULL DEFAULT FALSE;
                """);
        }
    }
}
