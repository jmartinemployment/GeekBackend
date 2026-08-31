using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.GeekCrawler.Polite;
using GeekApplication.Models.GeekCrawler;
using Microsoft.Extensions.Configuration;

namespace GeekBackend.Tests.GeekCrawler;

public class GeekCrawlerSeedNormalizerTests
{
    [Fact]
    public void NormalizeSeeds_adds_https_for_bare_domain()
    {
        var seeds = GeekCrawlerSeedNormalizer.NormalizeSeeds(["example.com", "https://b.com/"]);
        Assert.Equal(2, seeds.Count);
        Assert.Contains(seeds, s => s.Equals("https://example.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(seeds, s => s.Contains("b.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SeedUrlsMatch_requires_same_url_set()
    {
        const string stored = """["https://a.com","https://b.com"]""";
        Assert.True(GeekCrawlerSeedNormalizer.SeedUrlsMatch(
            stored,
            ["https://b.com", "https://a.com"]));
        Assert.False(GeekCrawlerSeedNormalizer.SeedUrlsMatch(
            stored,
            ["https://a.com"]));
    }

    [Fact]
    public void GroupSeedsByOrigin_groups_same_host()
    {
        var grouped = GeekCrawlerSeedNormalizer.GroupSeedsByOrigin(
        [
            "https://pipedrive.com/",
            "https://pipedrive.com/pricing",
        ]);

        Assert.Single(grouped);
        Assert.Equal(2, grouped.First().Value.Count);
    }
}

public class GeekCrawlerStartRulesTests
{
    [Fact]
    public void CrawlTypes_rejects_unknown_type()
    {
        Assert.False(CrawlTypes.IsValid("vendor"));
        Assert.True(CrawlTypes.IsValid(CrawlTypes.Partner));
    }

    [Fact]
    public void ShouldWakeAtStartup_skips_recent_pending()
    {
        var recent = new GeekAPI.HttpClients.GeekCrawlerRunDto(
            Guid.NewGuid(),
            "user-1",
            CrawlTypes.Competitors,
            "pending",
            "[]",
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null);

        Assert.False(GeekCrawlerRecovery.ShouldWakeAtStartup(recent, DateTimeOffset.UtcNow));
    }
}

public class GeekCrawlerLinkExtractorTests
{
    [Fact]
    public void ExtractAllLinks_includes_external_and_same_origin()
    {
        const string html = """
            <html><body>
              <a href="/pricing">Pricing</a>
              <a href="https://wikipedia.org/wiki/Test">Wiki</a>
              <a href="mailto:a@b.com">Mail</a>
            </body></html>
            """;

        var links = GeekCrawlerLinkExtractor.ExtractAllLinks(
            html,
            "https://example.com/page",
            "https://example.com");

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.IsSameOrigin && l.LinkUrl.Contains("example.com/pricing"));
        Assert.Contains(links, l => !l.IsSameOrigin && l.LinkUrl.Contains("wikipedia.org"));
    }
}

public class GeekCrawlerOptionsTests
{
    [Fact]
    public void FromConfiguration_applies_accelerated_presets()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_MODE"] = "accelerated",
                ["GEEK_CRAWLER_WORKER_COUNT"] = "2",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal("accelerated", options.Mode);
        Assert.Equal(2, options.WorkerCount);
        Assert.Equal(4, options.ParallelismPerOrigin);
        Assert.Equal(3, options.HostDelaySeconds);
    }

    [Fact]
    public void FromConfiguration_worker_count_has_no_upper_cap()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_WORKER_COUNT"] = "10",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal(10, options.WorkerCount);
    }

    [Fact]
    public void FromConfiguration_parses_seeds_only()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_SEEDS_ONLY"] = "true",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.True(options.SeedsOnly);
    }

    [Fact]
    public void FromConfiguration_respects_explicit_overrides()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEEK_CRAWLER_MODE"] = "standard",
                ["GEEK_CRAWLER_PARALLELISM_PER_ORIGIN"] = "3",
                ["GEEK_CRAWLER_HOST_DELAY_SECONDS"] = "5",
            })
            .Build();

        var options = GeekCrawlerOptions.FromConfiguration(config);

        Assert.Equal(3, options.ParallelismPerOrigin);
        Assert.Equal(5, options.HostDelaySeconds);
    }
}

public class GeekCrawlerHostTrafficControllerTests
{
    [Fact]
    public async Task ExecutePolitelyAsync_allows_parallelism_up_to_max()
    {
        var controller = new GeekCrawlerHostTrafficController(maxParallel: 2);
        var clock = TimeProvider.System;
        var inFlight = 0;
        var maxSeen = 0;

        async Task Work()
        {
            await controller.ExecutePolitelyAsync(async () =>
            {
                var current = Interlocked.Increment(ref inFlight);
                try
                {
                    var peak = current;
                    var prior = maxSeen;
                    while (peak > prior)
                    {
                        prior = Interlocked.CompareExchange(ref maxSeen, peak, prior);
                    }

                    await Task.Delay(25);
                    return 0;
                }
                finally
                {
                    Interlocked.Decrement(ref inFlight);
                }
            }, CancellationToken.None);
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Work()));

        Assert.Equal(2, maxSeen);
        _ = clock;
    }
}
