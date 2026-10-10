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
    private static IEnumerable<GccV2HeadingNode> Flatten(IEnumerable<GccV2HeadingNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children))
                yield return c;
        }
    }
}
