using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccResearchUploadTests
{
    [Fact]
    public void ArticleExtractor_pulls_title_headings_paragraphs_from_html()
    {
        const string html = """
            <html><head><title>AI Marketing Guide</title></head>
            <body>
              <h1>AI Marketing Guide</h1>
              <h2>How AI optimizes ad budgets</h2>
              <p>AI systems reallocate spend toward the best-performing segments continuously over time.</p>
              <h3>Measuring impact</h3>
              <p>Track conversion value and cost per acquisition to judge the model's real contribution here.</p>
            </body></html>
            """;

        var page = GccArticleHtmlExtractor.Extract("upload://abc/guide.html", html);

        Assert.Equal("AI Marketing Guide", page.Title);
        Assert.Equal("upload://abc/guide.html", page.Url);
        Assert.Contains(page.Headings, h => h.Contains("optimizes ad budgets", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Paragraphs, p => p.Contains("reallocate spend", StringComparison.OrdinalIgnoreCase));
        Assert.False(GccArticleHtmlExtractor.IsEmpty(page));
    }

    [Fact]
    public void ArticleExtractor_serp_like_html_without_article_markup_is_empty()
    {
        // Google SERP saves are mostly div chrome — no usable h1–h3 / <p> for quoteables.
        const string html = """
            <html><head><title>keyword - Google Search</title></head>
            <body>
              <div class="g"><a href="https://example.com"><span>Some Result Title</span></a></div>
              <div role="heading">People also ask</div>
              <span>A short snippet with no paragraph tag</span>
            </body></html>
            """;

        var page = GccArticleHtmlExtractor.Extract("upload://abc/serp.html", html);

        Assert.True(GccArticleHtmlExtractor.IsEmpty(page));
    }

    [Fact]
    public void ResearchDocument_with_sources_round_trips_and_is_unlimited()
    {
        // More than the old 3-quoteable cap — all must persist.
        var quoteables = Enumerable.Range(0, 5)
            .Select(i => new GccQuoteablePage($"upload://s{i}/f{i}.html", $"Doc {i}", [$"H{i}"], [$"P{i}"]))
            .ToList();
        var sources = Enumerable.Range(0, 5)
            .Select(i => new GccKeywordSource($"s{i}", $"f{i}.html", "KeywordResult", 1, 1, 0))
            .ToList();
        var doc = new GccResearchDocument(null, quoteables, sources);

        var json = GccResearchFetchService.Serialize(doc);
        var back = GccResearchFetchService.Deserialize(json);

        Assert.NotNull(back);
        Assert.Equal(5, back!.Quoteables.Count);
        Assert.NotNull(back.Sources);
        Assert.Equal(5, back.Sources!.Count);
        Assert.Equal("f3.html", back.Sources[3].FileName);
    }

    [Fact]
    public void Legacy_research_json_without_sources_still_deserializes()
    {
        const string legacy = """
            { "SerpIndex": null, "Quoteables": [ { "Url": "u", "Title": "t", "Headings": [], "Paragraphs": ["p"] } ] }
            """;

        var back = GccResearchFetchService.Deserialize(legacy);

        Assert.NotNull(back);
        Assert.Single(back!.Quoteables);
        Assert.Null(back.Sources); // optional field defaults to null
    }
}
