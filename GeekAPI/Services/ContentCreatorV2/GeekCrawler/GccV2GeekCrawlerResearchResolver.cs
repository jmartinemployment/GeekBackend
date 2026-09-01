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
    Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
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

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default) =>
        repo.ListProjectSiteCrawlPagesBySeedsAsync(runId, seedUrls, ct);
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

    Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default);

    Task<GeekCrawlerRunDto?> GetRunForSlotAsync(
        string ownerUserId,
        string crawlType,
        string seedKey,
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

    public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default) =>
        inner.ListPagesBySeedsAsync(runId, seedUrls, ct);

    public Task<GeekCrawlerRunDto?> GetRunForSlotAsync(
        string ownerUserId,
        string crawlType,
        string seedKey,
        CancellationToken ct = default) =>
        inner.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, ct);
}

/// <summary>
/// Partner/competitor research at generate: on-site tool pages from the owned project-site
/// crawl; external URLs from Geek-Crawler stored pages when available (partial runs OK).
/// Missing external research is warned and skipped — generate continues.
/// </summary>
public sealed class GccV2GeekCrawlerResearchResolver
{
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

    public async Task<GccV2ExternalResearchMergeResult> MergeExternalResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct)
    {
        var partner = await MergePartnerResearchAsync(
            ownerUserId,
            rawBriefJson,
            projectSiteUrl,
            projectSiteCrawlRunId,
            ct);
        var competitor = await MergeCompetitorResearchAsync(
            ownerUserId,
            partner.BriefJson,
            ct);

        return new GccV2ExternalResearchMergeResult(
            competitor.BriefJson,
            partner.PartnerResearchWarnings.Concat(competitor.PartnerResearchWarnings).ToList());
    }

    public async Task<GccV2ExternalResearchMergeResult> MergePartnerResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct)
    {
        var quoteable = new List<GccQuoteablePage>();
        var warnings = new List<string>();

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
        foreach (var seed in externalSeeds)
        {
            var (pages, warning) = await TryResolveExternalSeedAsync(
                ownerUserId,
                CrawlTypes.Partner,
                seed,
                ct);
            if (pages.Count > 0)
                quoteable.AddRange(pages);
            else if (warning is not null)
                warnings.Add(warning);
        }

        if (quoteable.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, warnings);

        _logger.LogInformation(
            "Merged {Count} partner research page(s) for project site {SiteUrl}.",
            quoteable.Count,
            projectSiteUrl);

        return new GccV2ExternalResearchMergeResult(
            GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(rawBriefJson, quoteable),
            warnings);
    }

    public async Task<GccV2ExternalResearchMergeResult> MergeCompetitorResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        CancellationToken ct)
    {
        var seeds = GccV2PartnerUrlResearchService.CollectCompetitorHrefs(rawBriefJson);
        if (seeds.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, []);

        var quoteable = new List<GccQuoteablePage>();
        var warnings = new List<string>();
        foreach (var seed in seeds)
        {
            var (pages, warning) = await TryResolveExternalSeedAsync(
                ownerUserId,
                CrawlTypes.Competitors,
                seed,
                ct);
            if (pages.Count > 0)
                quoteable.AddRange(pages);
            else if (warning is not null)
                warnings.Add(warning);
        }

        if (quoteable.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, warnings);

        _logger.LogInformation(
            "Merged {Count} competitor research page(s) from Geek-Crawler for {SeedCount} seed(s).",
            quoteable.Count,
            seeds.Count);

        return new GccV2ExternalResearchMergeResult(
            GccV2PartnerUrlResearchService.MergeCompetitorResearchIntoBriefJson(rawBriefJson, quoteable),
            warnings);
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
        var storedPages = await LoadProjectSitePagesAsync(projectSiteCrawlRunId, seedSet, ct);

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
        var quoteable = new List<GccQuoteablePage>();
        foreach (var seed in seeds)
        {
            var (pages, _) = await TryResolveExternalSeedAsync(ownerUserId, crawlType, seed, ct);
            quoteable.AddRange(pages);
        }

        return quoteable;
    }

    private async Task<(IReadOnlyList<GccQuoteablePage> Pages, string? Warning)> TryResolveExternalSeedAsync(
        string ownerUserId,
        string crawlType,
        string seed,
        CancellationToken ct)
    {
        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds([seed]);
        if (normalized.Count == 0)
            return ([], DescribeUnavailableResearch(seed, crawlType));

        var run = await FindRunForSeedsAsync(ownerUserId, crawlType, normalized, ct);
        if (run is null)
        {
            _logger.LogInformation(
                "No Geek-Crawler {CrawlType} run for {Seed}; skipping external research.",
                crawlType,
                seed);
            return ([], DescribeUnavailableResearch(seed, crawlType));
        }

        var seedSet = BuildSeedMatchSet(normalized);
        var quoteable = await ExtractQuoteableFromCrawlerPagesAsync(run.Id, seedSet, ct);
        if (quoteable.Count > 0)
        {
            if (!string.Equals(run.Status, "complete", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Using partial Geek-Crawler {CrawlType} run ({Status}) for {Seed}.",
                    crawlType,
                    run.Status,
                    seed);
            }

            return (quoteable, null);
        }

        _logger.LogInformation(
            "Geek-Crawler {CrawlType} run {RunId} ({Status}) has no extractable page for {Seed}.",
            crawlType,
            run.Id,
            run.Status,
            seed);
        return ([], DescribeUnavailableResearch(seed, crawlType));
    }

    private async Task<GeekCrawlerRunDto?> FindRunForSeedsAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> normalized,
        CancellationToken ct)
    {
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var run = await _crawlerRepo.GetLatestRunAsync(ownerUserId, crawlType, seedsJson, ct);
        if (run is null && normalized.Count == 1)
        {
            var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(normalized);
            run = await _crawlerRepo.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, ct);
        }

        return run;
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

    internal static string DescribeUnavailableResearch(string seed, string crawlType)
    {
        var host = Uri.TryCreate(seed, UriKind.Absolute, out var uri) ? uri.Host : seed;
        return crawlType == CrawlTypes.Competitors
            ? $"Competitor research for {host} unavailable (external crawl did not finish). Continuing without it."
            : $"Partner research for {host} unavailable (external crawl did not finish). Continuing without it.";
    }

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

    private async Task<List<GccQuoteablePage>> ExtractQuoteableFromCrawlerPagesAsync(
        Guid runId,
        HashSet<string> seedSet,
        CancellationToken ct)
    {
        var lookupUrls = ExpandUrlLookupVariants(seedSet);
        var storedPages = await _crawlerRepo.ListPagesBySeedsAsync(runId, lookupUrls, ct);

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

        return quoteable;
    }

    private async Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> LoadProjectSitePagesAsync(
        Guid runId,
        HashSet<string> seedSet,
        CancellationToken ct)
    {
        var lookupUrls = ExpandUrlLookupVariants(seedSet);
        return await _projectSitePages.ListProjectSiteCrawlPagesBySeedsAsync(runId, lookupUrls, ct);
    }

    internal static List<string> ExpandUrlLookupVariants(IEnumerable<string> seeds)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed)) continue;
            set.Add(seed);
            if (GeekCrawlerSeedNormalizer.TryNormalizeSeedUrl(seed, out var normalized))
                set.Add(normalized);
            var trimmed = seed.TrimEnd('/');
            set.Add(trimmed);
            set.Add(trimmed + "/");
        }

        return set.ToList();
    }
}
