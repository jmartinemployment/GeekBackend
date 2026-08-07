using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests;

public class GccResearchUploadTests
{
    private static GccCreateDto CreateWithResearch(string briefJson, string? researchJson) => new(
        Id: Guid.NewGuid(),
        ClientId: Guid.NewGuid(),
        OwnerUserId: Guid.NewGuid(),
        StartingContentType: "blog",
        Topic: "ai content workflow",
        Notes: null,
        SiteAnalysisId: null,
        SiteSectionJson: null,
        BriefJson: briefJson,
        ResearchJson: researchJson,
        Status: "draft",
        CreatedAtUtc: DateTime.UtcNow,
        UpdatedAtUtc: DateTime.UtcNow);


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
        Assert.Contains(page.Headings, h => h.Text.Contains("optimizes ad budgets", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Paragraphs, p => p.Contains("reallocate spend", StringComparison.OrdinalIgnoreCase));
        Assert.False(GccArticleHtmlExtractor.IsEmpty(page));
    }

    [Fact]
    public void ArticleExtractor_captures_h1_through_h6_with_real_levels()
    {
        const string html = """
            <html><head><title>Deep Outline</title></head>
            <body>
              <h1>Top level</h1>
              <h2>Second level</h2>
              <h3>Third level</h3>
              <h4>Fourth level</h4>
              <h5>Fifth level</h5>
              <h6>Sixth level</h6>
              <p>Enough paragraph text to pass the minimum length filter here.</p>
            </body></html>
            """;

        var page = GccArticleHtmlExtractor.Extract("upload://abc/deep.html", html);

        Assert.Equal(6, page.Headings.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6], page.Headings.Select(h => h.Level));
        Assert.Contains(page.Headings, h => h.Level == 4 && h.Text == "Fourth level");
        Assert.Contains(page.Headings, h => h.Level == 6 && h.Text == "Sixth level");
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
            .Select(i => new GccQuoteablePage($"upload://s{i}/f{i}.html", $"Doc {i}", [new HeadingDto(2, $"H{i}")], [$"P{i}"]))
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

    [Fact]
    public void SerpPages_round_trip_unlimited_and_persist_organics_and_related()
    {
        var pages = Enumerable.Range(0, 5).Select(i =>
        {
            var parsed = GccSavedSerpParser.Parse(
                $"<a href=\"https://ex{i}.com/p\"><h3>Title {i}</h3></a>\nRelated searches\nrel {i}",
                "ai content workflow");
            return new GccParsedSerpPage($"s{i}", $"f{i}.html", parsed.Organics, parsed.RelatedSearches, parsed.Shape, parsed.ParseWarning);
        }).ToList();
        var doc = new GccResearchDocument(null, [], SerpPages: pages);

        var json = GccResearchFetchService.Serialize(doc);
        var back = GccResearchFetchService.Deserialize(json);

        Assert.NotNull(back);
        Assert.NotNull(back!.SerpPages);
        Assert.Equal(5, back.SerpPages!.Count); // unlimited — all 5 persist, not capped at 3
        Assert.Equal("f3.html", back.SerpPages[3].FileName);
        Assert.Contains(back.SerpPages[0].Organics, o => o.Url == "https://ex0.com/p");
    }

    [Fact]
    public void Zero_organic_serp_page_still_persists_with_warning_no_hard_failure()
    {
        // Content the parser can't extract organics from — must not be discarded; still stored
        // with whatever was found (here: nothing) plus a ParseWarning, per the graceful-degradation
        // design (no reintroduced hard 400 for a partial/failed SERP parse).
        var parsed = GccSavedSerpParser.Parse("not html and not paa-like content at all", "ai content workflow");
        var page = new GccParsedSerpPage("s1", "empty.html", parsed.Organics, parsed.RelatedSearches, parsed.Shape, parsed.ParseWarning);

        Assert.Empty(page.Organics);
        Assert.NotNull(page.ParseWarning);

        var doc = new GccResearchDocument(null, [], SerpPages: [page]);
        var back = GccResearchFetchService.Deserialize(GccResearchFetchService.Serialize(doc));
        Assert.Single(back!.SerpPages!);
        Assert.Equal("empty.html", back.SerpPages![0].FileName);
    }

    [Fact]
    public void BuildBriefAndResearchBlock_emits_one_labeled_block_per_uploaded_serp_file_no_paa()
    {
        var page1 = new GccParsedSerpPage(
            "s1", "keyword-a.html",
            [new SavedSerpOrganic("Title A", "https://a.example/page", 1)],
            ["related a"],
            new SerpShapeSummary(["guide"], [], "guidance a", false, 1, "maybe-page2"),
            null);
        var page2 = new GccParsedSerpPage(
            "s2", "keyword-b.html",
            [new SavedSerpOrganic("Title B", "https://b.example/page", 1)],
            ["related b"],
            new SerpShapeSummary(["listicle"], [], "guidance b", false, 1, "maybe-page2"),
            null);
        var research = new GccResearchDocument(null, [], SerpPages: [page1, page2]);
        var create = CreateWithResearch(
            """{ "primaryIntent": "informational" }""",
            GccResearchFetchService.Serialize(research));

        var block = GccGenerateService.BuildBriefAndResearchBlock(create);

        Assert.Contains("=== KEYWORD SERP: keyword-a.html ===", block);
        Assert.Contains("=== KEYWORD SERP: keyword-b.html ===", block);
        Assert.Contains("Title A (https://a.example/page)", block);
        Assert.Contains("Title B (https://b.example/page)", block);
        Assert.Contains("related a", block);
        Assert.Contains("related b", block);
        // Neither Shape.Guidance nor PAA is injected into the Generate prompt.
        Assert.DoesNotContain("guidance a", block);
        Assert.DoesNotContain("guidance b", block);
        Assert.DoesNotContain("People Also Ask", block);
    }
}
