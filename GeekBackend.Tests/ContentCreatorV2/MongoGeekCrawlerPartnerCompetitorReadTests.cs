using EphemeralMongo;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
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
        Assert.NotNull(partnerMerged.BriefJson);
        Assert.Contains("partnerResearch", partnerMerged.BriefJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(partnerMerged.PartnerResearchWarnings);

        var competitorMerged = await resolver.MergeCompetitorResearchAsync(
            owner,
            partnerMerged.BriefJson,
            CancellationToken.None);
        Assert.NotNull(competitorMerged.BriefJson);
        Assert.Contains("competitorResearch", competitorMerged.BriefJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(competitorMerged.PartnerResearchWarnings);

        var missing = await resolver.MergeCompetitorResearchAsync(
            owner,
            """{"competitorUrls":"https://missing-smoke.example/page"}""",
            CancellationToken.None);
        Assert.Single(missing.PartnerResearchWarnings);
        Assert.Contains("Competitor research", missing.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
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
            CancellationToken ct = default)
        {
            var run = await mongo.GetRunForSlotAsync(ownerUserId, crawlType, seedKey, ct);
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
                p.CrawledAtUtc);
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
}
