using System.Text.Json;
using GeekAPI.Services.GeekCrawler;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.GeekCrawler;

public sealed class HttpGeekCrawlerRagClientTests
{
    [Fact]
    public void MapChunksToQuoteable_groupsByUrl_andCapsParagraphs()
    {
        var chunks = new List<HttpGeekCrawlerRagClient.ChunkDto>
        {
            new()
            {
                Url = "https://partner.example/tools",
                FinalUrl = "https://partner.example/tools",
                Title = "Partner Tool",
                ChunkIndex = 1,
                Text = "Second chunk about pricing and plans.",
            },
            new()
            {
                Url = "https://partner.example/tools",
                FinalUrl = "https://partner.example/tools",
                Title = "Partner Tool",
                ChunkIndex = 0,
                Text = "First chunk describing the partner tool features.",
            },
            new()
            {
                Url = "https://other.example/",
                FinalUrl = "https://other.example/",
                Title = "Other",
                ChunkIndex = 0,
                Text = "Unrelated page.",
            },
        };

        var pages = HttpGeekCrawlerRagClient.MapChunksToQuoteable(chunks);

        Assert.Equal(2, pages.Count);
        var tools = Assert.Single(pages, p => p.Url.Contains("partner.example", StringComparison.Ordinal));
        Assert.Equal("Partner Tool", tools.Title);
        Assert.Equal(2, tools.Paragraphs.Count);
        Assert.StartsWith("First chunk", tools.Paragraphs[0]);
        Assert.StartsWith("Second chunk", tools.Paragraphs[1]);
    }

    [Fact]
    public void MapChunksToQuoteable_empty_returnsEmpty()
    {
        Assert.Empty(HttpGeekCrawlerRagClient.MapChunksToQuoteable(null));
        Assert.Empty(HttpGeekCrawlerRagClient.MapChunksToQuoteable([]));
    }

    /// <summary>
    /// The wire shape, pinned. Geek-Crawler-Rag sends one <c>{label, href}</c> object per anchor
    /// (<c>ChunkAnchor</c>, models.py). Typed <c>List&lt;string&gt;</c>, this payload threw inside
    /// ReadFromJsonAsync, QueryAsync's catch turned the throw into a Failed result, and a page with
    /// links was indistinguishable from a page RAG could not reach.
    /// </summary>
    [Fact]
    public void ChunkDto_deserializes_objectShapedAnchors()
    {
        const string json = """
        {
          "url": "https://partner.example/tools",
          "finalUrl": "https://partner.example/tools",
          "title": "Partner Tool",
          "chunkIndex": 0,
          "text": "The tool directory lists every integration.",
          "sectionTitle": "Integrations",
          "anchors": [
            { "label": "Pricing", "href": "/pricing" },
            { "label": "Docs", "href": "https://partner.example/docs" }
          ]
        }
        """;

        var chunk = JsonSerializer.Deserialize<HttpGeekCrawlerRagClient.ChunkDto>(
            json, HttpGeekCrawlerRagClient.JsonOpts);

        Assert.NotNull(chunk);
        Assert.NotNull(chunk!.Anchors);
        Assert.Equal(2, chunk.Anchors!.Count);
        Assert.Equal("Pricing", chunk.Anchors[0].Label);
        Assert.Equal("/pricing", chunk.Anchors[0].Href);
        Assert.Equal("Docs", chunk.Anchors[1].Label);
        Assert.Equal("https://partner.example/docs", chunk.Anchors[1].Href);
    }

