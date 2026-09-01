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

        var resolver = new GccV2GeekCrawlerResearchResolver(repo, NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);
        var pages = await resolver.ResolveQuoteablePagesAsync(
            "user-1",
            "partner",
            ["https://partner.example/tools"],
            CancellationToken.None);

        Assert.Single(pages);
        Assert.Contains("Partner Tool", pages[0].Title);
    }

    [Fact]
    public async Task MergePartnerResearch_skips_on_site_project_tool_urls()
    {
        var resolver = new GccV2GeekCrawlerResearchResolver(
            new FakeReadRepo(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

        var brief = """
            {
              "hierarchyPlan": {
                "recommendedTools": [
                  { "name": "Fin.ai", "href": "https://geekatyourspot.com/tools/fin" },
                  { "name": "Intercom", "href": "https://geekatyourspot.com/tools/intercom" }
                ]
              }
            }
            """;

        var merged = await resolver.MergePartnerResearchAsync(
            "user-1",
            brief,
            "https://geekatyourspot.com",
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
    public async Task ResolveQuoteablePages_missingRun_failClosed()
    {
        var resolver = new GccV2GeekCrawlerResearchResolver(
            new FakeReadRepo(),
            NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

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
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null),
        };
        var resolver = new GccV2GeekCrawlerResearchResolver(repo, NullLogger<GccV2GeekCrawlerResearchResolver>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveQuoteablePagesAsync(
                "user-1",
                "partner",
                ["https://partner.example/tools"],
                CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

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
    }
}
