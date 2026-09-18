using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.GeekCrawler;
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

/// <summary>
/// Reads through <see cref="ProjectSite.IGccV2ProjectSitePageSource"/> so the store can move from
/// Postgres to the shared geek_crawler Mongo collection without touching this call path.
/// </summary>
internal sealed class GccV2ProjectSitePageReader(ProjectSite.IGccV2ProjectSitePageSource source)
    : IGccV2ProjectSitePageReader
{
    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
        Guid runId,
        int limit,
        int offset,
        CancellationToken ct = default) =>
        source.ListPagesAsync(runId, limit, offset, ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default) =>
        source.ListPagesBySeedsAsync(runId, seedUrls, ct);
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
        bool publishedOnly = false,
        CancellationToken ct = default);

    /// <summary>
    /// Finds the latest run for owner+crawlType whose seed set contains the given single normalized
    /// seed — resolves a URL that was crawled as part of a larger multi-seed batch run, where
    /// <see cref="GetLatestRunAsync"/>/<see cref="GetRunForSlotAsync"/> require an exact whole-set match.
    /// </summary>
    Task<GeekCrawlerRunDto?> GetLatestRunContainingSeedAsync(
        string ownerUserId,
        string crawlType,
        string seed,
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
        bool publishedOnly = false,
        CancellationToken ct = default) =>
        inner.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, publishedOnly, ct);

    public Task<GeekCrawlerRunDto?> GetLatestRunContainingSeedAsync(
        string ownerUserId,
        string crawlType,
        string seed,
        CancellationToken ct = default) =>
        inner.GetLatestRunContainingSeedAsync(ownerUserId, crawlType, seed, ct);
}

