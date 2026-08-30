using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.GeekCrawler;

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
