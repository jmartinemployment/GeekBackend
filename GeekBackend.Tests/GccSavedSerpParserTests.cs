using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using RelatedPageDto = GeekAPI.Services.ContentCreatorV2.RelatedPageDto;

namespace GeekBackend.Tests;

public class GccSavedSerpParserTests
{
    [Fact]
    public void Parse_plainText_page2_style_organics_and_related_without_paa()
    {
        // Worked example A: page-2 capture — organics + related, no PAA.
        const string text = """
            AI Content Creation Workflow: A Complete Guide
            https://example.com/ai-content-workflow

            How to Build an AI Content Pipeline
            https://example.com/ai-pipeline

            Best AI Writing Tools for Marketing Teams
            https://vendors.example.com/best-ai-writers

            Related searches
            ai content calendar
            ai content creation tools
            generative ai workflow
            """;

        var result = GccSavedSerpParser.Parse(text, "AI Content Creation Workflow");

        Assert.True(result.Organics.Count >= 2);
        Assert.Equal("AI Content Creation Workflow: A Complete Guide", result.Organics[0].Title);
        Assert.Equal("https://example.com/ai-content-workflow", result.Organics[0].Url);
        Assert.Contains(result.RelatedSearches, r => r.Contains("ai content", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.PeopleAlsoAsk);
        Assert.True(result.MissingPaaLikelyPage2);
        Assert.Contains(result.Shape.DominantFormats, f => f is "guide" or "listicle" or "mixed/informational");
    }

    [Fact]
    public void Parse_html_extracts_paa_and_organics()
    {
        // Page-1 style: organics via /url?q= + PAA-like nodes.
        const string html = """
            <html><body>
              <a href="/url?q=https%3A%2F%2Fexample.com%2Fai-ads&amp;sa=U">How does AI optimize ad budgets in marketing</a>
              <a href="/url?q=https%3A%2F%2Fcompetitor.com%2Fguide&amp;sa=U">Ultimate Guide to AI Marketing Automation</a>
              <div class="related-question-pair" data-q="q1">
                <span>How does AI optimize ad budgets in marketing?</span>
              </div>
              <div class="related-question-pair">
                <span>Is it legal to use AI for advertising?</span>
              </div>
              <div class="related-question-pair">
                <span>How can I make $1000/day with AI?</span>
              </div>
              <h3>How does Coca-Cola use AI in marketing?</h3>
              Related searches
              ai advertising tools
            </body></html>
            """;

        var result = GccSavedSerpParser.Parse(html, "AI marketing");

        Assert.NotEmpty(result.Organics);
        Assert.Contains(result.Organics, o => o.Url.Contains("example.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Organics, o => o.Url.Contains("google.", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.PeopleAlsoAsk);
        Assert.Contains(result.PeopleAlsoAsk, q => q.Question.Contains("ad budgets", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_paa_relevance_prechecks_noise_unchecked()
    {
        const string text = """
            How does AI optimize ad budgets in marketing?
            Is it legal to use AI for advertising?
            How does Coca-Cola use AI in marketing?
            How can I make $1000/day with AI?
            Who is the richest YouTuber?
            Do YouTubers pay taxes?
            What jobs will be gone by 2030?
            """;

        var result = GccSavedSerpParser.Parse(text, "AI marketing advertising");

        Assert.Contains(result.PeopleAlsoAsk, q =>
            q.Question.Contains("ad budgets", StringComparison.OrdinalIgnoreCase) && q.LikelyRelevant);
        Assert.Contains(result.PeopleAlsoAsk, q =>
            q.Question.Contains("make $1000", StringComparison.OrdinalIgnoreCase) && !q.LikelyRelevant);
        Assert.Contains(result.PeopleAlsoAsk, q =>
            q.Question.Contains("richest", StringComparison.OrdinalIgnoreCase) && !q.LikelyRelevant);
        Assert.Contains(result.PeopleAlsoAsk, q =>
            q.Question.Contains("pay taxes", StringComparison.OrdinalIgnoreCase) && !q.LikelyRelevant);
    }

    [Fact]
    public void Parse_html_uses_h3_title_when_anchor_text_exceeds_limit()
    {
        // Regression: modern Google wraps the title in <h3> inside the result anchor, whose
        // full InnerText (title + source + breadcrumb cite + "About this result") exceeds 200
        // chars. The old whole-anchor-text heuristic skipped all of them -> 0 organics -> error.
        const string html = """
            <!doctype html><html><body>
            <div class="MjjYud">
              <a jsname="UWckNb" class="zReHs" href="https://business.adobe.com/blog/how-enterprises-scale-content-creation-workflows">
                <h3 class="LC20lb MBeuO DKV0Md">How enterprises scale content creation workflows</h3>
                <div class="notranslate"><span class="VuuXrf">Adobe for Business</span>
                  <cite class="qLRx3b">https://business.adobe.com &rsaquo; blog &rsaquo; how-enterprises-scale-content-creation-workflows-and-more-breadcrumb-text...</cite>
                </div>
                <div>About this result — Adobe for Business — cached — similar — more chrome text to push length past two hundred characters easily now</div>
              </a>
            </div>
            </body></html>
            """;

        var result = GccSavedSerpParser.Parse(html, "content creation workflow");

        Assert.Single(result.Organics);
        Assert.Equal("How enterprises scale content creation workflows", result.Organics[0].Title);
        Assert.Equal(
            "https://business.adobe.com/blog/how-enterprises-scale-content-creation-workflows",
            result.Organics[0].Url);
        Assert.DoesNotContain("About this result", result.Organics[0].Title);
        Assert.False(
            (result.ParseWarning ?? "").Contains("Could not", StringComparison.OrdinalIgnoreCase),
            $"Unexpected hard warning: {result.ParseWarning}");
    }

    [Fact]
    public void BuildPartialInformationGain_lists_this_site_coverage()
    {
        var pages = new List<RelatedPageDto>
        {
            new("https://site.example/ai", "AI Services", [new HeadingDto(1, "Overview"), new HeadingDto(2, "Pricing")], "We offer AI consulting."),
        };

        var note = GccSavedSerpParser.BuildPartialInformationGain("AI consulting", pages);

        Assert.NotEmpty(note.ThisSiteCovers);
        Assert.Contains("AI consulting", note.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(note.CompetitorOpens);
    }

    [Fact]
    public void BuildPartialInformationGain_adds_competitor_opens_from_serp()
    {
        var pages = new List<RelatedPageDto>
        {
            new("https://oursite.com/blog", "Our Blog", [new HeadingDto(2, "AI")], "excerpt"),
        };
        var organics = new List<SavedSerpOrganic>
        {
            new("Competitor Guide", "https://other.com/guide", 1),
            new("Our own page", "https://oursite.com/other", 2),
        };

        var note = GccSavedSerpParser.BuildPartialInformationGain("AI", pages, organics);

        Assert.Contains(note.CompetitorOpens, c => c.Contains("other.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(note.CompetitorOpens, c => c.Contains("oursite.com", StringComparison.OrdinalIgnoreCase));
    }
}
