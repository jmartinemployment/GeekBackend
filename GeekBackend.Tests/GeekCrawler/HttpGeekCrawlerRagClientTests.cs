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
}
