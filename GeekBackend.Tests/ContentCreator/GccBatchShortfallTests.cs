using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What a batch of a page owes when it comes back: its words. Not a keyword count, and not the
/// keyword's heading.
/// </summary>
/// <remarks>
/// <para>
/// Jeff, 2026-10-05, reading a tool page's SEO report -- score 40, "No section heading includes the
/// keyword", density 0.20%, 2,024 words against 3,000: "these SEO hints should already be applied to
/// all content types". The writer had been told all three and was held only to length, and only on
/// the types whose sections declare one.
/// </para>
/// <para>
/// The count was then owed a call at a time, and warned on 18 of 21 calls on 2026-10-07, nine of them
/// on pages the score passed. Jeff, 2026-10-10: "Do not rely on the LLM to count its own keyword
/// usage." A batch's uses are counted for the run's record and owed to nothing; the page is judged
/// once, by its score, in <c>GccDraftGuard</c>.
/// </para>
/// <para>
/// The heading was owed by the first call until 2026-10-10. That day's pillar was reported for a first
/// call with no keyword heading while eight of its later headings carried the phrase, and every tool
/// page's first call met the check with "... Manual Automated Accounts Receivable". Whether the page
/// has a keyword heading is judged on the finished page (<c>GccDraftGuardTests</c>).
/// </para>
/// </remarks>
public sealed class GccBatchShortfallTests
{
    private const string Keyword = "Automated Approval Workflows";
    private const string Label = "Tool page 'Stampli' sections 1-2";

    private static readonly SectionSlot[] TwoSlots =
    [
        SectionSlot.Cover("what the manual way costs", "600-850 words"),
        SectionSlot.Cover("how the product removes it", "500-700 words"),
    ];

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    private static Section H2(string heading, params string[] paragraphs) =>
        new("h2", heading, [.. paragraphs.Select(p => (Paragraph)new TextParagraph([new Run(p)]))], null, []);

    private static string? UnderFloor(IReadOnlyList<Section> sections) =>
        GccGenerateService.BatchUnderFloor(sections, TwoSlots, Label);

    [Fact]
    public void A_batch_that_delivers_its_words_owes_nothing()
    {
        Section[] sections =
        [
            H2("What Automated Approval Workflows replace", Words(600), "Automated Approval Workflows route each invoice."),
            H2("How the routing is set up", Words(500), "With automated approval workflows the approver is named up front."),
        ];

        Assert.Null(UnderFloor(sections));
    }

    [Fact]
    public void Words_under_the_floor_are_reported_in_the_sentence_the_operator_already_reads()
    {
        Section[] sections =
        [
            H2("What Automated Approval Workflows replace", Words(470), "Automated Approval Workflows, Automated Approval Workflows."),
            H2("How it is set up", "Automated Approval Workflows again."),
        ];

        var shortfall = UnderFloor(sections);

        // Listed only when the finished page is under its own floor (GccDraftIsGuardedOnceTests).
        Assert.NotNull(shortfall);
        Assert.StartsWith(Label + " is ", shortfall, StringComparison.Ordinal);
        Assert.EndsWith("words against a 1,100-word floor", shortfall, StringComparison.Ordinal);
    }

    [Fact]
    public void No_count_of_the_keyword_is_owed_by_a_batch()
    {
        // What the Stampli page of 2026-10-05 did: "approval workflows" and "automated workflows"
        // throughout, the phrase itself rarely. A batch that has its words is not warned for it; the
        // page is, once, by its score.
        Section[] sections =
        [
            H2(
                "What Automated Approval Workflows replace",
                Words(650),
                "Approval workflows route each invoice. Automated workflows cut the wait. Automation helps approvals."),
            H2("How it is set up", Words(520), "The approvers are configured once."),
        ];

        Assert.Null(UnderFloor(sections));
    }

    [Fact]
    public void A_batchs_uses_of_the_keyword_are_counted_as_the_exact_phrase_for_the_run_log()
    {
        Section[] sections =
        [
            H2(
                "What Automated Approval Workflows replace",
                Words(650),
                "Approval workflows route each invoice. Automated workflows cut the wait. Automation helps approvals."),
            H2("How it is set up", Words(520), "automated  approval\nworkflows are configured once."),
        ];

        // The heading and the one mention in the prose, whatever their case or spacing: two. A
        // shortened form is not one.
        Assert.Equal(2, GccGenerateService.KeywordUses(sections, Keyword));
        Assert.Equal(0, GccGenerateService.KeywordUses(sections, "  "));
    }

    [Fact]
    public void An_ampersand_in_the_keyword_is_the_word_and()
    {
        Section[] sections =
        [
            H2("Why Automated Data Entry and Processing pays for itself", Words(650), "Automated data entry and processing removes rekeying."),
            H2("How it is set up", Words(520), "Automated Data Entry & Processing starts with capture."),
        ];

        Assert.Equal(3, GccGenerateService.KeywordUses(sections, "Automated Data Entry & Processing"));
    }

    [Fact]
    public void No_heading_is_owed_by_a_batch()
    {
        // The two headings the Stampli page actually wrote. Each is one word away from the keyword, and
        // the call has its words: nothing is said of the call. The page says it, once, if no heading on
        // it carries the phrase.
        Section[] sections =
        [
            H2("The Challenges of Manual Approval Workflows", Words(650), "Automated Approval Workflows. Automated Approval Workflows."),
            H2("How Stampli Transforms Approval Workflows", Words(520), "Automated Approval Workflows."),
        ];

        Assert.Null(UnderFloor(sections));
    }

    [Fact]
    public void Slots_that_declare_no_floor_owe_no_words()
    {
        Section[] sections = [H2("One", Words(30)), H2("Two", Words(30))];

        Assert.Null(GccGenerateService.BatchUnderFloor(sections, [SectionSlot.Cover("one"), SectionSlot.Cover("two")], Label));
    }
}
