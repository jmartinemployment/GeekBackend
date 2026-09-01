using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.GeekCrawler;
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

        Assert.NotNull(merged);
        Assert.Contains("partnerResearch", merged!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MergePartnerResearch_soft_fails_missing_external_run()
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

        Assert.Equal(brief, merged);
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
    public async Task ResolveQuoteablePages_missingRun_failClosed()
    {
        var resolver = CreateResolver(new FakeReadRepo());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveQuoteablePagesAsync(
                "user-1",
                "partner",
                ["https://partner.example/tools"],
                CancellationToken.None));

        Assert.Contains("No Geek-Crawler partner run", ex.Message);
    }

    [Fact]
    public async Task ResolveQuoteablePages_incompleteRun_failClosed()
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveQuoteablePagesAsync(
                "user-1",
                "partner",
                ["https://partner.example/tools"],
                CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeIncompleteRun_maps_out_of_memory_to_actionable_message()
    {
        var run = new GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            "partner",
            "failed",
            "[\"https://www.pipedrive.com\"]",
            null,
            null,
            "System.OutOfMemoryException was thrown.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var message = GccV2GeekCrawlerResearchResolver.DescribeIncompleteRun(run, "partner");

        Assert.Contains("out of memory", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("page limit", message, StringComparison.OrdinalIgnoreCase);
    }

    private static GccV2GeekCrawlerResearchResolver CreateResolver(
        FakeReadRepo crawlerRepo,
        FakeProjectSitePageReader? projectSitePages = null) =>
        new(
            crawlerRepo,
            projectSitePages ?? new FakeProjectSitePageReader(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

    private sealed class FakeReadRepo : IGccV2GeekCrawlerReadRepository
    {
        public GeekCrawlerRunDto? LatestRun { get; init; }
        public IReadOnlyList<GeekCrawlerPageDto> Pages { get; init; } = [];

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
            CancellationToken ct = default) =>
            Task.FromResult(Pages);

        public Task<IReadOnlyList<GeekCrawlerPageDto>> ListPagesBySeedsAsync(
            Guid runId,
            IReadOnlyList<string> seedUrls,
            CancellationToken ct = default) =>
            Task.FromResult(Pages);

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
