using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The keyword is judged once, on the finished page, by the page's own score: the exact phrase as a
/// share of the page's words, inside the band the SEO report passes. Outside it the page ships and says
/// so, naming the sections that never use the phrase.
/// </summary>
/// <remarks>
/// Until 2026-10-10 the count was warned a call at a time, against the call's share of a count sized to
/// the page's floor: 18 of 21 calls on 2026-10-07, nine of them on pages the score passed. The score's
/// mark is a share of the words the page ends up with, and that is what is checked.
/// </remarks>
public sealed class GccKeywordOnThePageTests
{
    private const string Keyword = "Automated Approval Workflows";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    private static TextParagraph Text(string text) => new([new Run(text)]);

    private static Section H2(string heading, params Paragraph[] paragraphs) => new("h2", heading, paragraphs, null, []);

    private static GccGuardInputs Inputs(string? keyword = Keyword) => new(
        null,
        [],
        null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        string.Empty,
        Keyword: keyword);

    private static GccGuardFinding? KeywordFinding(GccGuardVerdict verdict) =>
        verdict.Findings.SingleOrDefault(f => f.Check == "keyword-density");

    private static bool ScorePasses(ContentDocument document, string type) =>
        GcwSeoAnalyzer.Analyze(JsonSerializer.Serialize(document, GccDocumentJson.Options), Keyword, type)
            .Checks.Single(c => c.Id == "keyword-density").Passed;

    private static ContentDocument Page(int uses, int fillerWords)
    {
        var phrase = string.Join(' ', Enumerable.Repeat("Automated approval workflows.", uses));
        return new ContentDocument(
            H2("The opening", Text(Words(fillerWords))),
            [H2("Where the hours go", Text(phrase)), H2("What to do next", Text("Book the call."))]);
    }

    [Fact]
    public void A_page_under_the_scores_floor_ships_with_the_gap_named_and_the_sections_that_never_use_it()
    {
        var page = Page(uses: 1, fillerWords: 1000);

        var finding = KeywordFinding(GccDraftGuard.Pillar(page, Inputs()));

        Assert.NotNull(finding);
        Assert.False(finding.Refuses);
        Assert.StartsWith("The pillar uses \"Automated Approval Workflows\" 1 time in 1,0", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("Its SEO score needs 0.4%.", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("Sections that never use it: \"The opening\", \"What to do next\".", finding.Detail, StringComparison.Ordinal);
        Assert.False(ScorePasses(page, "pillar"));
    }

    [Fact]
    public void A_page_inside_the_band_has_no_keyword_finding()
    {
        var page = Page(uses: 8, fillerWords: 1000);

        Assert.Null(KeywordFinding(GccDraftGuard.Pillar(page, Inputs())));
        Assert.Null(KeywordFinding(GccDraftGuard.Blog(page, Inputs())));
        Assert.True(ScorePasses(page, "pillar"));
    }

    [Fact]
    public void A_page_over_the_band_ships_and_says_it_reads_stuffed()
    {
        var page = Page(uses: 6, fillerWords: 100);

        var finding = KeywordFinding(GccDraftGuard.Blog(page, Inputs()));

        Assert.NotNull(finding);
        Assert.False(finding.Refuses);
        Assert.Contains("Its SEO score allows 2.5%; it reads stuffed.", finding.Detail, StringComparison.Ordinal);
        Assert.False(ScorePasses(page, "blog"));
    }

    [Fact]
    public void A_use_in_a_list_item_a_quotation_or_a_subsection_counts_and_a_lower_case_one_too()
    {
        var page = new ContentDocument(
            H2("The opening", Text(Words(1000))),
            [
                H2("In a list", new ListParagraph(false, [[new Run("automated approval workflows route.")]])),
                H2("In a quotation", new QuoteParagraph([new Run("Automated approval workflows, said the partner.")], "https://partner.test/page")),
                new Section("h2", "In a subsection", [Text("Prose.")], null, [H2("Deeper", Text("The automated approval workflows log."))]),
                H2("A shortening only", Text("The approval workflows and automated workflows are not the phrase.")),
            ]);

        // The tool guard, because a pillar does not carry a quotation at all.
        var finding = KeywordFinding(GccDraftGuard.Tool(page, Inputs()));

        Assert.NotNull(finding);
        Assert.StartsWith("The tool page uses \"Automated Approval Workflows\" 3 times in", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("Sections that never use it: \"The opening\", \"A shortening only\".", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void No_keyword_means_no_keyword_check()
    {
        var page = Page(uses: 0, fillerWords: 1000);

        Assert.Null(KeywordFinding(GccDraftGuard.Pillar(page, Inputs(keyword: null))));
        Assert.Null(KeywordFinding(GccDraftGuard.Tool(page, Inputs(keyword: "  "))));
    }

    [Fact]
    public void The_count_the_guard_reads_is_the_scores_own()
    {
        var page = Page(uses: 5, fillerWords: 1000);
        var json = JsonSerializer.Serialize(page, GccDocumentJson.Options);

        var counted = GcwSeoAnalyzer.CountKeyword(json, Keyword);
        var report = GcwSeoAnalyzer.Analyze(json, Keyword, "pillar");

        Assert.Equal(5, counted.Uses);
        Assert.Equal(report.WordCount, counted.Words);
        Assert.Equal(report.KeywordDensityPercent, Math.Round(counted.DensityPercent, 2));
    }
}
