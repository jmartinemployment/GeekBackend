using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Workflow.DTOs;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The tools the publisher's own site already links under this topic, resolved at generate time so a
/// blog or pillar can name them and link each to its own page.
///
/// <para>
/// Jeff, 2026-09-27: "Add Next js links for tools in blog, and Pillar". The instruction that does
/// that lives in <c>ResearchBriefBuilder.AppendKnownToolsBrief</c> -- it renders each tool's name and
/// its <c>/tools/{department}/{slug}</c> path, requires <c>Run.href</c> on the first substantive
/// mention, and forbids a roll-call list. It opens with <c>if (tools.Count == 0) return;</c> and
/// <see cref="ProjectGenerationContext.KnownCrawlTools"/> was never set on the Create path, so none
/// of it ever reached the model.
/// </para>
///
/// <para>
/// The names still got in, through <c>BuildPublisherSiteBlock</c> -- the publisher's own home page
/// headings and prose, where those tools are listed. That block carries no paths and no rule against
/// enumerating them, which is exactly what came out: "Among the leading AI tools ... are Melio, Dext,
/// Lightyear, Stampli, and AvidXchange." Names, in a list, unlinked.
/// </para>
///
/// <para>
/// The data was already being computed and dropped. <c>GccController</c>'s must-mention block builds
/// the same site structure and the same match, keeps <c>MatchedHeading</c> and <c>ChildHeadings</c>,
/// and discards <c>MatchResult.RecommendedTools</c>. This reads that field. Shape copied from
/// <see cref="GccPublisherProfileResolver"/> deliberately: a resolver called from generation,
/// returning empty when the crawl cannot answer.
/// </para>
///
/// <para>
/// Empty is never a refusal. No project-site run, no pages, or a topic the site does not cover are
/// all "we do not know which tools", and the draft is then written as it is today. Refusing here
/// would block every create on a site whose crawl has not landed.
/// </para>
/// </summary>
public sealed class GccKnownToolsResolver(
    IGccCrawlPageReader pages,
    ILogger<GccKnownToolsResolver> logger)
{
    /// <summary>Matches the page size the must-mention block already reads this run with.</summary>
    private const int PageBatch = 50;

    /// <summary>
    /// A ceiling, so one generate cannot turn a 50,000-page project-site run into a thousand
    /// repository calls. The pages that link tools for a topic are the pages that cover it.
    /// </summary>
    private const int MaxPages = 2_000;

    public async Task<IReadOnlyList<KnownCrawlTool>> ResolveAsync(
        GccCreateDto create,
        CancellationToken ct = default)
    {
        if (create.ProjectSiteRunId is not Guid runId || runId == Guid.Empty) return [];
        if (string.IsNullOrWhiteSpace(create.Topic)) return [];

        var crawled = new List<GeekCrawlerPageDto>();
        try
        {
            var offset = 0;
            while (crawled.Count < MaxPages)
            {
                var chunk = await pages.ListPageBlocksAsync(runId, PageBatch, offset, ct);
                if (chunk.Count == 0) break;
                crawled.AddRange(chunk);
                if (chunk.Count < PageBatch) break;
                offset += chunk.Count;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex, "Known tools: could not read run {RunId}, so this create generates with no tool links", runId);
            return [];
        }

        if (crawled.Count == 0)
        {
            logger.LogInformation(
                "Known tools: run {RunId} returned no pages, so this create generates with no tool links.", runId);
            return [];
        }

        var structure = GeekCrawlerSiteStructure.Build(runId, crawled);
        var matched = GccSiteStructureMatch.MatchAll(structure, [create.Topic.Trim()])
            .FirstOrDefault(m => m.RecommendedTools.Count > 0);
        if (matched is null)
        {
            logger.LogInformation(
                "Known tools: nothing on the site links tools under \"{Topic}\", so this create generates with no tool links.",
                create.Topic);
            return [];
        }

        // Href is where the crawl found the tool. AppendKnownToolsBrief reports it as the source and
        // forbids sending the reader there -- the link the writer sets is the publisher's own
        // /tools/{department}/{slug} path, which that brief derives from the name.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = new List<KnownCrawlTool>();
        foreach (var tool in matched.RecommendedTools)
        {
            var name = tool.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!seen.Add(name)) continue;
            tools.Add(new KnownCrawlTool(name, string.IsNullOrWhiteSpace(tool.Href) ? null : tool.Href!.Trim()));
        }

        return tools;
    }
}
