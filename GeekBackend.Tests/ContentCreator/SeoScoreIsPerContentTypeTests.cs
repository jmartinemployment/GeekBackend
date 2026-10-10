using GeekAPI.Services.Gcw;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The SEO score is graded against the draft's own content type, and the keyword it measures is the
/// keyword the writer was asked for.
/// </summary>
/// <remarks>
/// <para>
/// Both halves were broken together, which is why one report could show three keyword checks failing
/// on a draft that discussed the keyword throughout, and a length check passing on a draft that was
/// 1,200 words short of its real floor (Jeff, 2026-10-02).
/// </para>
/// <para>
/// <b>The type.</b> <c>Analyze</c> had a two-argument overload that passed null, and null reaches
/// <c>GccLongFormTypes.Normalize</c>, which returns <c>blog</c>. So pillar and tool — 3,000-word
/// floors — were graded against 1,800. The overload is gone; the type is required.
/// </para>
/// <para>
/// <b>The keyword.</b> <c>GcwSeoAnalyzer</c> scored against <c>GccTargetKeyword.FromTopic</c> from
/// 2026-09-28, while <c>BuildMinimalContext</c> still handed the prompts the whole topic. The writer
/// was told to place <i>"Accounts Payable: Automated Data Entry &amp; Processing"</i> in a heading and
/// a lede verbatim. It appeared nowhere, so density was 0.00%.
/// </para>
/// </remarks>
public class SeoScoreIsPerContentTypeTests
{
    /// <summary>~500 words, three sections, the keyword used in the lede and in exactly one H2 —
    /// written with "and" where the keyword has "&amp;", which is what real prose does.</summary>
    private static string DraftJson(int bodyRepeats)
    {
        var filler = string.Join(" ", Enumerable.Repeat("teams reconcile invoices against purchase orders every month", bodyRepeats));
        return $$"""
            {"lede":{"tag":"h2","heading":"Automated Data Entry and Processing","paragraphs":[
              {"type":"text","runs":[{"text":"Automated Data Entry and Processing replaces the manual keying that slows accounts payable."}]}],
              "href":null,"children":[]},
             "sections":[
              {"tag":"h2","heading":"Where manual entry breaks down","paragraphs":[{"type":"text","runs":[{"text":"{{filler}}"}]}],"href":null,"children":[]},
              {"tag":"h2","heading":"What Automated Data Entry and Processing changes","paragraphs":[{"type":"text","runs":[{"text":"{{filler}}"}]}],"href":null,"children":[]},
              {"tag":"h2","heading":"Choosing an approach","paragraphs":[{"type":"text","runs":[{"text":"{{filler}}"}]}],"href":null,"children":[]}]}
            """;
    }

    private const string Keyword = "Automated Data Entry & Processing";

    [Fact]
    public void An_ampersand_keyword_is_found_in_prose_that_spells_it_and()
    {
        // The 0.00% density report. The draft uses the keyword four times; an exact-substring match
        // over "&" found none of them.
        var report = GcwSeoAnalyzer.Analyze(DraftJson(1), Keyword, "blog");

        Assert.True(report.KeywordDensityPercent > 0, $"density was {report.KeywordDensityPercent}%");
        Assert.True(Passed(report, "keyword-in-lede"));
        Assert.True(Passed(report, "keyword-in-heading"));
    }

    [Fact]
    public void A_pillar_is_graded_against_the_pillar_floor_not_the_blog_floor()
    {
        // ~2,000 words: over the blog's 1,800, under the pillar's 3,000. The two must disagree, which
        // is the whole claim -- before this, both said "blog".
        var draft = DraftJson(110);

        var asBlog = GcwSeoAnalyzer.Analyze(draft, Keyword, "blog");
        var asPillar = GcwSeoAnalyzer.Analyze(draft, Keyword, "pillar");

        Assert.InRange(asBlog.WordCount, 1800, 2999);
        Assert.True(Passed(asBlog, "word-count"));
        Assert.False(Passed(asPillar, "word-count"));
        Assert.True(asPillar.Score < asBlog.Score);
    }

    [Fact]
    public void A_tool_page_is_graded_as_the_most_important_type_not_as_a_blog()
    {
        // Jeff, 2026-09-28: "Tool is the most important Content Type and at very least should equal a
        // Pillar on every measure." Graded as a blog it passed on length 1,200 words short.
        var draft = DraftJson(110);

        var asTool = GcwSeoAnalyzer.Analyze(draft, Keyword, "tool");
        var asPillar = GcwSeoAnalyzer.Analyze(draft, Keyword, "pillar");

        Assert.False(Passed(asTool, "word-count"));
        Assert.Equal(asPillar.Score, asTool.Score);
    }

    [Fact]
    public void A_short_form_type_is_not_graded_on_length_at_all()
    {
        // An email has no word floor, so it must not be handed a pillar's -- the per-type dispatch has
        // to go both ways or "per content type" just means "a different number".
        var report = GcwSeoAnalyzer.Analyze(DraftJson(1), Keyword, "email");

        Assert.DoesNotContain("word-count", report.Checks.Select(c => c.Id));
        Assert.DoesNotContain("section-count", report.Checks.Select(c => c.Id));
    }

    [Fact]
    public void The_fix_hints_name_the_keyword_the_writer_was_asked_for()
    {
        // ApplyFeedback is fed back to the revise model, so a hint naming the whole topic would ask the
        // writer to do the thing that caused the 0.00% in the first place.
        var report = GcwSeoAnalyzer.Analyze(
            """{"lede":{"tag":"h2","heading":"Overview","paragraphs":[{"type":"text","runs":[{"text":"Nothing relevant here."}]}],"href":null,"children":[]},"sections":[]}""",
            Keyword,
            "blog");

        Assert.Contains(Keyword, report.ApplyFeedback, StringComparison.Ordinal);
        Assert.DoesNotContain("Accounts Payable:", report.ApplyFeedback, StringComparison.Ordinal);
    }

    private static bool Passed(GcwSeoAnalyzer.SeoReport report, string checkId) =>
        report.Checks.Single(c => c.Id == checkId).Passed;
}