/// <summary>
/// Partner/competitor research at generate: on-site tool pages from the owned project-site
/// crawl; external partner/competitor URLs from Geek-Crawler-Rag library only (fail closed —
/// no Mongo seed-HTML substitute). External local seeds may still use crawl HTML extract.
/// </summary>
public sealed class GccV2GeekCrawlerResearchResolver
{
    private static readonly HashSet<string> IndexBuildingStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "running",
    };

    private static readonly HashSet<string> IndexQueryableStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "complete",
    };

    private readonly IGccV2GeekCrawlerReadRepository _crawlerRepo;
    private readonly IGccV2ProjectSitePageReader _projectSitePages;
    private readonly IGeekCrawlerRagClient _rag;
    private readonly GccV2CompetitorExtractionService _competitorExtraction;
    private readonly GccV2PartnerExtractionService _partnerExtraction;
    private readonly ILogger<GccV2GeekCrawlerResearchResolver> _logger;

    public GccV2GeekCrawlerResearchResolver(
        IGccV2GeekCrawlerReadRepository crawlerRepo,
        IGccV2ProjectSitePageReader projectSitePages,
        IGeekCrawlerRagClient rag,
        GccV2CompetitorExtractionService competitorExtraction,
        GccV2PartnerExtractionService partnerExtraction,
        ILogger<GccV2GeekCrawlerResearchResolver> logger)
    {
        _crawlerRepo = crawlerRepo;
        _projectSitePages = projectSitePages;
        _rag = rag;
        _competitorExtraction = competitorExtraction;
        _partnerExtraction = partnerExtraction;
        _logger = logger;
    }

    public async Task<GccV2ExternalResearchMergeResult> MergeExternalResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct,
        string? createTitle = null,
        string? targetKeyword = null)
    {
        var topic = BuildTopicContext(rawBriefJson, createTitle, targetKeyword);
        var partner = await MergePartnerResearchAsync(
            ownerUserId,
            rawBriefJson,
            projectSiteUrl,
            projectSiteCrawlRunId,
            ct,
            topic);
        var competitor = await MergeCompetitorResearchAsync(
            ownerUserId,
            partner.BriefJson,
            ct,
            topic);
        var enriched = await EnrichPartnerExtractionAfterCompetitorAsync(competitor.BriefJson, ct)
            .ConfigureAwait(false);
        var local = await MergeLocalResearchAsync(
            ownerUserId,
            enriched,
            projectSiteUrl,
            projectSiteCrawlRunId,
            ct,
            topic);

        return new GccV2ExternalResearchMergeResult(
            local.BriefJson,
            partner.PartnerResearchWarnings
                .Concat(competitor.PartnerResearchWarnings)
                .Concat(local.PartnerResearchWarnings)
                .ToList());
    }

    private Task<string?> EnrichPartnerExtractionAfterCompetitorAsync(
        string? rawBriefJson,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rawBriefJson))
            return Task.FromResult(rawBriefJson);

        var extraction = GccV2PartnerUrlResearchService.ParsePartnerExtraction(rawBriefJson);
        if (extraction is null)
            return Task.FromResult<string?>(rawBriefJson);

        var competitorPages = ParseResearchPages(rawBriefJson, "competitorResearch");
        var competitorExtraction = GccV2PartnerUrlResearchService.ParseCompetitorExtraction(rawBriefJson);
        var partnerNames = GccV2PartnerUrlResearchService.CollectPartnerToolRows(rawBriefJson)
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (partnerNames.Count == 0)
        {
            partnerNames = ParseResearchPages(rawBriefJson, "partnerResearch")
                .Select(p => p.Title)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Prefer structured competitor deficits when present; else paragraph scan.
        if (competitorExtraction is { DeficitRouter.Count: > 0 })
        {
            // W3: only deficits that completed the counterweight join may become Alternatives copy.
            // An unjoined deficit has no evidenced partner strength to point at, so it is not
            // back-filled with partnerNames — that back-fill was the invention path.
            var fromRouter = GccV2DeficitStrengthJoin.JoinedOnly(competitorExtraction)
                .Select(d => new GccPartnerAlternativesAsset(
                    d.TriggerDeficit,
                    d.RecommendedSwap,
                    d.PivotCopy ?? "",
                    GccV2DeficitStrengthJoin.ToPartnerProvenance(d.Provenance)))
                .ToList();
            var mergedAlts = extraction.Alternatives
                .Concat(fromRouter)
                .GroupBy(a => a.TriggerDeficit, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(40)
                .ToList();
            var joinedDoc = extraction with { Alternatives = mergedAlts };
            return Task.FromResult(
                GccV2PartnerUrlResearchService.MergePartnerExtractionIntoBriefJson(rawBriefJson, joinedDoc));
        }

        if (competitorPages.Count == 0)
            return Task.FromResult<string?>(rawBriefJson);

        var joined = GccV2PartnerAlternativesJoin.EnrichWithCompetitorDeficits(
            extraction, competitorPages, partnerNames);
        return Task.FromResult(
            GccV2PartnerUrlResearchService.MergePartnerExtractionIntoBriefJson(rawBriefJson, joined));
    }

    private static IReadOnlyList<GccQuoteablePage> ParseResearchPages(string rawBriefJson, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var el)
                || el.ValueKind != JsonValueKind.Array)
                return [];
            return el.Deserialize<List<GccQuoteablePage>>(
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<GccV2ExternalResearchMergeResult> MergeLocalResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct,
        RagTopicContext? topic = null)
    {
        topic ??= BuildTopicContext(rawBriefJson, null, null);
        var quoteable = new List<GccQuoteablePage>();
        var warnings = new List<string>();

        // Resolve on-site local seeds from the project-site crawl.
        if (projectSiteCrawlRunId is { } runId && runId != Guid.Empty)
        {
            var onSite = await ResolveOnSiteLocalSeedsAsync(
                runId,
                rawBriefJson,
                projectSiteUrl,
                ct);
            quoteable.AddRange(onSite);
        }

        // Resolve external local seeds (e.g., Google Business Profile, Yelp) from Geek-Crawler.
        var externalSeeds = CollectExternalLocalSeeds(rawBriefJson, projectSiteUrl);
        foreach (var seed in externalSeeds)
        {
            var (pages, warning, _) = await TryResolveExternalSeedAsync(
                ownerUserId,
                CrawlTypes.Local,
                seed,
                topic,
                ct);
            if (pages.Count > 0)
                quoteable.AddRange(pages);
            if (warning is not null)
                warnings.Add(warning);
        }

        if (quoteable.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, warnings);

        _logger.LogInformation(
            "Merged {Count} local research page(s) for {SeedCount} seed(s).",
            quoteable.Count,
            (projectSiteCrawlRunId != Guid.Empty ? 1 : 0) + externalSeeds.Count);

        return new GccV2ExternalResearchMergeResult(
            GccV2PartnerUrlResearchService.MergeLocalResearchIntoBriefJson(rawBriefJson, quoteable),
            warnings);
    }

    internal static IReadOnlyList<string> CollectLocalSeeds(string? rawBriefJson, string? projectSiteUrl)
    {
        var seeds = new List<string>();
        if (GccV2HomepageUrl.TryNormalize(projectSiteUrl, out var homepage))
            seeds.Add(homepage);

        if (string.IsNullOrWhiteSpace(rawBriefJson)) return seeds;

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (doc.RootElement.TryGetProperty("localBusinessUrls", out var urls) && urls.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in urls.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var url = item.GetString();
                    if (!string.IsNullOrWhiteSpace(url) && !seeds.Contains(url, StringComparer.OrdinalIgnoreCase))
                        seeds.Add(url.Trim());
                }
            }
        }
        catch (JsonException)
        {
            // ignore malformed brief
        }

        return seeds;
    }

    private async Task<IReadOnlyList<GccQuoteablePage>> ResolveOnSiteLocalSeedsAsync(
        Guid projectSiteCrawlRunId,
        string? rawBriefJson,
        string? projectSiteUrl,
        CancellationToken ct)
    {
        // Collect on-site local seeds (project homepage matches projectSiteUrl).
        var seeds = CollectLocalSeeds(rawBriefJson, projectSiteUrl)
            .Where(seed => !IsExternalPartnerSeed(seed, projectSiteUrl))
            .ToList();
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
            {
                quoteable.Add(GccV2SeedHtmlProvenance.StampSeedHtml(
                    extracted, page.RunId, page.Id, page.Html, page.CrawledAtUtc));
            }
        }

        return quoteable;
    }

    internal static IReadOnlyList<string> CollectExternalLocalSeeds(
        string? rawBriefJson,
        string? projectSiteUrl) =>
        CollectLocalSeeds(rawBriefJson, projectSiteUrl)
            .Where(url => IsExternalPartnerSeed(url, projectSiteUrl))
            .ToList();

    public async Task<GccV2ExternalResearchMergeResult> MergePartnerResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        string? projectSiteUrl,
        Guid? projectSiteCrawlRunId,
        CancellationToken ct,
        RagTopicContext? topic = null)
    {
        topic ??= BuildTopicContext(rawBriefJson, null, null);
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
        var selectedRunIds = new List<Guid>();
        var seenRunIds = new HashSet<Guid>();
        foreach (var seed in externalSeeds)
        {
            var (pages, warning, sourceRunId) = await TryResolveExternalSeedAsync(
                ownerUserId,
                CrawlTypes.Partner,
                seed,
                topic,
                ct);
            if (pages.Count > 0)
            {
                quoteable.AddRange(pages);
                if (sourceRunId is { } resolvedRunId
                    && resolvedRunId != Guid.Empty
                    && seenRunIds.Add(resolvedRunId))
                    selectedRunIds.Add(resolvedRunId);
            }
            if (warning is not null)
                warnings.Add(warning);
        }

        if (quoteable.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, warnings);

        _logger.LogInformation(
            "Merged {Count} partner research page(s) for project site {SiteUrl}.",
            quoteable.Count,
            projectSiteUrl);

        var toolNames = GccV2PartnerUrlResearchService.CollectPartnerToolRows(rawBriefJson)
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var extraction = await _partnerExtraction.ExtractFromPagesAsync(quoteable, toolNames, ct).ConfigureAwait(false);
        if (_rag.IsEnabled)
        {
            extraction = await GccV2PartnerExtractionVerify.VerifyAgainstLibraryAsync(
                extraction, _rag, rawBriefJson, ct).ConfigureAwait(false);
        }

        var briefWithResearch = GccV2PartnerUrlResearchService.MergePartnerResearchIntoBriefJson(
            rawBriefJson, quoteable);
        var briefWithExtraction = GccV2PartnerUrlResearchService.MergePartnerExtractionIntoBriefJson(
            briefWithResearch, extraction);

        _logger.LogInformation(
            "Partner extraction v{Version}: citables={Citables} (mdVerified={MdVerified}), ads={Ads}, pricing={Pricing}, faq={Faq}, offers={Offers}.",
            extraction.ExtractorVersion,
            extraction.Citables.Count,
            extraction.Citables.Count(c => c.Provenance.QuoteVerified),
            extraction.Advertisements.Count,
            extraction.PricingCatalog.Count,
            extraction.FaqBank.Count,
            extraction.OfferCtas.Count);

        return new GccV2ExternalResearchMergeResult(
            MergeSourceRunIds(
                briefWithExtraction,
                singularPropertyName: "partnerSourceRunId",
                pluralPropertyName: "partnerSourceRunIds",
                selectedRunIds),
            warnings);
    }

    public async Task<GccV2ExternalResearchMergeResult> MergeCompetitorResearchAsync(
        string ownerUserId,
        string? rawBriefJson,
        CancellationToken ct,
        RagTopicContext? topic = null)
    {
        topic ??= BuildTopicContext(rawBriefJson, null, null);
        var seeds = GccV2PartnerUrlResearchService.CollectCompetitorHrefs(rawBriefJson);
        if (seeds.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, []);

        var quoteable = new List<GccQuoteablePage>();
        var warnings = new List<string>();
        var selectedRunIds = new List<Guid>();
        var seenRunIds = new HashSet<Guid>();
        foreach (var seed in seeds)
        {
            var (pages, warning, sourceRunId) = await TryResolveExternalSeedAsync(
                ownerUserId,
                CrawlTypes.Competitors,
                seed,
                topic,
                ct);
            if (pages.Count > 0)
            {
                quoteable.AddRange(pages);
                if (sourceRunId is { } resolvedRunId
                    && resolvedRunId != Guid.Empty
                    && seenRunIds.Add(resolvedRunId))
                    selectedRunIds.Add(resolvedRunId);
            }
            if (warning is not null)
                warnings.Add(warning);
        }

        if (quoteable.Count == 0)
            return new GccV2ExternalResearchMergeResult(rawBriefJson, warnings);

        _logger.LogInformation(
            "Merged {Count} competitor research page(s) from Geek-Crawler for {SeedCount} seed(s).",
            quoteable.Count,
            seeds.Count);

        var partnerNames = GccV2PartnerUrlResearchService.CollectPartnerToolRows(rawBriefJson)
            .Select(r => r.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Competitor-native extraction: no partner extractor, no partner vocabulary, no relabelling.
        var extraction = await _competitorExtraction
            .ExtractFromPagesAsync(quoteable, seeds, ct).ConfigureAwait(false);
        if (_rag.IsEnabled)
        {
            extraction = await GccV2CompetitorExtractionVerify.VerifyAgainstLibraryAsync(
                extraction, _rag, rawBriefJson, ct).ConfigureAwait(false);
        }

        // GccCompetitorExtractionDocument no longer carries a SoftwareApplicationJsonLd cache field
        // (dead — computed, never emitted; see plans/rag-foundation-rewrite.md §0.000). Competitor
        // JSON-LD emission, if ever wired up, must go through a real publish path, not a discard-only
        // field on the extraction record.

        var briefWithResearch = GccV2PartnerUrlResearchService.MergeCompetitorResearchIntoBriefJson(
            rawBriefJson, quoteable);
        var briefWithExtraction = GccV2PartnerUrlResearchService.MergeCompetitorExtractionIntoBriefJson(
            briefWithResearch, extraction);

        _logger.LogInformation(
            "Competitor extraction v{Version}: boundaries={Boundaries}, deficits={Deficits}, framing={Framing}, claimRisk={ClaimRisk}, typeLabels={Types}.",
            extraction.ExtractorVersion,
            extraction.Disqualifiers.Count,
            extraction.DeficitRouter.Count,
            extraction.FramingBank.Count,
            extraction.ClaimRiskFlags.Count,
            extraction.TypeLabels.Count);

        return new GccV2ExternalResearchMergeResult(
            MergeSourceRunIds(
                briefWithExtraction,
                singularPropertyName: "competitorSourceRunId",
                pluralPropertyName: "competitorSourceRunIds",
                selectedRunIds),
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
            {
                quoteable.Add(GccV2SeedHtmlProvenance.StampSeedHtml(
                    extracted, page.RunId, page.Id, page.Html, page.CrawledAtUtc));
            }
        }

        return quoteable;
    }

    internal async Task<IReadOnlyList<GccQuoteablePage>> ResolveQuoteablePagesAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct,
        RagTopicContext? topic = null)
    {
        topic ??= RagTopicContext.Empty;
        var quoteable = new List<GccQuoteablePage>();
        foreach (var seed in seeds)
        {
            var (pages, _, _) = await TryResolveExternalSeedAsync(
                ownerUserId,
                crawlType,
                seed,
                topic,
                ct);
            quoteable.AddRange(pages);
        }

        return quoteable;
    }

    private async Task<(IReadOnlyList<GccQuoteablePage> Pages, string? Warning, Guid? RunId)> TryResolveExternalSeedAsync(
        string ownerUserId,
        string crawlType,
        string seed,
        RagTopicContext topic,
        CancellationToken ct)
    {
        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds([seed]);
        if (normalized.Count == 0)
            return ([], DescribeUnavailableResearch(seed, crawlType), null);

        var run = await FindRunForSeedsAsync(ownerUserId, crawlType, normalized, ct);
        if (run is null)
        {
            _logger.LogInformation(
                "No Geek-Crawler {CrawlType} run for {Seed}; skipping external research.",
                crawlType,
                seed);
            return ([], DescribeUnavailableResearch(seed, crawlType), null);
        }

        GccV2SeedHtmlProvenance.EnsureRunAuthorized(ownerUserId, run.OwnerUserId, run.Id);

        var seedSet = BuildSeedMatchSet(normalized);
        // All external Create research (partner, competitor, local) is library-only — no Mongo seed-HTML.
        if (!_rag.IsEnabled)
        {
            _logger.LogWarning(
                "Geek-Crawler-Rag disabled; cannot resolve {CrawlType} seed {Seed} (library-only, no seed HTML).",
                crawlType,
                seed);
            return ([], DescribeLibraryUnavailable(seed, crawlType, "research library is disabled"), run.Id);
        }

        var indexStatus = await _rag.GetIndexStatusAsync(run.Id, ct).ConfigureAwait(false);
        var indexState = indexStatus?.State;
        if (indexState is not null && IndexBuildingStates.Contains(indexState))
        {
            _logger.LogInformation(
                "Geek-Crawler-Rag index {State} for {CrawlType} run {RunId}; fail closed (no seed HTML).",
                indexState,
                crawlType,
                run.Id);
            return ([], DescribeIndexNotReady(seed, crawlType, indexState), run.Id);
        }

        if (indexState is not null
            && !IndexQueryableStates.Contains(indexState)
            && !string.Equals(indexState, "failed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(indexState, "skipped", StringComparison.OrdinalIgnoreCase))
        {
            return ([], DescribeLibraryUnavailable(seed, crawlType, $"index state '{indexState}' is not queryable"), run.Id);
        }

        // complete → query; failed/skipped/unknown → query once; empty = fail closed (no Mongo).
        var host = Uri.TryCreate(normalized[0], UriKind.Absolute, out var seedUri)
            ? seedUri.Host
            : null;
        var need = BuildRagNeed(topic, seed, crawlType);
        var rag = await _rag.QueryAsync(
            need: need,
            runId: run.Id,
            crawlType: crawlType,
            host: host,
            topK: 12,
            preferParent: true,
            preferChild: false,
            ct: ct).ConfigureAwait(false);
        if (rag is null || rag.Pages.Count == 0)
        {
            _logger.LogInformation(
                "Geek-Crawler-Rag returned no pages for {CrawlType} run {RunId} seed {Seed}; fail closed.",
                crawlType,
                run.Id,
                seed);
            return ([], DescribeLibraryUnavailable(seed, crawlType, "research index returned no pages for this seed"), run.Id);
        }

        var filtered = rag.Pages
            .Where(p => PageMatchesSeed(p.Url, seedSet) || HostMatchesSeed(p.Url, seedSet))
            .ToList();
        if (filtered.Count == 0)
        {
            _logger.LogInformation(
                "Geek-Crawler-Rag pages for {CrawlType} run {RunId} did not match seed {Seed}; fail closed (no unfiltered adopt).",
                crawlType,
                run.Id,
                seed);
            return ([], DescribeLibraryUnavailable(seed, crawlType, "no indexed pages matched this seed URL or host"), run.Id);
        }

        var stamped = filtered
            .Select(p => GccV2SeedHtmlProvenance.StampRagChunk(p, run.Id))
            .ToList();
        return (stamped, null, run.Id);
    }

    internal static string? MergeSourceRunIds(
        string? rawBriefJson,
        string singularPropertyName,
        string pluralPropertyName,
        IReadOnlyList<Guid> runIds)
    {
        var distinct = runIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (distinct.Count == 0) return rawBriefJson;
        JsonObject root;
        try
        {
            root = JsonNode.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson) as JsonObject
                ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var array = new JsonArray();
        foreach (var id in distinct)
            array.Add(id.ToString("D"));
        root[pluralPropertyName] = array;
        // Legacy singular field = first run (older readers / diagnostics).
        root[singularPropertyName] = distinct[0].ToString("D");
        return root.ToJsonString();
    }

    /// <summary>Legacy single-run writer — prefers <see cref="MergeSourceRunIds"/>.</summary>
    internal static string? MergeSourceRunId(string? rawBriefJson, string propertyName, Guid? runId)
    {
        if (runId is null || runId == Guid.Empty) return rawBriefJson;
        var plural = propertyName.EndsWith("Id", StringComparison.Ordinal)
            ? propertyName[..^2] + "Ids"
            : propertyName + "s";
        return MergeSourceRunIds(rawBriefJson, propertyName, plural, [runId.Value]);
    }

    /// <summary>Topic fields used to build the Geek-Crawler-Rag query <c>need</c>.</summary>
    public sealed record RagTopicContext(
        string? Title,
        string? TargetKeyword,
        string? ContentType,
        string? WritingNotes,
        string? Angle,
        string? PrimaryIntent)
    {
        public static RagTopicContext Empty { get; } = new(null, null, null, null, null, null);
    }

    public static RagTopicContext BuildTopicContext(
        string? rawBriefJson,
        string? createTitle,
        string? targetKeyword)
    {
        string? title = TrimOrNull(createTitle);
        string? keyword = TrimOrNull(targetKeyword);
        string? contentType = null;
        string? writingNotes = null;
        string? angle = null;
        string? primaryIntent = null;

        if (!string.IsNullOrWhiteSpace(rawBriefJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBriefJson);
                var root = doc.RootElement;
                title ??= ReadString(root, "title") ?? ReadString(root, "Title");
                keyword ??= ReadString(root, "targetKeyword") ?? ReadString(root, "TargetKeyword");
                writingNotes = ReadString(root, "writingNotes") ?? ReadString(root, "WritingNotes");
                angle = ReadString(root, "angle") ?? ReadString(root, "Angle");
                primaryIntent = ReadString(root, "primaryIntent") ?? ReadString(root, "PrimaryIntent");
                contentType = ReadString(root, "primaryDraft") ?? ReadString(root, "contentType");
                if (contentType is null
                    && root.TryGetProperty("contentTypes", out var types)
                    && types.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in types.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            contentType = value.Trim();
                            break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // ignore malformed brief — still use create title / keyword
            }
        }

        return new RagTopicContext(title, keyword, contentType, writingNotes, angle, primaryIntent);
    }

    internal static string BuildRagNeed(RagTopicContext topic, string seed, string crawlType)
    {
        var role = crawlType switch
        {
            CrawlTypes.Competitors => "competitor differentiation research",
            CrawlTypes.Local => "local business research",
            _ => "partner tool research",
        };

        var parts = new List<string> { role };
        if (!string.IsNullOrWhiteSpace(topic.Title))
            parts.Add($"article title: {Truncate(topic.Title!, 160)}");
        if (!string.IsNullOrWhiteSpace(topic.TargetKeyword))
            parts.Add($"target keyword: {Truncate(topic.TargetKeyword!, 120)}");
        if (!string.IsNullOrWhiteSpace(topic.ContentType))
            parts.Add($"content type: {Truncate(topic.ContentType!, 40)}");
        if (!string.IsNullOrWhiteSpace(topic.PrimaryIntent))
            parts.Add($"intent: {Truncate(topic.PrimaryIntent!, 60)}");
        if (!string.IsNullOrWhiteSpace(topic.Angle))
            parts.Add($"angle: {Truncate(topic.Angle!, 60)}");
        if (!string.IsNullOrWhiteSpace(topic.WritingNotes))
            parts.Add($"notes: {Truncate(topic.WritingNotes!, 240)}");
        parts.Add($"source site: {seed}");
        return string.Join("; ", parts);
    }

    internal static string DescribeIndexNotReady(string seed, string crawlType, string state)
    {
        var host = Uri.TryCreate(seed, UriKind.Absolute, out var uri) ? uri.Host : seed;
        var label = CrawlTypeLabel(crawlType);
        return $"{label} research index for {host} is still {state}; indexed library pages are required (no seed-HTML fallback).";
    }

    internal static string DescribeLibraryUnavailable(string seed, string crawlType, string reason)
    {
        var host = Uri.TryCreate(seed, UriKind.Absolute, out var uri) ? uri.Host : seed;
        var label = CrawlTypeLabel(crawlType);
        return $"{label} research for {host} unavailable ({reason}). Indexed library retrieval is required — no seed-HTML fallback.";
    }

    private static string CrawlTypeLabel(string crawlType) =>
        crawlType switch
        {
            _ when string.Equals(crawlType, CrawlTypes.Competitors, StringComparison.OrdinalIgnoreCase) => "Competitor",
            _ when string.Equals(crawlType, CrawlTypes.Local, StringComparison.OrdinalIgnoreCase) => "Local",
            _ => "Partner",
        };

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? TrimOrNull(el.GetString())
            : null;

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int maxChars) =>
        value.Length <= maxChars ? value : value[..maxChars];

    private static bool HostMatchesSeed(string pageUrl, HashSet<string> seedSet)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            return false;
        foreach (var seed in seedSet)
        {
            if (!Uri.TryCreate(seed, UriKind.Absolute, out var seedUri))
                continue;
            if (HostsMatch(pageUri.Host, seedUri.Host))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Preflight for the Create wizard: does each partner seed already have an indexed crawl run?
    ///
    /// Partner evidence is mandatory and nothing in Create crawls partners - the resolver looks up a
    /// run that already exists. Typing a URL is therefore not the requirement, so the wizard must be
    /// able to tell the operator which partners have evidence BEFORE they fill in the rest.
    ///
    /// Deliberately reuses the same steps as <c>TryResolveExternalSeedAsync</c> - normalize, find run,
    /// authorize, check index state - so the answer here cannot drift from what PLAN will decide.
    /// Never throws: an unknown seed reports as not ready with a reason.
    /// </summary>
    /// <summary>
    /// Confirms an entered URL by running the same retrieval the Create pipeline runs, rather than
    /// checking that a crawl run merely exists. The previous version returned ready=true whenever a
    /// run was present and not mid-index, so a crawl that fetched nothing, indexed nothing, or failed
    /// indexing still showed as confirmed - and then PLAN reported the seed unavailable. Presence is
    /// not fitness: the only thing worth confirming is that pages actually come back.
    ///
    /// Limit worth knowing: this probes with an empty topic, while PLAN queries with the create's real
    /// topic. A confirmed seed therefore means "this host has indexed, retrievable pages", not "PLAN
    /// will find pages for your specific topic".
    /// </summary>
    public async Task<IReadOnlyList<GccV2SeedReadiness>> CheckSeedReadinessAsync(
        string ownerUserId,
        string crawlType,
        IReadOnlyList<string> seeds,
        CancellationToken ct)
    {
        var results = new List<GccV2SeedReadiness>();
        foreach (var seed in seeds.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var (pages, warning, runId) = await TryResolveExternalSeedAsync(
                    ownerUserId, crawlType, seed, RagTopicContext.Empty, ct);

                string? indexState = null;
                if (runId is { } id && _rag.IsEnabled)
                    indexState = (await _rag.GetIndexStatusAsync(id, ct).ConfigureAwait(false))?.State;

                if (pages.Count > 0)
                {
                    results.Add(new(seed, true, runId, indexState, null));
                    continue;
                }

                results.Add(new(seed, false, runId, indexState,
                    warning ?? "No indexed pages are retrievable for this URL yet."));
            }
            catch (Exception cause)
            {
                _logger.LogWarning(cause, "Seed readiness check failed for {Seed}.", seed);
                results.Add(new(seed, false, null, null, "Could not check this URL."));
            }
        }

        return results;
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
            // Evidence must come from a committed crawl. Resolving a partner or competitor run that
            // is still in flight would cite whatever prefix of that site had landed, which is the
            // same defect as grounding a create on a partial project-site crawl.
            run = await _crawlerRepo.GetRunForSlotAsync(
                ownerUserId, crawlType, seedKey, publishedOnly: true, ct);
        }

        // Exact-set lookups above only match a run crawled for exactly this seed (or this exact seed
        // set). A seed that was crawled as part of a larger multi-seed batch run (e.g. five competitor
        // URLs crawled together in one job) has a different SeedKey/SeedUrlsJson and would otherwise
        // never resolve. Fall back to a containment lookup for the single-seed case.
        if (run is null && normalized.Count == 1)
        {
            run = await _crawlerRepo.GetLatestRunContainingSeedAsync(ownerUserId, crawlType, normalized[0], ct);
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
        var label = CrawlTypeLabel(crawlType);
        return $"{label} research for {host} unavailable (no usable crawl run or indexed library pages). Indexed library retrieval is required — no seed-HTML fallback.";
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
