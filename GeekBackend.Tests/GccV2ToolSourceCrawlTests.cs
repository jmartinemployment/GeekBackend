using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.ContentCreatorV2.ToolSources;

namespace GeekBackend.Tests;

public class GccV2ToolSourceCrawlTests
{
    [Fact]
    public void CollectOperatorSeedUrls_reads_operatorTools_only()
    {
        const string brief = """
            {
              "operatorTools": [
                { "name": "Pipedrive", "url": "https://www.pipedrive.com/" },
                "https://manychat.com/"
              ],
              "hierarchyPlan": {
                "recommendedTools": [{ "name": "Other", "href": "https://other.example/" }]
              }
            }
            """;

        var seeds = GccV2PartnerUrlResearchService.CollectOperatorSeedUrls(brief);
        Assert.Equal(2, seeds.Count);
        Assert.Contains(seeds, s => s.Contains("pipedrive.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(seeds, s => s.Contains("manychat.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupOperatorSeedsByOrigin_deduplicates_hosts()
    {
        var grouped = GccV2PartnerUrlResearchService.GroupOperatorSeedsByOrigin(
        [
            "https://pipedrive.com/",
            "https://pipedrive.com/pricing",
        ]);

        Assert.Single(grouped);
        Assert.Equal(2, grouped.First().Value.Count);
    }

    [Fact]
    public void SeedUrlsMatch_requires_same_url_set()
    {
        const string stored = """["https://a.com/","https://b.com/"]""";
        Assert.True(GccV2ToolSourceCrawlService.SeedUrlsMatch(
            stored,
            ["https://b.com/", "https://a.com/"]));
        Assert.False(GccV2ToolSourceCrawlService.SeedUrlsMatch(
            stored,
            ["https://a.com/"]));
    }

    [Fact]
    public void Gate_defers_when_crawl_running()
    {
        var run = new GccV2ToolSourceCrawlRunDto(
            Guid.NewGuid(),
            "user-1",
            "running",
            "[]",
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null);

        Assert.Throws<GccV2ToolWriteDeferredException>(() =>
            GccV2ToolSourceCrawlGate.ThrowIfDeferred("{\"operatorTools\":[{\"url\":\"https://a.com\"}]}", run));
    }
}
