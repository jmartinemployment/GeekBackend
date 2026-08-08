using GeekRepository.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator;

/// <summary>
/// Purges GccSiteAnalysis rows created before the gap-generation logic fix (commit bbd2a05, ~2026-08-07).
/// Pre-fix analyses have stale GapsJson with LLM-invented pillar/subtopic topics instead of real
/// verbatim heading matches. Since the ready-path endpoint replays stored GapsJson without re-derivation,
/// this stale data was served indefinitely. Purging forces re-analysis with the corrected logic.
/// One-way: Down is no-op.
/// </summary>
[DbContext(typeof(ContentCreatorDbContext))]
[Migration("20260808120000_PurgePreFixGapsData")]
public partial class PurgePreFixGapsData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM "content_creator"."gcc_site_analyses"
            WHERE "CreatedAtUtc" < '2026-08-07T00:00:00Z'::timestamptz;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible: deleted rows cannot be restored.
    }
}
