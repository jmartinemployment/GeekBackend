using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

public interface IGccV2GeekCrawlerReadRepository
{
    Task<GeekCrawlerRunDto?> GetLatestRunAsync(
        string ownerUserId,
        string crawlType,
        string seedsJson,
        CancellationToken ct = default);

    Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesAsync(
        Guid runId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default);
}

public sealed class GccV2GeekCrawlerReadRepository(HttpGeekCrawlerRepository inner) : IGccV2GeekCrawlerReadRepository
{
    public Task<GeekCrawlerRunDto?> GetLatestRunAsync(
        string ownerUserId,
        string crawlType,
        string seedsJson,
        CancellationToken ct = default) =>
        inner.GetLatestRunAsync(ownerUserId, crawlType, seedsJson, ct);

    public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesAsync(
        Guid runId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default) =>
        inner.ListPagesAsync(runId, limit, offset, ct);
}

/// <summary>
/// Read-only bridge to Geek-Crawler for partner/tools and competitor research at preflight/generate.
/// Does not crawl inline — fails closed when a required external run is missing or incomplete.
/// </summary>
public sealed class GccV2GeekCrawlerResearchResolver
{
    private const int PageBatchSize = 100;

    private readonly IGccV2GeekCrawlerReadRepository _crawlerRepo;
    private readonly ILogger<GccV2GeekCrawlerResearchResolver> _logger;

    public GccV2GeekCrawlerResearchResolver(
        IGccV2GeekCrawlerReadRepository crawlerRepo,
        ILogger<GccV2GeekCrawlerResearchResolver> logger)
    {
        _crawlerRepo = crawlerRepo;
        _logger = logger;
    }

    public async Task<string?> MergeExternalResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        CancellationToken ct)
    {
        rawBriefJson = await MergePartnerResearchAsync(ownerUserId, rawBriefJson, projectSiteUrl, ct);
        rawBriefJson = await MergeCompetitorResearchAsync(ownerUserId, rawBriefJson, ct);
        return rawBriefJson;
    }

    public async Task<string?> MergePartnerResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        CancellationToken ct)
    {
        var seeds = CollectExternalPartnerSeeds(rawBriefJson, projectSiteUrl);
        if (seeds.Count == 0) return rawBriefJson;

        var pages = await ResolveQuoteablePagesAsync(ownerUserId, CrawlTypes.Partner, seeds, ct);
        _logger.LogInformation(
            "Merged {Count} partner research page(s) from Geek-Crawler partner run for {SeedCount} seed(s).",
            pages.Count,
            seeds.Count);

        return GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(rawBriefJson, pages);
    }

    public async Task<string?> MergeCompetitorResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        CancellationToken ct)
    {
        var seeds = GccV2PartnerUrlResearchService.CollectCompetitorHrefs(rawBriefJson);
        if (seeds.Count == 0) return rawBriefJson;

        var pages = await ResolveQuoteablePagesAsync(ownerUserId, CrawlTypes.Competitors, seeds, ct);
        _logger.LogInformation(
            "Merged {Count} competitor research page(s) from Geek-Crawler competitors run for {SeedCount} seed(s).",
            pages.Count,
            seeds.Count);

        return GccV2PartnerUrlResearchService.MergeCompetitorResearchIntoBriefJson(rawBriefJson, pages);
    }

    internal async Task<IReadOnlyList<GccQuoteablePage>> ResolveQuoteablePagesAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(seeds);
        if (normalized.Count == 0)
            throw new InvalidOperationException($"No valid seed URLs for Geek-Crawler {crawlType} lookup.");

        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var run = await _crawlerRepo.GetLatestRunAsync(ownerUserId, crawlType, seedsJson, ct);
        if (run is null)
        {
            throw new InvalidOperationException(
                $"No Geek-Crawler {crawlType} run found for the requested URLs — start the crawl in Geek-Crawler, then retry.");
        }

        if (!string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Geek-Crawler {crawlType} run is \"{run.Status}\" — wait for completion in Geek-Crawler, then retry.");
        }

        var seedSet = new HashSet<string>(normalized, StringComparer.OrdinalIgnoreCase);
        var storedPages = await LoadAllPagesAsync(run.Id, ct);

        var quoteable = new List<GccQuoteablePage>();
        foreach (var page in storedPages)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;
            if (!PageMatchesSeed(page, seedSet)) continue;

            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(url, page.Html);
            if (!GccV2ArticleHtmlExtractor.IsEmpty(extracted))
                quoteable.Add(extracted);
        }

        if (quoteable.Count == 0)
        {
            throw new InvalidOperationException(
                $"Geek-Crawler {crawlType} run completed but no extractable pages matched the requested URLs.");
        }

        return quoteable;
    }

    /// <summary>
    /// Partner seeds for Geek-Crawler — external tool URLs only.
    /// On-site <c>/tools/…</c> pages on the project site are covered by the owned project-site crawl.
    /// </summary>
    internal static IReadOnlyList<string> CollectExternalPartnerSeeds(
        string? rawBriefJson,
        string? projectSiteUrl)
    {
        var merged = new List<string>();
        foreach (var url in GccV2PartnerUrlResearchService.CollectPartnerHrefs(rawBriefJson)
                     .Concat(GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson)))
        {
            if (!IsExternalPartnerSeed(url, projectSiteUrl)) continue;
            if (!merged.Contains(url, StringComparer.OrdinalIgnoreCase))
                merged.Add(url);
        }

        return merged;
    }

    internal static bool IsExternalPartnerSeed(string url, string? projectSiteUrl)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (string.IsNullOrWhiteSpace(projectSiteUrl))
            return true;

        if (!GccV2HomepageUrl.TryNormalize(projectSiteUrl, out var homepage))
            return true;

        if (!Uri.TryCreate(homepage, UriKind.Absolute, out var site))
            return true;

        return !string.Equals(uri.Host, site.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PageMatchesSeed(GeekCrawlerPageDto page, HashSet<string> seedSet)
    {
        if (seedSet.Contains(page.Url) || seedSet.Contains(page.FinalUrl))
            return true;

        if (GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(page.Url, out var normalizedUrl)
            && seedSet.Contains(normalizedUrl))
            return true;

        if (GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(page.FinalUrl, out var normalizedFinal)
            && seedSet.Contains(normalizedFinal))
            return true;

        return false;
    }

    private async Task<IReadOnlyList<GeekCrawlerPageDto>> LoadAllPagesAsync(Guid runId, CancellationToken ct)
    {
        var all = new List<GeekCrawlerPageDto>();
        var offset = 0;
        while (true)
        {
            var batch = await _crawlerRepo.ListPagesAsync(runId, PageBatchSize, offset, ct);
            if (batch.Count == 0) break;
            all.AddRange(batch);
            if (batch.Count < PageBatchSize) break;
            offset += batch.Count;
        }

        return all;
    }
}
