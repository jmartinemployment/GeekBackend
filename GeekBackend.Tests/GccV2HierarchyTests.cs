using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Hierarchy;
using Microsoft.Extensions.Logging;

namespace GeekBackend.Tests;

public class GccV2HierarchyTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Theory]
    [InlineData("geekatyourspot.com", "https://geekatyourspot.com/")]
    [InlineData("https://geekatyourspot.com/pricing", "https://geekatyourspot.com/")]
    [InlineData("http://example.com/a/b?x=1", "http://example.com/")]
    public void HomepageUrl_Normalizes_To_Origin(string input, string expected)
    {
        Assert.True(GccV2HomepageUrl.TryNormalize(input, out var homepage));
        Assert.Equal(expected, homepage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    public void HomepageUrl_Rejects_Invalid(string input)
    {
        Assert.False(GccV2HomepageUrl.TryNormalize(input, out _));
    }

    [Fact]
    public void HeadingTreeBuilder_Builds_Nested_Structure_With_Links()
    {
        const string html = """
            <html><body>
              <h2>Artificial Intelligence Use Cases</h2>
              <p>Intro with a <a href="/about">About</a> link.</p>
              <h3>Marketing</h3>
              <h4>Lead Capture Pipeline</h4>
              <h5>Smart Chatbots for Marketing:</h5>
              <ul>
                <li><a href="/tools/fin">Fin.ai</a></li>
                <li><a href="/tools/intercom">Intercom</a></li>
              </ul>
              <h6>Details</h6>
              <p>Nested copy.</p>
            </body></html>
            """;

        var roots = GccV2HeadingTreeBuilder.Build(html);
        Assert.Single(roots);
        Assert.Equal(2, roots[0].Level);
        Assert.Equal("Artificial Intelligence Use Cases", roots[0].HeadingText);
        Assert.Contains(roots[0].Paragraphs, p => p.Contains("Intro", StringComparison.Ordinal));
        Assert.Contains(roots[0].Links, l => l.Text == "About" && l.Href == "/about");

        var marketing = Assert.Single(roots[0].Children);
        Assert.Equal("Marketing", marketing.HeadingText);

        var pipeline = Assert.Single(marketing.Children);
        var chatbots = Assert.Single(pipeline.Children);
        Assert.Equal("Smart Chatbots for Marketing:", chatbots.HeadingText);
        Assert.Equal(2, chatbots.Links.Count);
        Assert.Contains(chatbots.Links, l => l.Text == "Fin.ai" && l.Href == "/tools/fin");
        Assert.Contains(chatbots.Links, l => l.Text == "Intercom" && l.Href == "/tools/intercom");

        var details = Assert.Single(chatbots.Children);
        Assert.Equal(6, details.Level);
        Assert.Contains(details.Paragraphs, p => p.Contains("Nested copy", StringComparison.Ordinal));
    }

    [Fact]
    public void MergeIntoBriefJson_RoundTrips_SiteHierarchy_As_Structured_Json()
    {
        var hierarchy = new GccV2SiteHierarchy(
            HomepageUrl: "https://geekatyourspot.com/",
            Viewport: "mobile",
            BuiltAtUtc: DateTimeOffset.Parse("2026-08-27T12:00:00Z"),
            Pages:
            [
                new GccV2PageHierarchy(
                    "https://geekatyourspot.com/",
                    [
                        new GccV2HeadingNode(
                            2,
                            "Use Cases",
                            ["Body"],
                            [new GccV2HeadingLink("Tool", "/tools/a")],
                            []),
                    ]),
            ]);

        var merged = GccV2SiteHierarchyService.MergeIntoBriefJson("""{"title":"x"}""", hierarchy);
        Assert.NotNull(merged);

        using var doc = JsonDocument.Parse(merged!);
        Assert.Equal("x", doc.RootElement.GetProperty("title").GetString());
        var sh = doc.RootElement.GetProperty("siteHierarchy");
        Assert.Equal("mobile", sh.GetProperty("viewport").GetString());
        Assert.Equal("https://geekatyourspot.com/", sh.GetProperty("homepageUrl").GetString());

        var link = sh.GetProperty("pages")[0].GetProperty("roots")[0].GetProperty("links")[0];
        Assert.Equal("Tool", link.GetProperty("text").GetString());
        Assert.Equal("/tools/a", link.GetProperty("href").GetString());

        var roundTrip = JsonSerializer.Deserialize<GccV2SiteHierarchy>(sh.GetRawText(), JsonOpts);
        Assert.NotNull(roundTrip);
        Assert.Equal("mobile", roundTrip!.Viewport);
        Assert.Equal("Tool", roundTrip.Pages[0].Roots[0].Links[0].Text);
    }

    [Fact]
    public void ExtractSameOriginLinks_Keeps_Same_Host_Only()
    {
        const string html = """
            <html><body>
              <a href="/pricing">Pricing</a>
              <a href="https://geekatyourspot.com/tools/fin">Fin</a>
              <a href="https://other.com/x">Other</a>
              <a href="mailto:a@b.com">Mail</a>
            </body></html>
            """;

        var links = GccV2PageFetcher.ExtractSameOriginLinks(html, "https://geekatyourspot.com/");
        Assert.Contains(links, l => l.Contains("/pricing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(links, l => l.Contains("/tools/fin", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(links, l => l.Contains("other.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeadingTreeBuilder_Skips_GeekHidden_Twin_Markup()
    {
        const string html = """
            <html><body>
              <div data-geek-hidden="1">
                <h2>Desktop twin Use Cases</h2>
                <a href="/tools/desktop-only">DesktopTool</a>
              </div>
              <div>
                <h2>Mobile Use Cases</h2>
                <p>Visible <a href="/tools/mobile">MobileTool</a></p>
              </div>
            </body></html>
            """;

        var roots = GccV2HeadingTreeBuilder.Build(html);
        Assert.Single(roots);
        Assert.Equal("Mobile Use Cases", roots[0].HeadingText);
        Assert.DoesNotContain(roots, r => r.HeadingText.Contains("Desktop", StringComparison.Ordinal));
    }

    [Fact]
    public void HeadingTreeBuilder_Skips_CssHidden_Twin_Markup()
    {
        const string html = """
            <html><body>
              <div data-gcc-hidden="1">
                <h2>Desktop twin Use Cases</h2>
                <a href="/tools/desktop-only">DesktopTool</a>
              </div>
              <div>
                <h2>Mobile Use Cases</h2>
                <p>Visible <a href="/tools/mobile">MobileTool</a></p>
              </div>
            </body></html>
            """;

        var roots = GccV2HeadingTreeBuilder.Build(html);
        Assert.Single(roots);
        Assert.Equal("Mobile Use Cases", roots[0].HeadingText);
        Assert.DoesNotContain(roots, r => r.HeadingText.Contains("Desktop", StringComparison.Ordinal));
        Assert.Contains(roots[0].Links, l => l.Href == "/tools/mobile");
        Assert.DoesNotContain(
            Flatten(roots),
            n => n.Links.Any(l => l.Href.Contains("desktop-only", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Live_Mobile_Homepage_Smoke_When_Enabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_HIERARCHY_LIVE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        await using var holder = new GccV2PlaywrightBrowserHolder();
        await holder.InitializeAsync();
        Assert.NotNull(holder.Browser);

        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
        var fetcher = new GccV2PageFetcher(
            holder,
            loggerFactory.CreateLogger<GccV2PageFetcher>());
        var service = new GccV2SiteHierarchyService(
            fetcher,
            loggerFactory.CreateLogger<GccV2SiteHierarchyService>());

        var hierarchy = await service.BuildHomepageAsync("https://geekatyourspot.com/", CancellationToken.None);
        Assert.NotNull(hierarchy);
        Assert.Equal("mobile", hierarchy!.Viewport);
        Assert.NotEmpty(hierarchy.Pages);
        Assert.NotEmpty(hierarchy.Pages[0].Roots);

        var flat = Flatten(hierarchy.Pages[0].Roots).ToList();
        Assert.Contains(
            flat,
            n => n.HeadingText.Contains("Use Cases", StringComparison.OrdinalIgnoreCase)
                 || n.HeadingText.Contains("Marketing", StringComparison.OrdinalIgnoreCase)
                 || n.Level >= 1);
        Assert.Contains(flat, n => n.Links.Count > 0);
    }

    private static IEnumerable<GccV2HeadingNode> Flatten(IEnumerable<GccV2HeadingNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children))
                yield return c;
        }
    }

    [Fact]
    public void SiteHierarchyFromCrawl_Excludes_404_And_Orders_Homepage_First()
    {
        const string errorHtml = """
            <html><body>
              <h1>404</h1>
              <h2>This page could not be found.</h2>
            </body></html>
            """;

        const string articleHtml = """
            <html><body>
              <h1>Automated Accounts Payable</h1>
              <h2>Introduction to Automated Accounts Payable</h2>
            </body></html>
            """;

        const string homeHtml = """
            <html><body>
              <h2>Artificial Intelligence Use Cases</h2>
              <h3>Marketing</h3>
            </body></html>
            """;

        var pages = new List<GccV2ProjectSiteCrawlPageDto>
        {
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com",
                "https://geekatyourspot.com/tools/accounting/jotform",
                "https://geekatyourspot.com/tools/accounting/jotform",
                404,
                true,
                errorHtml,
                DateTimeOffset.UtcNow),
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com",
                "https://geekatyourspot.com/blog/ap-automation",
                "https://geekatyourspot.com/blog/ap-automation",
                200,
                true,
                articleHtml,
                DateTimeOffset.UtcNow),
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com",
                "https://geekatyourspot.com/",
                "https://geekatyourspot.com/",
                200,
                true,
                homeHtml,
                DateTimeOffset.UtcNow),
        };

        var hierarchy = GccV2SiteHierarchyFromCrawl.Build(
            "https://geekatyourspot.com/tools/accounting/jotform",
            pages);

        Assert.NotNull(hierarchy);
        Assert.Equal("https://geekatyourspot.com/", hierarchy!.HomepageUrl);
        Assert.Single(hierarchy.Pages);
        Assert.True(GccV2SiteHierarchyFromCrawl.IsHomepage(hierarchy.Pages[0].PageUrl, hierarchy.HomepageUrl));
        Assert.DoesNotContain(
            hierarchy.Pages,
            p => p.Roots.Any(r => r.HeadingText.Contains("404", StringComparison.Ordinal)));
    }

    [Fact]
    public void SiteHierarchyFromCrawl_Keeps_UseCase_Page_With_Tool_Link_Group()
    {
        const string useCaseHtml = """
            <html><body>
              <h2>Smart Chatbots for Marketing</h2>
              <ul>
                <li><a href="/tools/fin">Fin.ai</a></li>
                <li><a href="/tools/intercom">Intercom</a></li>
              </ul>
            </body></html>
            """;

        const string homeHtml = """
            <html><body><h2>Home</h2></body></html>
            """;

        var pages = new List<GccV2ProjectSiteCrawlPageDto>
        {
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com",
                "https://geekatyourspot.com/",
                "https://geekatyourspot.com/",
                200,
                true,
                homeHtml,
                DateTimeOffset.UtcNow),
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://geekatyourspot.com",
                "https://geekatyourspot.com/ai-use-cases/marketing",
                "https://geekatyourspot.com/ai-use-cases/marketing",
                200,
                true,
                useCaseHtml,
                DateTimeOffset.UtcNow),
        };

        var hierarchy = GccV2SiteHierarchyFromCrawl.Build("https://geekatyourspot.com", pages);

        Assert.NotNull(hierarchy);
        Assert.Equal(2, hierarchy!.Pages.Count);
        Assert.Contains(
            hierarchy.Pages,
            p => p.PageUrl.Contains("ai-use-cases", StringComparison.OrdinalIgnoreCase));
        Assert.True(GccV2SiteHierarchyFromCrawl.HasRichLinkGroups(hierarchy.Pages[1].Roots));
    }

    [Fact]
    public void SiteHierarchyFromCrawl_Drops_Error_Heading_Trees_Even_When_Status_200()
    {
        const string soft404Html = """
            <html><body>
              <h1>404</h1>
              <h2>Page not found</h2>
            </body></html>
            """;

        var pages = new List<GccV2ProjectSiteCrawlPageDto>
        {
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "https://example.com",
                "https://example.com/missing",
                "https://example.com/missing",
                200,
                true,
                soft404Html,
                DateTimeOffset.UtcNow),
        };

        var hierarchy = GccV2SiteHierarchyFromCrawl.Build("https://example.com", pages);
        Assert.Null(hierarchy);
    }
}
