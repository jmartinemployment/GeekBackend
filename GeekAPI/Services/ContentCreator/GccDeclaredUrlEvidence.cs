using GeekAPI.HttpClients;
using GeekApplication.Models.GeekCrawler;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// What a declared URL must be before Generate may write from it: crawled, indexed, and carrying
/// enough to write from. The Profile saves a URL before any of this is true; the form shows this
/// answer beside it as it is entered.
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
    /// <summary>
    /// Exactly one project site. It is the page this content must not duplicate, and the only URL a
    /// project cannot be saved without. Partners and competitors are saved at any count: the floor of
    /// five of each (2026-09-29) was lifted on 2026-10-09 at Jeff's instruction.
    /// </summary>
    public const int RequiredSiteUrls = 1;

    /// <summary>
    /// A crawl below this produced a row and not a corpus — blocked at the first page, or a site
    /// that renders nothing without JavaScript. Both index; neither can be written from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two different failures, so two rules, and only the first is an absolute count.
    /// <see cref="MinIndexedPages"/> catches a crawl that was blocked: one page, or three.
    /// <see cref="MinChunksPerPage"/> catches a crawl that fetched a site rendering nothing without
    /// JavaScript — many pages, each a nav shell.
    /// </para>
    /// <para>
    /// The second rule is a ratio because site size is not the thing being measured. This was an
    /// absolute 250 chunks, derived from "200-token children with 40 overlap and 1,000-token parents
    /// (<c>config.py:69-76</c>), so a substantial page yields roughly 10-12 chunks". Both halves of
    /// that are now wrong: parents are 500 tokens (<c>config.py:102</c>) and a parent point is
    /// emitted only when it carries more than its child (Geek-Crawler-Rag <c>04310dd</c>). Measured
    /// against the live index on 2026-10-01, three finished runs in <c>geek_crawler_chunks</c>:
    /// </para>
    /// <para>
    /// lightyear.cloud 24 pages / 370 chunks = 15.4 · approvalmax 72 / 1,438 = 20.0 ·
    /// ottimate 161 / 2,519 = 15.6. So 15-20 per page, not 10-12, which made 250 chunks reachable at
    /// ~13-17 pages — the chunk floor went slack and the page floor did all the work. That is the
    /// opposite of "both, because they catch different failures".
    /// </para>
    /// <para>
    /// 25 pages also refused real sites. lightyear.cloud is a genuine vendor site carrying 370
    /// chunks of prose and it was refused for being one page short; its counts were confirmed final
    /// by re-reading them while the collection as a whole grew 62,343 → 105,737 points. Ten pages
    /// still excludes a blocked crawl (1-3) and a brochure (5-8), and three chunks a page is a page
    /// with almost no prose on it however large the site.
    /// </para>
    /// <para>
    /// The first version of this was 5 and 25, which is two good pages — Jeff, 2026-09-29: "seems
    /// like a very low bar". It was, and it was eyeballed rather than derived. These are measured;
    /// if the chunker changes again, re-measure rather than re-reason from this paragraph.
    /// </para>
    /// </remarks>
    public const int MinIndexedPages = 10;

    /// <summary>Chunks per indexed page, below which the pages carried no prose.</summary>
    public const int MinChunksPerPage = 3;

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

        // Indexing in progress is not "indexed nothing". On a first index the Library's start frame
        // lands on the run as RagState=running with both counters at zero, while the index already
        // holds the first flushes -- so until it finishes, the counters below would call a healthy
        // run a blocked one (medius.com, 2026-10-09: "indexed 0 page(s) and 0 chunk(s)" for the
        // twelve minutes its 424 pages took).
        if (run.RagState is { } ragState
            && (ragState.Equals("pending", StringComparison.OrdinalIgnoreCase)
                || ragState.Equals("running", StringComparison.OrdinalIgnoreCase)))
        {
            return "its crawl is still being indexed; check again when it finishes";
        }

        // Counted, not merely present. The whole point of this check is that a row in the index is
        // not evidence, and a threshold is what separates the two.
        var pages = run.RagPagesEnglish ?? 0;
        var chunks = run.RagChunksUpserted ?? 0;
        // Multiplied rather than divided: pages can be zero, and the page floor is checked in the
        // same expression, so a division here would be the one branch that could throw.
        if (pages < MinIndexedPages || chunks < pages * MinChunksPerPage)
        {
            // The reject count is added to the MESSAGE, deliberately not to the decision. It
            // answers the operator's next question -- why is the count this low -- and a crawl that
            // threw away 460 error pages reads very differently from one that simply found 40.
            // Making it a gate would change which runs are usable, which is a separate policy call
            // from the recalibration above: that moved where the bar sits, this would add a bar.
            var rejected = run.RagPagesSkippedUnusable ?? 0;
            var because = rejected > 0
                ? $" ({rejected} page(s) were rejected as not citable -- error pages, robots-denied, "
                    + "or non-English locale paths)"
                : "";
            return $"its crawl indexed {pages} page(s) and {chunks} chunk(s), below the "
                + $"{MinIndexedPages} pages and {MinChunksPerPage} chunks per page a page can be "
                + "written from"
                + because;
        }

        return null;
    }
}
