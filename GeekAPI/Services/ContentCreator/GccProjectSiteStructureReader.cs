using GeekAPI.HttpClients;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// The operator's own site as structure, read back from the crawl Geek-Crawler-v2 already performed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Blocks, never Html.</b> Heading levels and the anchors under a heading survive in the typed
/// blocks and do not survive the flat text projection, so this takes the block route — and the Html
/// is ~98% of corpus size besides.
/// </para>
/// <para>
/// <b>One implementation, because there were about to be three.</b> The paged read and the structure
/// build were written out twice inside <c>GccController</c> — the <c>hierarchy-match</c> route and the
/// must-mention block — and <c>ToolPageGenerator</c> needs the same thing to stop asking Site
/// Analyzer. Three copies of "page the blocks and build the tree" is three places for the batch size,
/// the termination condition or the Html-versus-blocks choice to drift.
/// </para>
/// <para>
/// <b>Nothing here crawls.</b> The crawl happened; this reads it. An empty result means the run has no
/// pages, which the caller reports — it is never recovered from by crawling or by substituting
/// anything.
/// </para>
/// </remarks>
public sealed class GccProjectSiteStructureReader(IGccCrawlPageReader pages)
{
    /// <summary>The repository's block route pages; this is the page size, in one place.</summary>
    private const int Batch = 50;

    /// <summary>
    /// The crawled site's structure, or null when the run id is empty or the run holds no pages.
    /// </summary>
    public async Task<SiteStructure?> ReadAsync(Guid runId, CancellationToken ct)
    {
        if (runId == Guid.Empty) return null;

        var crawled = new List<GeekCrawlerPageDto>();
        var offset = 0;
        while (true)
        {
            var chunk = await pages.ListPageBlocksAsync(runId, Batch, offset, ct).ConfigureAwait(false);
            if (chunk.Count == 0) break;
            crawled.AddRange(chunk);
            if (chunk.Count < Batch) break;
            offset += chunk.Count;
        }

        return crawled.Count == 0 ? null : GeekCrawlerSiteStructure.Build(runId, crawled);
    }
}
