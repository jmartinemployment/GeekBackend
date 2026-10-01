using GeekAPI.HttpClients;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Reads the crawl pages behind a set of retrieved URLs and maps their blocks to typed paragraphs.
/// </summary>
/// <remarks>
/// <para>
/// Retrieval answers with the plaintext projection, which is the right thing to match a quote
/// against its page and the wrong thing to cut a quote out of: the projection is assembled for the
/// prompt with <c>Section:</c> / <c>Context:</c> / <c>Specific detail:</c> labels interleaved, and
/// every block boundary flattened into it. The blocks themselves were never lost — they are on the
/// crawl page — so this reads them back and maps them kind for kind, which is what lets a retrieved
/// quote arrive as a <c>QuoteParagraph</c> carrying its source.
/// </para>
/// <para>
/// <b>One implementation, two callers, deliberately.</b> <see cref="GccGroundingResolver"/> does this
/// at generate time and <see cref="GccAngleQuoteProbe"/> at brief time, and they must agree: the
/// probe tells the operator this partner can answer the angle, and the generate then has to find the
/// same quote. A second read-back-and-map would be two answers to one question, which is how the
/// halves of this pipeline have drifted before.
/// </para>
/// <para>
/// <b>An empty result is not an error here, and what it means depends on the caller.</b> The pages
/// were already proven present by the query that produced <c>retrieved</c>; this is the read of
/// their blocks. For a type that never quotes — pillar, blog — the typed form is an enrichment and
/// its absence changes nothing about the draft. For a type that must quote a partner it is the
/// evidence itself, and <see cref="GccGroundingResolver"/> refuses there rather than letting the
/// writer be blamed for it. The distinction belongs to the caller, so nothing is decided here.
/// </para>
/// </remarks>
public sealed class GccTypedPassageReader(IGccCrawlPageReader pages)
{
    /// <summary>The repository route caps seeds at 32.</summary>
    private const int MaxSeedsPerRead = 32;

    public async Task<IReadOnlyList<GccGroundedPassage>> ReadAsync(
        Guid runId,
        IReadOnlyList<GccQuoteablePage> retrieved,
        CancellationToken ct)
    {
        if (runId == Guid.Empty || retrieved.Count == 0)
        {
            return [];
        }

        var urls = retrieved.Select(page => page.Url).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSeedsPerRead).ToList();
        var crawled = await pages.ListPagesBySeedsAsync(runId, urls, ct);

        var byUrl = new Dictionary<string, GeekCrawlerPageDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in crawled)
        {
            byUrl.TryAdd(page.Url, page);
            if (!string.IsNullOrWhiteSpace(page.FinalUrl))
            {
                byUrl.TryAdd(page.FinalUrl, page);
            }
        }

        var typed = new List<GccGroundedPassage>();
        foreach (var page in retrieved)
        {
            if (!byUrl.TryGetValue(page.Url, out var crawledPage))
            {
                continue;
            }

            var content = GccCorpusBlockMapper.MapBlocks(crawledPage.Blocks, page.Url);
            if (content.Count > 0)
            {
                typed.Add(new GccGroundedPassage(page.Url, page.Title, content));
            }
        }
        return typed;
    }
}
