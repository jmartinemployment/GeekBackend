using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

/// <summary>
/// K2 project-site required grounding: seed HTML must be present and extractable.
/// </summary>
public static class GccV2ProjectSiteGrounding
{
    /// <summary>
    /// A crawl is only grounding once it has committed. Until then the page set is whatever happened
    /// to have landed so far, and grounding a piece against 3 pages of a 2,500-page site is not a
    /// smaller success — it is a wrong answer delivered confidently.
    ///
    /// EnsureUsableSeedHtml cannot catch this: one page with good HTML satisfies it. The run status
    /// is the only thing that distinguishes "this site has 3 pages" from "this crawl is 3 pages in".
    /// </summary>
    public static void EnsureRunCommitted(Guid runId, string? runStatus)
    {
        if (runId == Guid.Empty)
            throw new InvalidOperationException(
                "Missing required project-site crawl run id — start from a project-site crawl.");

        if (runStatus is null)
            throw new InvalidOperationException(
                $"Project-site crawl {runId:D} was not found — it cannot ground a create.");

        if (!string.Equals(runStatus, "complete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Project-site crawl {runId:D} is '{runStatus}', not 'complete' — a crawl grounds "
                + "nothing until it has finished. Wait for the crawl or re-run it.");
        }
    }

    public static void EnsureUsableSeedHtml(
        Guid runId,
        IReadOnlyList<GccV2ProjectSiteCrawlPageDto> pages)
    {
        if (runId == Guid.Empty)
            throw new InvalidOperationException(
                "Missing required project-site crawl run id — start from a project-site crawl.");

        var candidates = pages
            .Where(p => !string.IsNullOrWhiteSpace(p.Html)
                        && p.StatusCode is >= 200 and < 300)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"Project-site crawl {runId:D} has no usable seed HTML — re-crawl the project site or wait for index.");
        }

        var anyExtractable = false;
        foreach (var page in candidates)
        {
            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(url, page.Html!);
            if (GccV2ArticleHtmlExtractor.IsEmpty(extracted)) continue;
            // Stamp to enforce digest/fail-closed on empty body.
            _ = GccV2SeedHtmlProvenance.StampSeedHtml(
                extracted, page.RunId, page.Id, page.Html, page.CrawledAtUtc);
            anyExtractable = true;
            break;
        }

        if (!anyExtractable)
        {
            throw new InvalidOperationException(
                $"Project-site crawl {runId:D} seed HTML could not be extracted — re-crawl the project site.");
        }
    }
}
