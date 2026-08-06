using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator;

/// <summary>
/// Purges GccSiteAnalysis rows that became orphaned when Geek-SEO deleted
/// pre-tree SiteAnalysisProfile rows. All rows with CreatedAtUtc < 2026-08-06 UTC
/// are prior to the 2.0 tree model and are now orphaned (cross-DB, time cutoff is proxy).
/// One-way: Down is no-op.
/// </summary>
[DbContext(typeof(ContentCreatorDbContext))]
[Migration("20260806160000_PurgeOrphanedGccSiteAnalyses")]
public partial class PurgeOrphanedGccSiteAnalyses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "content_creator"."gcc_site_analyses"
            WHERE "CreatedAtUtc" < '2026-08-06T00:00:00Z'::timestamptz;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible: deleted rows cannot be restored.
    }
}
