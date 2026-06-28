using Dapper;
using GeekSeo.Application.Models.Seo;
using Microsoft.Extensions.Logging;
using Npgsql;
using static GeekSa2Read.Sa2ContentWriterExportBuilder;

namespace GeekSa2Read;

public sealed class Sa2ContentWriterExportReader(ILogger<Sa2ContentWriterExportReader> logger)
{
    public async Task<ContentWriterSerpExport?> GetExportAsync(Guid runId, CancellationToken ct = default)
    {
        var connectionString = SiteAnalyzer2Connection.TryResolve();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("SITE_ANALYZER2_DATABASE_URL is not configured; cannot read export for run {RunId}", runId);
            return null;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        const string runSql = """
            SELECT
                "Id",
                "ProjectId",
                "Keyword",
                "TargetSiteUrl",
                "Status",
                "SerpSeResultsCount",
                "SerpCapturedAt",
                "CompetitorCrawlStatus",
                "CompetitorCrawlFinishedAt",
                "MatchedPillarTopic",
                "MatchedPillarIntent",
                "MatchedPillarAngle",
                "GapTopics",
                "WritingInstructions"
            FROM sa2.analysis_runs
            WHERE "Id" = @RunId
            """;

        var run = await conn.QuerySingleOrDefaultAsync<AnalysisRunRow>(
            new CommandDefinition(runSql, new { RunId = runId }, cancellationToken: ct));
        if (run is null)
            return null;

        var serpItems = (await conn.QueryAsync<SerpItemRow>(
            new CommandDefinition(
                """
                SELECT
                    "Id", "Type", "Ads", "RankAbsolute", "RankGroup",
                    "Title", "Url", "Domain", "Description", "ExtendedSnippet",
                    "PreSnippet", "WebsiteName", "AiOverviewMarkdown", "AiOverviewStatusMessage"
                FROM sa2.serp_items
                WHERE "RunId" = @RunId
                ORDER BY "RankAbsolute"
                """,
                new { RunId = runId },
                cancellationToken: ct))).ToList();

        var serpItemIds = serpItems.Select(i => i.Id).ToList();
        var relatedQueries = serpItemIds.Count == 0
            ? []
            : (await conn.QueryAsync<RelatedQueryRow>(
                new CommandDefinition(
                    """
                    SELECT "SerpItemId", "QueryText", "QueryType", "Sequence"
                    FROM sa2.serp_related_queries
                    WHERE "SerpItemId" = ANY(@SerpItemIds)
                    ORDER BY "Sequence"
                    """,
                    new { SerpItemIds = serpItemIds.ToArray() },
                    cancellationToken: ct))).ToList();

        var competitorPages = (await conn.QueryAsync<CompetitorPageRow>(
            new CommandDefinition(
                """
                SELECT "Id", "Domain", "Url", "SeedRankAbsolute", "DepthFromSeed"
                FROM sa2.competitor_pages
                WHERE "RunId" = @RunId
                ORDER BY "SeedRankAbsolute", "DepthFromSeed"
                """,
                new { RunId = runId },
                cancellationToken: ct))).ToList();

        var competitorPageIds = competitorPages.Select(p => p.Id).ToList();
        var competitorHeadings = competitorPageIds.Count == 0
            ? []
            : (await conn.QueryAsync<CompetitorHeadingRow>(
                new CommandDefinition(
                    """
                    SELECT "CompetitorPageId", "Level", "Text", "Sequence"
                    FROM sa2.competitor_page_headings
                    WHERE "CompetitorPageId" = ANY(@CompetitorPageIds)
                    ORDER BY "Sequence"
                    """,
                    new { CompetitorPageIds = competitorPageIds.ToArray() },
                    cancellationToken: ct))).ToList();

        var competitorJsonLd = competitorPageIds.Count == 0
            ? []
            : (await conn.QueryAsync<CompetitorJsonLdRow>(
                new CommandDefinition(
                    """
                    SELECT "CompetitorPageId", "ParsedType"
                    FROM sa2.competitor_page_json_ld
                    WHERE "CompetitorPageId" = ANY(@CompetitorPageIds)
                    """,
                    new { CompetitorPageIds = competitorPageIds.ToArray() },
                    cancellationToken: ct))).ToList();

        var sourcePages = (await conn.QueryAsync<SourcePageRow>(
            new CommandDefinition(
                """
                SELECT "Id", "Url", "DepthFromHomepage"
                FROM sa2.pages
                WHERE "RunId" = @RunId AND "IsTargetSite" = TRUE
                """,
                new { RunId = runId },
                cancellationToken: ct))).ToList();

        var sourcePageIds = sourcePages.Select(p => p.Id).ToList();
        var pageHeadings = sourcePageIds.Count == 0
            ? []
            : (await conn.QueryAsync<PageHeadingRow>(
                new CommandDefinition(
                    """
                    SELECT "PageId", "Level", "Text", "Sequence"
                    FROM sa2.page_headings
                    WHERE "PageId" = ANY(@PageIds)
                    ORDER BY "Sequence"
                    """,
                    new { PageIds = sourcePageIds.ToArray() },
                    cancellationToken: ct))).ToList();

        var authorityRow = await conn.QuerySingleOrDefaultAsync<AuthorityRow>(
            new CommandDefinition(
                """
                SELECT "AuthorityPageUrls", "GeekSeoProjectId"
                FROM sa2.site_profiles
                WHERE "SiteUrl" = @SiteUrl
                LIMIT 1
                """,
                new { SiteUrl = run.TargetSiteUrl },
                cancellationToken: ct));

        var authorityUrls = Sa2Json.ParseStringList(authorityRow?.AuthorityPageUrls);
        var geekSeoProjectId = authorityRow?.GeekSeoProjectId;

        return Sa2ContentWriterExportBuilder.Build(
            run,
            serpItems,
            relatedQueries,
            competitorPages,
            competitorHeadings,
            competitorJsonLd,
            sourcePages,
            pageHeadings,
            authorityUrls,
            geekSeoProjectId,
            DateTimeOffset.UtcNow);
    }

    private sealed class AuthorityRow
    {
        public string? AuthorityPageUrls { get; init; }
        public Guid? GeekSeoProjectId { get; init; }
    }
}
