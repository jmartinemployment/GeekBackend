using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.ContentCreatorV2.GeekCrawler;

public interface IGccV2ProjectSitePageReader
{
    Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
        Guid runId,
        int limit,
        int offset,
        CancellationToken ct = default);
}

internal sealed class GccV2ProjectSitePageReader(HttpGccV2Repository repo) : IGccV2ProjectSitePageReader
{
    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
        Guid runId,
        int limit,
        int offset,
        CancellationToken ct = default) =>
        repo.ListProjectSiteCrawlPagesAsync(runId, limit, offset, ct);
}

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
/// Partner/competitor research at preflight/generate: on-site tool pages from the owned project-site
/// crawl; external URLs from Geek-Crawler (soft-fail when missing on preflight/generate).
/// </summary>
public sealed class GccV2GeekCrawlerResearchResolver
{
    private const int PageBatchSize = 100;

    private readonly IGccV2GeekCrawlerReadRepository _crawlerRepo;
    private readonly IGccV2ProjectSitePageReader _projectSitePages;
    private readonly ILogger<GccV2GeekCrawlerResearchResolver> _logger;

    public GccV2GeekCrawlerResearchResolver(
        IGccV2GeekCrawlerReadRepository crawlerRepo,
        IGccV2ProjectSitePageReader projectSitePages,
        ILogger<GccV2GeekCrawlerResearchResolver> logger)
    {
        _crawlerRepo = crawlerRepo;
        _projectSitePages = projectSitePages;
        _logger = logger;
    }

    public async Task<string?> MergeExternalResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct)
    {
        rawBriefJson = await MergePartnerResearchAsync(
            ownerUserId,
            rawBriefJson,
            projectSiteUrl,
            projectSiteCrawlRunId,
            ct);
        rawBriefJson = await MergeCompetitorResearchAsync(ownerUserId, rawBriefJson, ct);
        return rawBriefJson;
    }

    public async Task<string?> MergePartnerResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct)
    {
        var quoteable = new List<GccQuoteablePage>();

        if (projectSiteCrawlRunId is { } runId && runId != Guid.Empty)
        {
            var onSite = await ResolveOnSiteQuoteablePagesAsync(
                runId,
                rawBriefJson,
                projectSiteUrl,
                ct);
            quoteable.AddRange(onSite);
        }

        var externalSeeds = CollectExternalPartnerSeeds(rawBriefJson, projectSiteUrl);
        if (externalSeeds.Count > 0)
        {
            try
            {
                var external = await ResolveQuoteablePagesAsync(
                    ownerUserId,
                    CrawlTypes.Partner,
                    externalSeeds,
                    ct);
                quoteable.AddRange(external);
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
                _logger.LogWarning(
                    ex,
                    "External Geek-Crawler partner merge skipped — {SeedCount} external seed(s) without a completed run.",
                    externalSeeds.Count);
            }
        }

        if (quoteable.Count == 0)
            return rawBriefJson;

        _logger.LogInformation(
            "Merged {Count} partner research page(s) for project site {SiteUrl}.",
            quoteable.Count,
            projectSiteUrl);

        return GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(rawBriefJson, quoteable);
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

    private async Task<IReadOnlyList<GccQuoteablePage>> ResolveOnSiteQuoteablePagesAsync(
        Guid projectSiteCrawlRunId,
        string? rawBriefJson,
        string? projectSiteUrl,
        CancellationToken ct)
    {
        var seeds = CollectOnSitePartnerSeeds(rawBriefJson, projectSiteUrl);
        if (seeds.Count == 0) return [];

        var seedSet = BuildSeedMatchSet(seeds);
        var storedPages = await LoadProjectSitePagesAsync(projectSiteCrawlRunId, ct);

        var quoteable = new List<GccQuoteablePage>();
        foreach (var page in storedPages)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;
            if (page.StatusCode is < 200 or >= 300) continue;

            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            if (!PageMatchesSeed(url, seedSet)) continue;

            var extracted = GccV2ArticleHtmlExtractor.ExtractPartnerPage(url, page.Html);
            if (!GccV2ArticleHtmlExtractor.IsEmpty(extracted))
                quoteable.Add(extracted);
        }

        return quoteable;
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

        var seedSet = BuildSeedMatchSet(normalized);
        var storedPages = await LoadAllCrawlerPagesAsync(run.Id, ct);

        var quoteable = new List<GccQuoteablePage>();
        foreach (var page in storedPages)
        {
            if (string.IsNullOrWhiteSpace(page.Html)) continue;

            var url = string.IsNullOrWhiteSpace(page.FinalUrl) ? page.Url : page.FinalUrl;
            if (!PageMatchesSeed(url, seedSet)) continue;

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

    internal static IReadOnlyList<string> CollectExternalPartnerSeeds(
        string? rawBriefJson,
        string? projectSiteUrl) =>
        CollectAllPartnerSeedUrls(rawBriefJson)
            .Where(url => IsExternalPartnerSeed(url, projectSiteUrl))
            .ToList();

    internal static IReadOnlyList<string> CollectOnSitePartnerSeeds(
        string? rawBriefJson,
        string? projectSiteUrl) =>
        CollectAllPartnerSeedUrls(rawBriefJson)
            .Where(url => !IsExternalPartnerSeed(url, projectSiteUrl))
            .ToList();

    private static IReadOnlyList<string> CollectAllPartnerSeedUrls(string? rawBriefJson)
    {
        var merged = new List<string>();
        foreach (var url in GccV2PartnerUrlResearchService.CollectPartnerHrefs(rawBriefJson)
                     .Concat(GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(rawBriefJson)))
        {
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

        return !HostsMatch(uri.Host, site.Host);
    }

    internal static bool HostsMatch(string left, string right) =>
        string.Equals(NormalizeHost(left), NormalizeHost(right), StringComparison.Ordinal);

    private static string NormalizeHost(string host) =>
        host.Trim().ToLowerInvariant().Replace("www.", "", StringComparison.Ordinal);

    private static HashSet<string> BuildSeedMatchSet(IEnumerable<string> seeds)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            set.Add(seed);
            if (GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(seed, out var normalized))
                set.Add(normalized);
        }

        return set;
    }

    private static bool PageMatchesSeed(string pageUrl, HashSet<string> seedSet)
    {
        if (seedSet.Contains(pageUrl))
            return true;

        return GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(pageUrl, out var normalized)
               && seedSet.Contains(normalized);
    }

    private async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> LoadProjectSitePagesAsync(
        Guid runId,
        CancellationToken ct)
    {
        var all = new List<GccV2ProjectSiteCrawlPageDto>();
        var offset = 0;
        while (true)
        {
            var batch = await _projectSitePages.ListProjectSiteCrawlPagesAsync(runId, PageBatchSize, offset, ct);
            if (batch.Count == 0) break;
            all.AddRange(batch);
            if (batch.Count < PageBatchSize) break;
            offset += batch.Count;
        }

        return all;
    }

    private async Task<IReadOnlyList<GeekCrawlerPageDto>> LoadAllCrawlerPagesAsync(Guid runId, CancellationToken ct)
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