    /// <summary>
    /// The label is what reaches the writer -- deduplicated, and capped at
    /// <see cref="GccPartnerResearchCaps.MaxAnchorsPerChunk"/> so a nav-heavy page cannot spend the
    /// prompt on its own menu.
    /// </summary>
    [Fact]
    public void MapChunksToQuoteable_rendersAnchorLabels_dedupedAndCapped()
    {
        var anchors = new List<HttpGeekCrawlerRagClient.GeekCrawlerRagAnchorDto>
        {
            new() { Label = "Pricing", Href = "/pricing" },
            new() { Label = " pricing ", Href = "/pricing?ref=nav" },
            new() { Label = null, Href = "/no-label" },
            new() { Label = "   ", Href = "/blank" },
        };
        for (var i = 0; i < GccPartnerResearchCaps.MaxAnchorsPerChunk + 3; i++)
            anchors.Add(new() { Label = $"Tool {i}", Href = $"/tool/{i}" });

        var pages = HttpGeekCrawlerRagClient.MapChunksToQuoteable(
        [
            new()
            {
                Url = "https://partner.example/tools",
                FinalUrl = "https://partner.example/tools",
                Title = "Partner Tool",
                ChunkIndex = 0,
                Text = "The tool directory lists every integration.",
                SectionTitle = "Integrations",
                Anchors = anchors,
            },
        ]);

        var paragraph = Assert.Single(Assert.Single(pages).Paragraphs);
        var line = Assert.Single(
            paragraph.Split('\n'),
            l => l.StartsWith("Linked from this section:", StringComparison.Ordinal));

        var labels = line["Linked from this section:".Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(GccPartnerResearchCaps.MaxAnchorsPerChunk, labels.Length);
        Assert.Equal("Pricing", labels[0]);
        Assert.DoesNotContain("pricing", labels[1..]);
        Assert.DoesNotContain(labels, l => string.IsNullOrWhiteSpace(l));
        Assert.DoesNotContain("/pricing", paragraph, StringComparison.Ordinal);
    }

    /// <summary>
    /// The spelling is the authority's, not this file's. A host-derived name would render
    /// "Zoneandco" into the same prompt whose required-mentions block asks for "Zone &amp; Co".
    /// </summary>
    [Fact]
    public void MapChunksToQuoteable_labelsChunkWithPartnerFromItsAnchors()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zoneandco.com"] = "Zone & Co",
        };

        var pages = HttpGeekCrawlerRagClient.MapChunksToQuoteable(
        [
            new()
            {
                Url = "https://partner.example/tools",
                FinalUrl = "https://partner.example/tools",
                Title = "Partner Tool",
                ChunkIndex = 0,
                Text = "The directory lists every integration.",
                Anchors =
                [
                    new() { Label = "Docs", Href = "/docs" },
                    new() { Label = "Zone", Href = "https://WWW.ZoneAndCo.com/pricing" },
                ],
            },
        ],
            lookup);

        var paragraph = Assert.Single(Assert.Single(pages).Paragraphs);
        Assert.Contains("Target Entity Match: Zone & Co", paragraph, StringComparison.Ordinal);
    }

    /// <summary>A subdomain is the same partner; the lookup is keyed on the registrable host.</summary>
    [Fact]
    public void DetectEntityFromAnchors_matchesSubdomains_andIgnoresUnknownHosts()
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dext.com"] = "Dext",
        };

        Assert.Equal("Dext", HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "App", Href = "https://app.dext.com/signin" }], lookup));
        Assert.Equal("Dext", HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "Home", Href = "//dext.com" }], lookup));
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "Other", Href = "https://notdext.com/x" }], lookup));
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "Mail", Href = "mailto:hi@dext.com" }], lookup));
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "Rel", Href = "/pricing" }], lookup));
    }

    /// <summary>No lookup means no label -- never a guessed one.</summary>
    [Fact]
    public void DetectEntityFromAnchors_withoutLookup_returnsNull()
    {
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "App", Href = "https://dext.com" }], null));
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(
            [new() { Label = "App", Href = "https://dext.com" }],
            new Dictionary<string, string>()));
        Assert.Null(HttpGeekCrawlerRagClient.DetectEntityFromAnchors(null,
            new Dictionary<string, string> { ["dext.com"] = "Dext" }));
    }
}
