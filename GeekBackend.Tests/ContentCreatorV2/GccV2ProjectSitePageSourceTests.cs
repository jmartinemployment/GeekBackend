using System.Reflection;
using GeekAPI.Services.ContentCreatorV2.ProjectSite;
using GeekAPI.HttpClients;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// The Mongo source projects geek_crawler pages into the shape project-site consumers already read,
/// so GccV2SiteHierarchyFromCrawl and GccV2ProjectSiteGrounding need no change when the store moves.
/// </summary>
public sealed class GccV2ProjectSitePageSourceTests
{
    private static MethodInfo Projector() =>
        typeof(GccV2MongoProjectSitePageSource)
            .GetMethod("ToProjectSitePage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ToProjectSitePage not found");

    private static MethodInfo SeedReader() =>
        typeof(GccV2MongoProjectSitePageSource)
            .GetMethod("FirstSeedUrl", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("FirstSeedUrl not found");

    [Fact]
    public void Projection_carries_every_field_grounding_reads()
    {
        var runId = Guid.NewGuid();
        var pageId = Guid.NewGuid();
        var crawled = DateTimeOffset.UtcNow;

        var mongo = new GeekCrawlerPageDto(
            pageId, runId, "https://example.com", "https://example.com/tools",
            "https://example.com/tools/", 200, true, "<h2>Tools</h2><a href=\"/x\">X</a>",
            null, crawled, "Tools", "# Tools", "excerpt", null);

        var projected = (GccV2ProjectSiteCrawlPageDto)Projector().Invoke(null, [mongo])!;

        // GccV2SiteHierarchyFromCrawl.Build reads exactly Html, StatusCode and FinalUrl ?? Url.
        // GccV2ProjectSiteGrounding additionally stamps provenance from RunId, Id and CrawledAtUtc.
        Assert.Equal("<h2>Tools</h2><a href=\"/x\">X</a>", projected.Html);
        Assert.Equal(200, projected.StatusCode);
        Assert.Equal("https://example.com/tools/", projected.FinalUrl);
        Assert.Equal("https://example.com/tools", projected.Url);
        Assert.Equal(runId, projected.RunId);
        Assert.Equal(pageId, projected.Id);
        Assert.Equal(crawled, projected.CrawledAtUtc);
        Assert.True(projected.RobotsAllowed);
    }

    [Fact]
    public void Projection_preserves_null_html_rather_than_substituting()
    {
        // A page whose HTML was omitted at ingest (over the 14 MiB cap) must stay null, so the
        // grounding gate can report "no usable seed HTML" instead of deriving an empty tree.
        var mongo = new GeekCrawlerPageDto(
            Guid.NewGuid(), Guid.NewGuid(), "https://example.com", "https://example.com/big",
            "https://example.com/big", 200, true, null, null, DateTimeOffset.UtcNow,
            null, null, null, null);

        var projected = (GccV2ProjectSiteCrawlPageDto)Projector().Invoke(null, [mongo])!;
        Assert.Null(projected.Html);
    }

    [Theory]
    [InlineData("[\"https://geekatyourspot.com/\"]", "https://geekatyourspot.com/")]
    [InlineData("[\"\", \"https://geekatyourspot.com/\"]", "https://geekatyourspot.com/")]
    public void Site_url_comes_from_the_first_usable_seed(string seedsJson, string expected) =>
        Assert.Equal(expected, (string?)SeedReader().Invoke(null, [seedsJson]));

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"\"]")]
    [InlineData("[\"   \"]")]
    [InlineData("[1, 2]")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData(null)]
    public void No_readable_seed_yields_null_not_an_empty_string(string? seedsJson)
    {
        // An empty string here would be a success-shaped run: GetRunAsync would hand back a run whose
        // SeedUrl points at nothing, and the hierarchy build would derive an empty tree from it
        // instead of the caller learning the run is unusable.
        Assert.Null((string?)SeedReader().Invoke(null, [seedsJson]));
    }
}
