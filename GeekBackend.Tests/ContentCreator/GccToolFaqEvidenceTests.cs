using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// <see cref="GccToolFaqEvidence"/> -- what a search of the partner's crawl found for each of the
/// operator's FAQ questions, found by host and question and rendered for one call. It searches and
/// selects nothing: the search is <c>GccGroundingResolver</c>'s, one per question. Until 2026-10-10
/// this class picked paragraphs by shared words out of passages retrieved for other questions, and a
/// question none of those was about was reported as one no page of the partner's answered.
/// </summary>
public class GccToolFaqEvidenceTests
{
    private const string Host = "partner.test";
    private const string Question = "Does Partner Widget sync with QuickBooks Online?";

    private static GccQuoteablePage Page(string title, string url, params string[] paragraphs) =>
        new(Url: url, Title: title, Headings: [], Paragraphs: paragraphs);

    [Fact]
    public void A_search_says_whether_a_reranking_model_ordered_it_in_the_librarys_own_word()
    {
        Assert.False(new GccFaqEvidence(Host, Question, []).Reranked);
        Assert.False(new GccFaqEvidence(Host, Question, [], "llamaindex-hybrid").Reranked);
        Assert.True(new GccFaqEvidence(Host, Question, [], "llamaindex-hybrid+rerank").Reranked);
    }

    [Fact]
    public void Each_passages_score_is_listed_with_its_page_for_the_run_log()
    {
        var scored = Page("Product updates", "https://partner.test/updates", "Forecasts run what-if simulations.", "Custom views.") with
        {
            Scores = [new GccPassageScore(0.03, 6.1), new GccPassageScore(0.02, -1.4)],
        };
        var unscored = Page("Typed by hand", "https://partner.test/notes", "A page that came from no search.");

        var json = System.Text.Json.JsonSerializer.Serialize(GccToolFaqEvidence.ScoresOf([scored, unscored]));

        Assert.Equal(
            """[{"url":"https://partner.test/updates","score":0.03,"reranked":6.1},{"url":"https://partner.test/updates","score":0.02,"reranked":-1.4}]""",
            json);
    }

    [Fact]
    public void Research_that_carries_no_searches_has_not_searched_for_any_question()
    {
        Assert.Null(GccToolFaqEvidence.FoundFor(null, Host, Question));
    }

    [Fact]
    public void A_question_with_no_entry_was_not_searched_for()
    {
        var searched = new[] { new GccFaqEvidence(Host, "Does Partner Widget fly?", []) };

        Assert.Null(GccToolFaqEvidence.FoundFor(searched, Host, Question));
    }

    [Fact]
    public void A_search_that_found_nothing_is_an_empty_answer_and_not_a_missing_one()
    {
        var searched = new[] { new GccFaqEvidence(Host, Question, []) };

        var found = GccToolFaqEvidence.FoundFor(searched, Host, Question);

        Assert.NotNull(found);
        Assert.Empty(found);
        Assert.Equal(0, GccToolFaqEvidence.PassageCount(found));
    }

    [Fact]
    public void A_question_is_found_by_its_own_host_and_its_exact_words()
    {
        var mine = Page("Integrations", "https://partner.test/integrations", "Every payment syncs to QuickBooks Online.");
        var theirs = Page("Rival", "https://rival.test/integrations", "Rival syncs to Xero.");
        var searched = new[]
        {
            new GccFaqEvidence("rival.test", Question, [theirs]),
            new GccFaqEvidence(Host, Question, [mine]),
        };

        var found = GccToolFaqEvidence.FoundFor(searched, Host, Question);

        Assert.Equal([mine], found);
        // The key is the question as written: a reworded one is another question, never a near match.
        Assert.Null(GccToolFaqEvidence.FoundFor(searched, Host, "Does Partner Widget sync with QuickBooks?"));
        Assert.Null(GccToolFaqEvidence.FoundFor(searched, string.Empty, Question));
    }

    [Fact]
    public void A_host_searched_in_two_runs_adds_what_each_found()
    {
        var first = Page("Integrations", "https://partner.test/integrations", "Every payment syncs to QuickBooks Online.");
        var second = Page("Sync", "https://partner.test/sync", "The sync runs nightly.");
        var searched = new[]
        {
            new GccFaqEvidence(Host, Question, [first]),
            new GccFaqEvidence("PARTNER.TEST", Question, [second]),
        };

        Assert.Equal([first, second], GccToolFaqEvidence.FoundFor(searched, Host, Question));
    }

    [Fact]
    public void Passages_are_counted_as_the_writer_is_shown_them()
    {
        var pages = new[]
        {
            Page("A", "https://partner.test/a", "One.", "  ", "Two."),
            Page("B", "https://partner.test/b", "Three."),
        };

        Assert.Equal(3, GccToolFaqEvidence.PassageCount(pages));
    }

    [Fact]
    public void Each_question_is_rendered_under_the_label_the_prompt_lists_it_by()
    {
        var rendered = GccToolFaqEvidence.Render(
        [
            (Question, [Page("Integrations", "https://partner.test/integrations", "Every payment syncs to QuickBooks Online.")]),
            ("What does Partner Widget cost?",
            [
                Page("Pricing", "https://partner.test/pricing", "Partner Widget starts at $19 per month.", "Billed monthly."),
            ]),
        ]);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "Found for Q1:",
                "[Integrations] (https://partner.test/integrations)",
                "  - Every payment syncs to QuickBooks Online.",
                "",
                "Found for Q2:",
                "[Pricing] (https://partner.test/pricing)",
                "  - Partner Widget starts at $19 per month.",
                "  - Billed monthly."),
            rendered);
    }

    [Fact]
    public void A_passage_found_for_one_question_is_not_shown_under_another()
    {
        var rendered = GccToolFaqEvidence.Render(
        [
            (Question, [Page("Integrations", "https://partner.test/integrations", "Every payment syncs to QuickBooks Online.")]),
            ("What does Partner Widget cost?", [Page("Pricing", "https://partner.test/pricing", "Partner Widget starts at $19 per month.")]),
        ]);

        var underTheSecond = rendered[rendered.IndexOf("Found for Q2:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("QuickBooks", underTheSecond, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_whose_passages_are_all_blank_is_not_named()
    {
        var rendered = GccToolFaqEvidence.Render(
        [
            (Question,
            [
                Page("Blank", "https://partner.test/blank", " ", ""),
                Page("Integrations", "https://partner.test/integrations", "Every payment syncs to QuickBooks Online."),
            ]),
        ]);

        Assert.DoesNotContain("https://partner.test/blank", rendered, StringComparison.Ordinal);
        Assert.Contains("https://partner.test/integrations", rendered, StringComparison.Ordinal);
    }
}
