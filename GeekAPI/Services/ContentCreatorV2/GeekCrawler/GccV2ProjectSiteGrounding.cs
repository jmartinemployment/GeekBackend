using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

/// <summary>
/// K2 project-site required grounding: seed HTML must be present and extractable.
/// </summary>
public static class GccV2ProjectSiteGrounding
{
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
