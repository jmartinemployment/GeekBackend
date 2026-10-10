using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator.Hierarchy;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.Services.JsonLd;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// One competitor page's real, extracted structure — its heading tree. Not a citable quote (that is
/// <see cref="GccGroundedPassage"/>); this is what a competitor page is <i>shaped like</i>, for outline
/// and content-gap analysis.
/// </summary>
/// <remarks>
/// <para>
/// The tree comes from the crawl's typed <c>blocks</c> (<see cref="SiteStructureNode"/>), not from a
/// re-parse of raw <c>Html</c>. <c>Html</c> is not a validated ingest field, so requiring it dropped
/// pages that had perfectly good structure — and the consumers only ever read heading text, level and
/// children, which the block-derived node already carries.
/// </para>
/// <para>
/// <c>DeclaredSchemaTypes</c> is gone with the HTML re-parse. It was computed from JSON-LD on every page
/// and had no production consumer at all — only tests read it.
/// </para>
/// </remarks>
public sealed record GccCompetitorPageAnalysis(
    string Url,
    IReadOnlyList<SiteStructureNode> Headings);

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
    GccProjectSiteStructureReader siteStructure,
    IGeekCrawlerRagClient rag,
    ILogger<GccCompetitorAnalysisResolver> logger)
{

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

        var indexed = await rag.HostsIndexedAsync(project.CompetitorUrls, CrawlTypes.Competitors, ct);
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
            // The whole run, paged, not the declared seed URLs.
            //
            // This asked by-seeds for project.CompetitorUrls -- the five declared homepages -- and the
            // repository matches those by exact equality, so at most five of a run's pages could come
            // back and in practice one: that competitor's homepage. Five competitors carrying 1,777
            // crawled pages and 28,517 paragraphs reached the prompt and the provenance guard as five
            // homepage outlines, whose headings are "Pricing" and "Book a demo". That is why the blog's
            // competitor: tags resolved to nothing while the evidence was abundant.
            //
            // Declared URLs were never canonicalized either, while the crawler normalizes a homepage to
            // authority + "/", so a declared "https://x.com" against a stored "https://x.com/" matched
            // zero rows. Reading the run by id sidesteps that entirely rather than adding URL variants.
            var structure = await siteStructure.ReadAsync(runId, ct).ConfigureAwait(false);
            if (structure is null)
            {
                logger.LogInformation(
                    "Competitor run {RunId} returned no pages with blocks; it contributes no structure.",
                    runId);
                continue;
            }

            foreach (var page in structure.Pages)
            {
                if (page.Roots.Count == 0) continue;
                analyses.Add(new GccCompetitorPageAnalysis(page.PageUrl, page.Roots));
            }
        }

        return analyses;
    }
}
