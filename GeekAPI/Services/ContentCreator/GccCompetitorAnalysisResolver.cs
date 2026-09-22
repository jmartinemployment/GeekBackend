using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Services.JsonLd;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// One competitor page's real, extracted structure — a heading tree and the schema.org types the
/// page itself declares. Not a citable quote (that's <see cref="GccGroundedPassage"/>); this is
/// what a competitor page is <i>shaped like</i>, for outline and content-gap analysis.
/// </summary>
public sealed record GccCompetitorPageAnalysis(
    string Url,
    IReadOnlyList<GccV2HeadingNode> Headings,
    IReadOnlyList<string> DeclaredSchemaTypes);

/// <summary>
/// Stage 8a: competitor heading outlines and schema, from already-persisted <c>competitors</c>
/// crawl pages. No new crawl, no new fetch — everything here was crawled and stored before this
/// resolver runs.
/// </summary>
/// <remarks>
/// <para><b>Content-mix classification is explicitly out of scope here.</b> The plan flagged that
/// <c>GccSavedSerpParser.InferShape</c> classifies from SERP <i>titles</i>, not page
/// <i>structure</i>, and needs its own logic or an explicit cut. This resolver produces the raw
/// heading/schema material a content-mix classifier would need; it does not attempt the
/// classification itself.</para>
/// <para><b>Not wired into outline selection.</b> The Coverage Gate — "a candidate heading enters
/// the outline only if retrieval returns verified evidence for it, otherwise recorded as a content
/// gap" — is Stage 2's job (heading provenance), deliberately deferred until this data exists as an
/// input. This resolver is that input becoming real; consuming it in outline generation is a
/// separate, later change.</para>
/// </remarks>
public sealed class GccCompetitorAnalysisResolver(
    IGccProjectReader projects,
    IGccCrawlPageReader pages,
    IGeekCrawlerRagClient rag,
    IJsonLdParserService jsonLdParser,
    ILogger<GccCompetitorAnalysisResolver> logger)
{
    /// <summary>The repository's by-seeds route accepts at most 32 URLs.</summary>
    private const int MaxSeedsPerRead = 32;

    /// <summary>
    /// Analyzes every indexed competitor page for a project. Pages whose crawl was never indexed,
    /// or whose HTML is empty, are skipped and logged — never a reason to fail the whole analysis;
    /// a partial competitor set is still real evidence, unlike a partial write.
    /// </summary>
    public async Task<IReadOnlyList<GccCompetitorPageAnalysis>> ResolveAsync(
        Guid projectId, CancellationToken ct = default)
    {
        var project = await projects.GetProjectAsync(projectId, ct);
        if (project is null || project.CompetitorUrls.Count == 0)
        {
            return [];
        }

        var indexed = await rag.HostsIndexedAsync(project.CompetitorUrls, ct);
        var runIds = indexed
            .Where(host => host.Indexed)
            .Select(host => Guid.TryParse(host.RunId, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (runIds.Count == 0)
        {
            logger.LogInformation(
                "No indexed competitor crawl for project {ProjectId}; competitor analysis is empty.",
                projectId);
            return [];
        }

        var analyses = new List<GccCompetitorPageAnalysis>();
        foreach (var runId in runIds)
        {
            var urls = project.CompetitorUrls.Take(MaxSeedsPerRead).ToList();
            var crawledPages = await pages.ListPagesBySeedsAsync(runId, urls, ct);

            foreach (var page in crawledPages)
            {
                if (string.IsNullOrWhiteSpace(page.Html))
                {
                    continue;
                }

                var headings = GccV2HeadingTreeBuilder.Build(page.Html);
                var jsonLdBlocks = GccJsonLdBlockExtractor.Extract(page.Html);
                var declaredTypes = jsonLdBlocks.Count > 0
                    ? jsonLdParser.DistinctDeclaredTypes(jsonLdBlocks)
                    : [];

                analyses.Add(new GccCompetitorPageAnalysis(page.Url, headings, declaredTypes));
            }
        }

        return analyses;
    }
}
