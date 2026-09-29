using GeekAPI.HttpClients;
using GeekApplication.Models.GeekCrawler;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a declared URL must be before a project may be saved: crawled, indexed, and carrying enough
/// to write from.
/// </summary>
/// <remarks>
/// <para>
/// Three lists, one test. The project site, the partners and the competitors are all declared URLs
/// and all answer the same question — the site differs only in how many there may be. Asking the
/// site a different question (does the index return a run id) while asking the other two whether
/// they are indexed is one rule in two shapes, which is how they come to disagree.
/// </para>
/// <para>
/// <b>Indexed is not the same as useful.</b> A crawl can complete having been blocked at the first
/// page, or against a site that renders nothing without JavaScript, and still put a row in the
/// index. That passes "does an index exist" and gives a writer nothing. The run itself records what
/// actually landed -- <c>RagPagesEnglish</c> and <c>RagChunksUpserted</c> are written by the RAG
/// webhook when indexing finishes -- and until now nothing read either of them.
/// </para>
/// <para>
/// Those counts belong to the <i>run</i>, so they describe a host only while a run covers one host.
/// That is the rule (<c>AGENTS.md</c>: "Run ID = one URL") and it is enforced at the point a crawl
/// starts; see <c>GeekCrawlerService.StartCrawlAsync</c>.
/// </para>
/// </remarks>
public static class GccDeclaredUrlEvidence
{
    /// <summary>Exactly one project site. It is the page this content must not duplicate.</summary>
    public const int RequiredSiteUrls = 1;

    /// <summary>
    /// Partners and competitors are required in fives (Jeff, 2026-09-29). Five is what a pillar
    /// names and what a comparison needs to be a comparison; fewer is a page about one vendor
    /// wearing a category's title.
    /// </summary>
    public const int RequiredPartnerUrls = 5;
    public const int RequiredCompetitorUrls = 5;

    /// <summary>
    /// A crawl below this produced a row and not a corpus — blocked at the first page, or a site
    /// that renders nothing without JavaScript. Both index; neither can be written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from the chunker rather than estimated. Geek-Crawler-Rag splits at 200-token children
    /// with 40 overlap (stride 160) and 1,000-token parents (<c>config.py:69-76</c>), so a
    /// substantial page of 1,200-1,500 words yields roughly 10-12 chunks and a thin one yields about
    /// three.
    /// </para>
    /// <para>
    /// 25 pages is a site rather than a brochure: a vendor's pricing, features, integrations, docs
    /// and about, or a consultancy's services, case studies and a blog. 250 chunks is those pages
    /// carrying prose — at ten a page it is what 25 real pages produce. Both, because they catch
    /// different failures: few pages is a crawl that was blocked, while many pages and few chunks is
    /// a crawl that fetched a site rendering nothing without JavaScript.
    /// </para>
    /// <para>
    /// The first version of this was 5 and 25, which is two good pages — Jeff, 2026-09-29: "seems
    /// like a very low bar". It was, and it was eyeballed rather than derived.
    /// </para>
    /// </remarks>
    public const int MinIndexedPages = 25;
    public const int MinIndexedChunks = 250;

    /// <summary>Why one declared URL cannot be used, or null when it can.</summary>
    public static string? Unusable(GeekCrawlerRagHostIndex row, GeekCrawlerRunDto? run)
    {
        if (!row.Indexed || string.IsNullOrWhiteSpace(row.RunId))
            return "no crawl is indexed for it";

        if (run is null)
            return "the index names a crawl run that cannot be read";

        if (!string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase))
            return $"its crawl is {run.Status}, not complete";

        if (run.ContentReadyAt is null)
            return "its crawl extracted no content";

        // Counted, not merely present. The whole point of this check is that a row in the index is
        // not evidence, and a threshold is what separates the two.
        var pages = run.RagPagesEnglish ?? 0;
        var chunks = run.RagChunksUpserted ?? 0;
        if (pages < MinIndexedPages || chunks < MinIndexedChunks)
        {
            // The reject count is added to the MESSAGE, deliberately not to the decision. It
            // answers the operator's next question -- why is the count this low -- and a crawl that
            // threw away 460 error pages reads very differently from one that simply found 40.
            // Making it a gate would change which runs are usable, which is a policy call and not
            // this change; the threshold above is untouched.
            var rejected = run.RagPagesSkippedUnusable ?? 0;
            var because = rejected > 0
                ? $" ({rejected} page(s) were rejected as not citable -- error pages, robots-denied, "
                    + "or non-English locale paths)"
                : "";
            return $"its crawl indexed {pages} page(s) and {chunks} chunk(s), below the "
                + $"{MinIndexedPages} pages and {MinIndexedChunks} chunks a page can be written from"
                + because;
        }

        return null;
    }

    /// <summary>The count rule, as the message the operator gets. Null when the count is met.</summary>
    public static string? WrongCount(string label, int actual, int required) =>
        actual >= required
            ? null
            : $"{label}: {actual} declared, {required} required.";
}
