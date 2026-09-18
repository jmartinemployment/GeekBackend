using EphemeralMongo;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;
using GeekRepository.Data.Entities.GeekCrawler;
using GeekRepository.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Live Mongo smoke for partner + competitor research reads.
/// Uses EphemeralMongo so CI/local can verify without Docker / MONGO_CRAWLER_URL.
/// When MONGO_CRAWLER_URL is set, tests also smoke against that instance (isolated DB name).
/// </summary>
public sealed class MongoGeekCrawlerPartnerCompetitorReadTests : IAsyncLifetime
{
    private IMongoRunner? _runner;
    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        var envUrl = Environment.GetEnvironmentVariable("MONGO_CRAWLER_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            _connectionString = envUrl.Trim();
            return;
        }

        _runner = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            Version = MongoVersion.V7,
            Edition = MongoEdition.Community,
            AdditionalArguments = ["--quiet"],
        });
        _connectionString = _runner.ConnectionString;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ListPagesBySeeds_and_GetLatestRun_roundtrip_partner_and_competitors()
    {
        var mongo = CreateMongo();
        const string owner = "mongo-smoke-user";
        var partnerUrl = "https://partner-smoke.example/tools";
        var competitorUrl = "https://rival-smoke.example/pricing";

        var partnerRun = await SeedRunWithPageAsync(
            mongo,
            owner,
            CrawlTypes.Partner,
            partnerUrl,
            "<html><head><title>Partner Smoke</title></head><body><h1>Partner Smoke</h1><p>This is a long enough paragraph for extraction from partner smoke page content.</p></body></html>");

        var competitorRun = await SeedRunWithPageAsync(
            mongo,
            owner,
            CrawlTypes.Competitors,
            competitorUrl,
            "<html><head><title>Rival Smoke</title></head><body><h1>Rival Smoke</h1><p>This is a long enough paragraph for extraction from competitor smoke page content.</p></body></html>");

        var partnerSeedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds([partnerUrl]);
        var competitorSeedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds([competitorUrl]);

        var latestPartner = await mongo.GetLatestRunAsync(owner, CrawlTypes.Partner, partnerSeedsJson);
        Assert.NotNull(latestPartner);
        Assert.Equal(partnerRun.Id, latestPartner!.Id);

        var latestCompetitor = await mongo.GetLatestRunAsync(owner, CrawlTypes.Competitors, competitorSeedsJson);
        Assert.NotNull(latestCompetitor);
        Assert.Equal(competitorRun.Id, latestCompetitor!.Id);

        var partnerPages = await mongo.ListPagesBySeedsAsync(partnerRun.Id, [partnerUrl]);
        Assert.Single(partnerPages);
        Assert.Contains("Partner Smoke", partnerPages[0].Html, StringComparison.Ordinal);

        var competitorPages = await mongo.ListPagesBySeedsAsync(competitorRun.Id, [competitorUrl]);
        Assert.Single(competitorPages);
        Assert.Contains("Rival Smoke", competitorPages[0].Html, StringComparison.Ordinal);

        // Cleanup when hitting a shared MONGO_CRAWLER_URL so we don't leave smoke docs behind.
        if (_runner is null)
        {
            await mongo.DeleteRunCrawlDataAsync(partnerRun.Id);
            await mongo.DeleteRunCrawlDataAsync(competitorRun.Id);
        }
    }

    [Fact]
    public async Task Resolver_merges_partner_and_competitor_research_from_mongo()
    {
        var mongo = CreateMongo();
        const string owner = "mongo-resolver-user";
        var partnerUrl = "https://pipedrive-smoke.example/";
        var competitorUrl = "https://hubspot-smoke.example/crm";

        await SeedRunWithPageAsync(
            mongo,
            owner,
            CrawlTypes.Partner,
            partnerUrl,
            "<html><head><title>Pipedrive</title></head><body><h1>Pipedrive</h1><p>This is a long enough paragraph for extraction from the partner homepage content during mongo smoke.</p></body></html>");

        await SeedRunWithPageAsync(
            mongo,
            owner,
            CrawlTypes.Competitors,
            competitorUrl,
            "<html><head><title>HubSpot</title></head><body><h1>HubSpot</h1><p>This is a long enough paragraph for extraction from the competitor page content during mongo smoke.</p></body></html>");

        var resolver = new GccV2GeekCrawlerResearchResolver(
            new MongoReadRepo(mongo),
            new EmptyProjectSitePageReader(),
            new DisabledRagClient(),
            CompetitorExtractionTestDoubles.Inert(),
            CompetitorExtractionTestDoubles.InertPartner(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

        var brief = $$"""
            {
              "operatorTools": [{ "name": "Pipedrive", "url": "{{partnerUrl}}" }],
              "competitorUrls": "{{competitorUrl}}"
            }
            """;

        var partnerMerged = await resolver.MergePartnerResearchAsync(
            owner,
            brief,
            "https://geekatyourspot.com",
            null,
            CancellationToken.None);
        Assert.Equal(brief, partnerMerged.BriefJson);
        Assert.DoesNotContain("partnerResearch", partnerMerged.BriefJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(partnerMerged.PartnerResearchWarnings);
        Assert.Contains("research library is disabled", partnerMerged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);

        var competitorMerged = await resolver.MergeCompetitorResearchAsync(
            owner,
            brief,
            CancellationToken.None);
        Assert.Equal(brief, competitorMerged.BriefJson);
        Assert.DoesNotContain("competitorResearch", competitorMerged.BriefJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(competitorMerged.PartnerResearchWarnings);
        Assert.Contains("research library is disabled", competitorMerged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);

        var missing = await resolver.MergeCompetitorResearchAsync(
            owner,
            """{"competitorUrls":"https://missing-smoke.example/page"}""",
            CancellationToken.None);
        Assert.Single(missing.PartnerResearchWarnings);
        Assert.Contains("Competitor research", missing.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestRunContainingSeed_finds_a_seed_crawled_as_part_of_a_larger_batch_run()
    {
        var mongo = CreateMongo();
        const string owner = "mongo-batch-user";
        const string crawlType = CrawlTypes.Competitors;
        var seeds = new[]
        {
            "https://batch-one.example/",
            "https://batch-two.example/pricing",
            "https://batch-three.example/",
        };

        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(seeds);
        Assert.Equal(seeds.Length, normalized.Count);
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(normalized);

        var run = await mongo.CreateRunAsync(new GeekCrawlerRun
        {
            OwnerUserId = owner,
            CrawlType = crawlType,
            Status = "complete",
            SeedUrlsJson = seedsJson,
            SeedKey = seedKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        });

        var targetSeed = normalized[1];

        // Exact whole-set lookups (as used before this fix) must NOT find a batch run from a single seed.
        var exactSeedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds([targetSeed]);
        var exactBySeedsJson = await mongo.GetLatestRunAsync(owner, crawlType, exactSeedsJson);
        Assert.Null(exactBySeedsJson);

        var exactSeedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey([targetSeed]);
        var exactBySlot = await mongo.GetRunForSlotAsync(owner, crawlType, exactSeedKey);
        Assert.Null(exactBySlot);

        // The new containment lookup must find it.
        var containing = await mongo.GetLatestRunContainingSeedAsync(owner, crawlType, targetSeed);
        Assert.NotNull(containing);
        Assert.Equal(run.Id, containing!.Id);

        // A seed that merely shares a prefix must NOT false-positive match.
        var noMatch = await mongo.GetLatestRunContainingSeedAsync(
            owner, crawlType, "https://batch-two.example/pricing.evil.com");
        Assert.Null(noMatch);

        if (_runner is null)
            await mongo.DeleteRunCrawlDataAsync(run.Id);
    }

    [Fact]
    public async Task Resolver_resolves_competitor_seed_that_was_crawled_as_part_of_a_batch_run()
    {
        var mongo = CreateMongo();
        const string owner = "mongo-batch-resolver-user";
        var seeds = new[]
        {
            "https://batch-rival-one.example/",
            "https://batch-rival-two.example/pricing",
        };
        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds(seeds);
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(normalized);

        var run = await mongo.CreateRunAsync(new GeekCrawlerRun
        {
            OwnerUserId = owner,
            CrawlType = CrawlTypes.Competitors,
            Status = "complete",
            SeedUrlsJson = seedsJson,
            SeedKey = seedKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        });

        var resolver = new GccV2GeekCrawlerResearchResolver(
            new MongoReadRepo(mongo),
            new EmptyProjectSitePageReader(),
            new DisabledRagClient(),
            CompetitorExtractionTestDoubles.Inert(),
            CompetitorExtractionTestDoubles.InertPartner(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

        var targetSeed = normalized[1];
        var brief = $$"""{"competitorUrls":"{{targetSeed}}"}""";

        var merged = await resolver.MergeCompetitorResearchAsync(owner, brief, CancellationToken.None);

        // Before the fix: "no usable crawl run or indexed library pages" (run not found at all).
        // After the fix: the run IS found via the batch-containment fallback, so the resolver gets far
        // enough to hit the (unrelated) "research library is disabled" warning instead.
        Assert.Single(merged.PartnerResearchWarnings);
        Assert.Contains("research library is disabled", merged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no usable crawl run", merged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);

        if (_runner is null)
            await mongo.DeleteRunCrawlDataAsync(run.Id);
    }

    private MongoGeekCrawlerService CreateMongo() =>
        new(_connectionString, NullLogger<MongoGeekCrawlerService>.Instance);

    private static async Task<GeekCrawlerRun> SeedRunWithPageAsync(
        IMongoGeekCrawlerService mongo,
        string ownerUserId,
        string crawlType,
        string seedUrl,
        string html)
    {
        var normalized = GeekCrawlerSeedNormalizer.NormalizeSeeds([seedUrl]);
        Assert.Single(normalized);
        var seedsJson = GeekCrawlerSeedNormalizer.SerializeSeeds(normalized);
        var seedKey = GeekCrawlerSeedNormalizer.ComputeSeedKey(normalized);

        var run = await mongo.CreateRunAsync(new GeekCrawlerRun
        {
            OwnerUserId = ownerUserId,
            CrawlType = crawlType,
            Status = "complete",
            SeedUrlsJson = seedsJson,
            SeedKey = seedKey,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            StartedAtUtc = DateTimeOffset.UtcNow,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        });

        var origin = GeekCrawlerSeedNormalizer.NormalizeOriginAuthority(
            new Uri(normalized[0]).GetLeftPart(UriPartial.Authority));

        await mongo.CreatePagesBatchAsync(
            run.Id,
            [
                new GeekCrawlerPage
                {
                    RunId = run.Id,
                    Origin = origin,
                    Url = normalized[0],
                    FinalUrl = normalized[0],
                    StatusCode = 200,
                    RobotsAllowed = true,
                    Html = html,
                    CrawledAtUtc = DateTimeOffset.UtcNow,
                },
            ]);

        return run;
    }

    private sealed class MongoReadRepo(IMongoGeekCrawlerService mongo) : IGccV2GeekCrawlerReadRepository
    {
        public async Task<GeekCrawlerRunDto?> GetLatestRunAsync(
            string ownerUserId,
            string crawlType,
            string seedsJson,
            CancellationToken ct = default)
        {
            var run = await mongo.GetLatestRunAsync(ownerUserId, crawlType, seedsJson, ct);
            return run is null ? null : ToDto(run);
        }

        public async Task<GeekCrawlerRunDto?> GetRunForSlotAsync(
            string ownerUserId,
            string crawlType,
            string seedKey,
            bool publishedOnly = false,
            CancellationToken ct = default)
        {
            var run = await mongo.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, publishedOnly, ct);
            return run is null ? null : ToDto(run);
        }

        public async Task<GeekCrawlerRunDto?> GetLatestRunContainingSeedAsync(
            string ownerUserId,
            string crawlType,
            string seed,
            CancellationToken ct = default)
        {
            var run = await mongo.GetLatestRunContainingSeedAsync(ownerUserId, crawlType, seed, ct);
            return run is null ? null : ToDto(run);
        }

        public async Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesAsync(
            Guid runId,
            int limit = 100,
            int offset = 0,
            CancellationToken ct = default)
        {
            var pages = await mongo.ListPagesByRunAsync(runId, limit, offset, ct);
            return pages.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId,
            IReadOnlyList<string> seedUrls,
            CancellationToken ct = default)
        {
            var pages = await mongo.ListPagesBySeedsAsync(runId, seedUrls, ct);
            return pages.Select(ToDto).ToList();
        }

        private static GeekCrawlerRunDto ToDto(GeekCrawlerRun r) =>
            new(
                r.Id,
                r.OwnerUserId,
                r.CrawlType,
                r.Status,
                r.SeedUrlsJson,
                r.SeedKey,
                r.HostProgressJson,
                r.ErrorSummary,
                r.CreatedAtUtc,
                r.StartedAtUtc,
                r.CompletedAtUtc);

        private static GeekCrawlerPageDto ToDto(GeekCrawlerPage p) =>
            new(
                p.Id,
                p.RunId,
                p.Origin,
                p.Url,
                p.FinalUrl,
                p.StatusCode,
                p.RobotsAllowed,
                p.Html,
                p.FailureReason,
                p.CrawledAtUtc,
                p.Title,
                p.Excerpt);
    }

    private sealed class EmptyProjectSitePageReader : IGccV2ProjectSitePageReader
    {
        public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
            Guid runId,
            int limit,
            int offset,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>>([]);

        public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
            Guid runId,
            IReadOnlyList<string> seedUrls,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>>([]);
    }

    private sealed class DisabledRagClient : IGeekCrawlerRagClient
    {
        public bool IsEnabled => false;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(
            Guid runId,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagIndexStatus?> GetIndexStatusAsync(
            Guid runId,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need,
            Guid runId,
            string? crawlType = null,
            string? host = null,
            int topK = 8,
            bool? preferParent = null,
            bool? preferChild = null,
            IReadOnlyList<string>? entityNames = null,
            string? retrievalMode = null,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagQueryResult?>(null);

        public Task<GeekCrawlerRagTemplateIndexResult?> IndexTemplatesAsync(
            IReadOnlyList<GeekCrawlerRagTemplateDto> templates,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateIndexResult?>(null);

        public Task<GeekCrawlerRagTemplateQueryResult?> QueryTemplatesAsync(
            string need,
            int topK = 5,
            string? channel = null,
            IReadOnlyList<string>? entityTags = null,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagTemplateQueryResult?>(null);

        public Task<GeekCrawlerRagPageText?> GetPageTextAsync(
            string pageId,
            CancellationToken ct = default,
            string? runId = null) =>
            Task.FromResult<GeekCrawlerRagPageText?>(null);

        public Task<GeekCrawlerRagCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) =>
            throw new CapabilitiesUnavailableException("Geek-Crawler-Rag is disabled in this test double.");
    }
}
