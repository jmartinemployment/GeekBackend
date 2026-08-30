using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>
/// Resolves Geek-Crawler <c>partner</c> runs and extracts quoteable pages from stored crawl HTML.
/// </summary>
public sealed class GeekCrawlerToolRunResolver
{
    private readonly HttpGeekCrawlerRepository _crawlerRepo;
    private readonly GeekCrawlerService _crawler;

    public GeekCrawlerToolRunResolver(HttpGeekCrawlerRepository crawlerRepo, GeekCrawlerService crawler)
    {
        _crawlerRepo = crawlerRepo;
        _crawler = crawler;
    }

    public async Task<GeekCrawlerRunDto?> ResolveRunForUserAsync(
        string ownerUserId,
        string? rawBriefJson,
        CancellationToken ct)
    {
        var seeds = GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson);
        if (seeds.Count == 0) return null;

        return await _crawler.FindInProgressRunAsync(ownerUserId, CrawlTypes.Partner, seeds, ct)
               ?? await FindLatestMatchingRunAsync(ownerUserId, seeds, ct).ConfigureAwait(false);
    }

    public async Task<GeekCrawlerRunDto> StartPartnerCrawlAsync(
        string ownerUserId,
        string? rawBriefJson,
        CancellationToken ct)
    {
        var seeds = GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson);
        if (seeds.Count == 0)
            throw new InvalidOperationException("No operator tool URLs — nothing to crawl.");

        return await _crawler.StartCrawlAsync(ownerUserId, CrawlTypes.Partner, seeds, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GccQuoteablePage>> ExtractPartnerResearchAsync(
        GeekCrawlerRunDto run,
        CancellationToken ct)
    {
        if (!string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase))
            return [];

        var quotePages = new List<GccQuoteablePage>();
        var offset = 0;
        const int pageSize = 100;

        while (true)
        {
            var batch = await _crawlerRepo.ListPagesAsync(run.Id, pageSize, offset, ct).ConfigureAwait(false);
            if (batch.Count == 0) break;

            foreach (var page in batch)
            {
                if (string.IsNullOrWhiteSpace(page.Html)) continue;
                var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(page.Url, page.Html);
                if (GccV2ArticleHtmlExtractor.IsEmpty(extracted)) continue;
                quotePages.Add(extracted);
            }

            if (batch.Count < pageSize) break;
            offset += batch.Count;
        }

        return quotePages;
    }

    private async Task<GeekCrawlerRunDto?> FindLatestMatchingRunAsync(
        string ownerUserId,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        var runs = await _crawlerRepo.ListRunsForUserAsync(ownerUserId, CrawlTypes.Partner, 50, ct)
            .ConfigureAwait(false);
        return runs.FirstOrDefault(r => GeekCrawlerSeedNormalizer.SeedUrlsMatch(r.SeedUrlsJson, seeds));
    }
}
