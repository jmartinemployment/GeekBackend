using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Gcw;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Where the keyword must be on a page: its title, its meta description and its opening, read by the
/// page's score and listed by its run when one is missing.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-13: "SEO skill requires, in which case it should be implemented." His
/// <c>seo-content-brief</c> skill lists where the primary keyword must appear: the title tag and the H1
/// (both the title), the URL slug (a pillar's and a blog's is made from the title), the meta description
/// and the first paragraph. Until then the score read the opening and a heading, no run listed a missing
/// opening, nothing read the title or the meta description, and only the pillar's request asked for the
/// keyword in its meta description.
/// </remarks>
public sealed class KeywordPlacementTests
{
    private const string Keyword = "Payroll Tax & Worker Misclassification";

    private static Section H2(string heading, string text) =>
        new("h2", heading, [new TextParagraph([new Run(text)])], null, []);

    private static ContentDocument Body(string opening) => new(
        H2("Payroll tax and worker misclassification, without the penalties", opening),
        [H2("Where it goes wrong", "A contractor is paid as an employee would be.")]);

    private static string Saved(string? title, string? metaDescription, string opening = "Payroll tax and worker misclassification cost more each quarter.") =>
        JsonSerializer.Serialize(
            new { title, metaDescription, summary = "A standfirst.", body = Body(opening) }, GccDocumentJson.Options);

    private static GcwSeoAnalyzer.SeoCheck? Check(GcwSeoAnalyzer.SeoReport report, string id) =>
        report.Checks.SingleOrDefault(c => c.Id == id);

    private static GccGuardInputs Inputs(string? keyword = Keyword) => new(
        null,
        [],
        null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        string.Empty,
        Keyword: keyword);

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public void The_score_passes_a_page_whose_title_and_meta_description_carry_the_keyword(string type)
    {
        var report = GcwSeoAnalyzer.Analyze(
            Saved(
                "Payroll Tax and Worker Misclassification: What It Costs",
                "How payroll tax & worker misclassification errors are found before a filing."),
            Keyword,
            type);

        Assert.True(Check(report, "keyword-in-title")!.Passed);
        Assert.True(Check(report, "keyword-in-meta-description")!.Passed);
        Assert.True(Check(report, "keyword-in-lede")!.Passed);
    }

    [Fact]
    public void The_score_fails_a_title_and_a_meta_description_without_the_keyword_and_says_which()
    {
        var report = GcwSeoAnalyzer.Analyze(
            Saved("Getting Payroll Right", "A guide to paying people correctly."), Keyword, "pillar");

        var title = Check(report, "keyword-in-title")!;
        Assert.False(title.Passed);
        Assert.Equal("The title, “Getting Payroll Right”, does not include the target keyword.", title.Detail);
        Assert.Equal($"Put “{Keyword}” in the title, near its start.", title.FixHint);

        var meta = Check(report, "keyword-in-meta-description")!;
        Assert.False(meta.Passed);
        Assert.Equal("The meta description does not include the target keyword.", meta.Detail);
    }

    [Fact]
    public void A_half_of_the_keyword_in_the_title_is_not_the_keyword()
    {
        var report = GcwSeoAnalyzer.Analyze(
            Saved("Payroll Tax: What It Costs", "Worker misclassification and what it costs."), Keyword, "blog");

        Assert.False(Check(report, "keyword-in-title")!.Passed);
        Assert.False(Check(report, "keyword-in-meta-description")!.Passed);
    }

    [Fact]
    public void A_page_saved_with_no_title_or_meta_description_fails_both_and_says_so()
    {
        var report = GcwSeoAnalyzer.Analyze(Saved(null, null), Keyword, "pillar");

        Assert.Equal("The page has no title.", Check(report, "keyword-in-title")!.Detail);
        Assert.Equal("The page has no meta description.", Check(report, "keyword-in-meta-description")!.Detail);
    }

