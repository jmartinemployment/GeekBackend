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
}
