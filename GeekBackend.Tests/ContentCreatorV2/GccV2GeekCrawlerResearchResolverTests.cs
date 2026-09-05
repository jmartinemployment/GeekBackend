using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
using GeekAPI.Services.GeekCrawler;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2GeekCrawlerResearchResolverTests
{
    [Fact]
    public async Task ResolveQuoteablePages_completeRun_returnsExtractedPages()
    {
        var runId = Guid.NewGuid();
        var repo = new FakeReadRepo
        {
            LatestRun = new GeekCrawlerRunDto(
                runId,
                "user-1",
                "partner",
                "complete",
                "[\"https://partner.example/tools\"]",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            Pages =
            [
                new GeekCrawlerPageDto(
                    Guid.NewGuid(),
                    runId,
                    "https://partner.example",
                    "https://partner.example/tools",
                    "https://partner.example/tools",
                    200,
                    true,
                    "<html><head><title>Partner Tool</title></head><body><h1>Partner Tool</h1><p>This is a long enough paragraph for extraction from partner page content.</p></body></html>",
                    null,
                    DateTimeOffset.UtcNow),
            ],
        };

        var resolver = CreateResolver(repo);
        var pages = await resolver.ResolveQuoteablePagesAsync(
            "user-1",
            "partner",
            ["https://partner.example/tools"],
            CancellationToken.None);

        Assert.Single(pages);
        Assert.Contains("Partner Tool", pages[0].Title);
        Assert.Equal(0, repo.ListPagesAsyncCallCount);
        Assert.Equal(1, repo.ListPagesBySeedsAsyncCallCount);
    }

    [Fact]
    public async Task ResolveQuoteablePages_failedRun_withMatchingPage_returnsExtractedPages()
    {
        var runId = Guid.NewGuid();
        var repo = new FakeReadRepo
        {
            LatestRun = new GeekCrawlerRunDto(
                runId,
                "user-1",
                "partner",
                "failed",
                "[\"https://www.pipedrive.com\"]",
                null,
                null,
                "System.OutOfMemoryException was thrown.",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            Pages =
            [
                new GeekCrawlerPageDto(
                    Guid.NewGuid(),
                    runId,
                    "https://www.pipedrive.com",
                    "https://www.pipedrive.com/",
                    "https://www.pipedrive.com/",
                    200,
                    true,
                    "<html><head><title>Pipedrive</title></head><body><h1>Pipedrive</h1><p>This is a long enough paragraph for extraction from the partner homepage content.</p></body></html>",
                    null,
                    DateTimeOffset.UtcNow),
            ],
        };

        var resolver = CreateResolver(repo);
        var pages = await resolver.ResolveQuoteablePagesAsync(
            "user-1",
            "partner",
            ["https://www.pipedrive.com"],
            CancellationToken.None);

        Assert.Single(pages);
        Assert.Contains("Pipedrive", pages[0].Title);
    }

    [Fact]
    public async Task MergePartnerResearch_merges_on_site_pages_from_project_crawl()
    {
        var crawlRunId = Guid.NewGuid();
        const string html =
            "<html><head><title>Fin.ai</title></head><body><h1>Fin.ai</h1><p>This is a long enough paragraph for extraction from the on-site tool page content.</p></body></html>";

        var resolver = CreateResolver(
            new FakeReadRepo(),
            new FakeProjectSitePageReader
            {
                Pages =
                [
                    new GccV2ProjectSiteCrawlPageDto(
                        Guid.NewGuid(),
                        crawlRunId,
                        "https://geekatyourspot.com",
                        "https://geekatyourspot.com/tools/fin",
                        "https://geekatyourspot.com/tools/fin",
                        200,
                        true,
                        html,
                        DateTimeOffset.UtcNow),
                ],
            });

        var brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "Fin.ai", "href": "https://geekatyourspot.com/tools/fin" }
                ]
              }
            }
            """;

        var merged = await resolver.MergePartnerResearchAsync(
            "user-1",
            brief,
            "https://geekatyourspot.com",
            crawlRunId,
            CancellationToken.None);

        Assert.NotNull(merged.BriefJson);
        Assert.Contains("partnerResearch", merged.BriefJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(merged.PartnerResearchWarnings);
    }

    [Fact]
    public async Task MergePartnerResearch_missing_external_run_warns_and_skips()
    {
        var resolver = CreateResolver(new FakeReadRepo());

        var brief = """
            {
              "operatorTools": [
                { "name": "Jotform", "url": "https://www.jotform.com/" }
              ]
            }
            """;

        var merged = await resolver.MergePartnerResearchAsync(
            "user-1",
            brief,
            "https://geekatyourspot.com",
            null,
            CancellationToken.None);

        Assert.Equal(brief, merged.BriefJson);
        Assert.Single(merged.PartnerResearchWarnings);
        Assert.Contains("jotform.com", merged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continuing without it", merged.PartnerResearchWarnings[0]);
    }

    [Fact]
    public async Task MergePartnerResearch_failedRun_withoutMatchingPage_warns_and_skips()
    {
        var runId = Guid.NewGuid();
        var resolver = CreateResolver(new FakeReadRepo
        {
            LatestRun = new GeekCrawlerRunDto(
                runId,
                "user-1",
                "partner",
                "failed",
                "[\"https://www.pipedrive.com\"]",
                null,
                null,
                "System.OutOfMemoryException was thrown.",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow),
            Pages = [],
        });

        var brief = """
            {
              "operatorTools": [
                { "name": "Pipedrive", "url": "https://www.pipedrive.com/" }
              ]
            }
            """;

        var merged = await resolver.MergePartnerResearchAsync(
            "user-1",
            brief,
            "https://geekatyourspot.com",
            null,
            CancellationToken.None);

        Assert.Equal(brief, merged.BriefJson);
        Assert.Single(merged.PartnerResearchWarnings);
        Assert.Contains("pipedrive.com", merged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergeLocalResearch_missing_external_run_warns_and_skips()
    {
        // Project-site host alone is not a Geek-Crawler local seed — only external
        // localBusinessUrls go through crawlType "local".
        var resolver = CreateResolver(new FakeReadRepo { LatestRun = null });
        const string brief = """
            {
              "title": "Miami HVAC",
              "localBusinessUrls": ["https://maps.example/biz/miami-hvac"]
            }
            """;

        var merged = await resolver.MergeLocalResearchAsync(
            "user-1",
            brief,
            "https://miami-hvac.example",
            null,
            CancellationToken.None);

        Assert.Equal(brief, merged.BriefJson);
        Assert.Single(merged.PartnerResearchWarnings);
        Assert.Contains("Local research", merged.PartnerResearchWarnings[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollectLocalSeeds_includes_project_site_and_brief_urls()
    {
        const string brief = """
            {"localBusinessUrls":["https://other-location.example"]}
            """;
        var seeds = GccV2GeekCrawlerResearchResolver.CollectLocalSeeds(
            brief,
            "https://miami-hvac.example/");

        Assert.Equal(2, seeds.Count);
        Assert.Contains(seeds, s => s.Contains("miami-hvac.example", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(seeds, s => s.Contains("other-location.example", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MergeCompetitorResearch_incompleteRun_warns_and_skips()
    {
        var runId = Guid.NewGuid();
        var resolver = CreateResolver(new FakeReadRepo
        {
            LatestRun = new GeekCrawlerRunDto(
                runId,
                "user-1",
                "competitors",
                "running",
                "[\"https://rival.example/page\"]",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null),
            Pages = [],
        });

        var brief = """
            {
              "competitorUrls": "https://rival.example/page"
            }
            """;

        var merged = await resolver.MergeCompetitorResearchAsync(
            "user-1",
            brief,
            CancellationToken.None);

        Assert.Equal(brief, merged.BriefJson);
        Assert.Single(merged.PartnerResearchWarnings);
        Assert.Contains("Competitor research", merged.PartnerResearchWarnings[0]);
    }

    [Fact]
    public void CollectExternalPartnerSeeds_excludes_project_site_host()
    {
        var brief = """
            {
              "operatorTools": [
                { "name": "Fin", "url": "https://geekatyourspot.com/tools/fin" },
                { "name": "Jotform", "url": "https://www.jotform.com/" }
              ]
            }
            """;

        var seeds = GccV2GeekCrawlerResearchResolver.CollectExternalPartnerSeeds(
            brief,
            "https://geekatyourspot.com");

        Assert.Single(seeds);
        Assert.Contains("jotform.com", seeds[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsExternalPartnerSeed_treats_www_project_host_as_on_site()
    {
        Assert.False(GccV2GeekCrawlerResearchResolver.IsExternalPartnerSeed(
            "https://www.geekatyourspot.com/tools/fin",
            "https://geekatyourspot.com"));
    }

    [Fact]
    public async Task ResolveQuoteablePages_missingRun_returnsEmpty()
    {
        var resolver = CreateResolver(new FakeReadRepo());

        var pages = await resolver.ResolveQuoteablePagesAsync(
            "user-1",
            "partner",
            ["https://partner.example/tools"],
            CancellationToken.None);

        Assert.Empty(pages);
    }

    [Fact]
    public async Task ResolveQuoteablePages_incompleteRun_withoutPages_returnsEmpty()
    {
        var repo = new FakeReadRepo
        {
            LatestRun = new GeekCrawlerRunDto(
                Guid.NewGuid(),
                "user-1",
                "partner",
                "running",
                "[\"https://partner.example/tools\"]",
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null),
        };
        var resolver = CreateResolver(repo);

        var pages = await resolver.ResolveQuoteablePagesAsync(
            "user-1",
            "partner",
            ["https://partner.example/tools"],
            CancellationToken.None);

        Assert.Empty(pages);
    }

    [Fact]
    public void DescribeUnavailableResearch_uses_creator_scope_copy()
    {
        var message = GccV2GeekCrawlerResearchResolver.DescribeUnavailableResearch(
            "https://www.pipedrive.com/",
            "partner");

        Assert.Contains("pipedrive.com", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continuing without it", message);
        Assert.DoesNotContain("page limit", message, StringComparison.OrdinalIgnoreCase);
    }

    private static GccV2GeekCrawlerResearchResolver CreateResolver(
        FakeReadRepo crawlerRepo,
        FakeProjectSitePageReader? projectSitePages = null) =>
        new(
            crawlerRepo,
            projectSitePages ?? new FakeProjectSitePageReader(),
            new DisabledGeekCrawlerRagClient(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

    private sealed class DisabledGeekCrawlerRagClient : IGeekCrawlerRagClient
    {
        public bool IsEnabled => false;

        public Task<GeekCrawlerRagIndexStatus?> EnqueueIndexAsync(
            Guid runId,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagIndexStatus?>(null);

        public Task<GeekCrawlerRagQueryResult?> QueryAsync(
            string need,
            Guid runId,
            string? crawlType = null,
            string? host = null,
            int topK = 8,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRagQueryResult?>(null);
    }

    private sealed class FakeReadRepo : IGccV2GeekCrawlerReadRepository
    {
        public GeekCrawlerRunDto? LatestRun { get; init; }
        public IReadOnlyList<GeekCrawlerPageDto> Pages { get; init; } = [];
        public int ListPagesAsyncCallCount { get; private set; }
        public int ListPagesBySeedsAsyncCallCount { get; private set; }

        public Task<GeekCrawlerRunDto?> GetLatestRunAsync(
            string ownerUserId,
            string crawlType,
            string seedsJson,
            CancellationToken ct = default) =>
            Task.FromResult(LatestRun);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesAsync(
            Guid runId,
            int limit = 100,
            int offset = 0,
            CancellationToken ct = default)
        {
            ListPagesAsyncCallCount++;
            return Task.FromResult(Pages);
        }

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId,
            IReadOnlyList<string> seedUrls,
            CancellationToken ct = default)
        {
            ListPagesBySeedsAsyncCallCount++;
            return Task.FromResult(Pages);
        }

        public Task<GeekCrawlerRunDto?> GetRunForSlotAsync(
            string ownerUserId,
            string crawlType,
            string seedKey,
            CancellationToken ct = default) =>
            Task.FromResult<GeekCrawlerRunDto?>(null);
    }

    private sealed class FakeProjectSitePageReader : IGccV2ProjectSitePageReader
    {
        public IReadOnlyList<GccV2ProjectSiteCrawlPageDto> Pages { get; init; } = [];

        public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
            Guid runId,
            int limit,
            int offset,
            CancellationToken ct = default) =>
            Task.FromResult(Pages);

        public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
            Guid runId,
            IReadOnlyList<string> seedUrls,
            CancellationToken ct = default) =>
            Task.FromResult(Pages);
    }
}