    [Fact]
    public void A_bare_document_and_a_short_piece_are_not_scored_on_a_title_they_do_not_have()
    {
        var bare = GcwSeoAnalyzer.Analyze(
            JsonSerializer.Serialize(Body("Payroll tax and worker misclassification."), GccDocumentJson.Options), Keyword, "pillar");
        var email = GcwSeoAnalyzer.Analyze(Saved("A subject", "A preview."), Keyword, "email");

        Assert.Null(Check(bare, "keyword-in-title"));
        Assert.Null(Check(bare, "keyword-in-meta-description"));
        Assert.Null(Check(email, "keyword-in-title"));
        Assert.Null(Check(email, "keyword-in-meta-description"));
    }

    [Fact]
    public void A_run_lists_a_title_and_a_meta_description_without_the_keyword()
    {
        var findings = GccDraftGuard.KeywordPlacement(
            "pillar", Keyword, "Getting Payroll Right", "A guide to paying people correctly.");

        Assert.Equal(["keyword-title", "keyword-meta-description"], findings.Select(f => f.Check).ToArray());
        Assert.All(findings, f => Assert.False(f.Refuses));
        Assert.Equal(
            "The pillar's title, \"Getting Payroll Right\", does not contain \"Payroll Tax & Worker Misclassification\". "
            + "Its SEO score needs it there.",
            findings[0].Detail);
        Assert.Equal(
            "The pillar's meta description does not contain \"Payroll Tax & Worker Misclassification\". Its SEO score needs it there.",
            findings[1].Detail);
    }

    [Fact]
    public void A_run_lists_nothing_when_both_carry_it_and_and_is_the_same_word_as_the_ampersand()
    {
        Assert.Empty(GccDraftGuard.KeywordPlacement(
            "tool page",
            Keyword,
            "Gusto: Payroll Tax & Worker Misclassification",
            "How Gusto handles payroll tax and worker misclassification for a growing team."));
    }

    [Fact]
    public void A_meta_description_whose_keyword_was_cut_off_is_listed()
    {
        var cut = "A long description that reaches its limit just as it gets to payroll tax & worker misclassif";

        var finding = Assert.Single(GccDraftGuard.KeywordPlacement("blog", Keyword, $"{Keyword}: A Guide", cut));

        Assert.Equal("keyword-meta-description", finding.Check);
    }

    [Fact]
    public void No_keyword_means_no_placement_check()
    {
        Assert.Empty(GccDraftGuard.KeywordPlacement("pillar", null, "A Title", "A description."));
        Assert.Empty(GccDraftGuard.KeywordPlacement("pillar", "  ", "A Title", "A description."));
    }

    [Fact]
    public void A_run_lists_an_opening_without_the_keyword_and_none_with_it()
    {
        var without = GccDraftGuard.Pillar(Body("Contractors and employees are taxed differently."), Inputs());
        var with = GccDraftGuard.Pillar(Body("Payroll tax and worker misclassification cost more each quarter."), Inputs());

        var finding = Assert.Single(without.Findings, f => f.Check == "keyword-opening");
        Assert.False(finding.Refuses);
        Assert.Equal(
            "The pillar's opening does not contain \"Payroll Tax & Worker Misclassification\". Its SEO score needs it there.",
            finding.Detail);
        Assert.DoesNotContain(with.Findings, f => f.Check == "keyword-opening");
        Assert.DoesNotContain(GccDraftGuard.Tool(Body("Payroll tax and worker misclassification."), Inputs()).Findings, f => f.Check == "keyword-opening");
        Assert.Contains(GccDraftGuard.Tool(Body("Nothing of it."), Inputs()).Findings, f => f.Check == "keyword-opening");
    }

    [Fact]
    public void The_requests_wording_names_the_keyword()
    {
        Assert.Equal(
            "TITLE AND THE KEYWORD: the title contains \"Payroll Tax & Worker Misclassification\", as near its start as reads naturally.",
            ContentPromptBuilder.SeoTitleInstruction(Keyword));
        Assert.Contains(
            "the meta description contains \"Payroll Tax & Worker Misclassification\"",
            ContentPromptBuilder.SeoMetaDescriptionInstruction(Keyword),
            StringComparison.Ordinal);
    }
}
