using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What a batch of a page owes when it comes back: its words, its share of the keyword, and the
/// keyword's heading when it is the batch that carries it.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-05, reading a tool page's SEO report -- score 40, "No section heading includes the
/// keyword", density 0.20%, 2,024 words against 3,000: "these SEO hints should already be applied to
/// all content types". The writer had been told all three and was held only to length, and only on
/// the types whose sections declare one.
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

    private static IReadOnlyList<GccGenerateService.BatchShortfall> Owed(
        IReadOnlyList<Section> sections, string? keyword = Keyword, int mentions = 3, bool heading = true) =>
        GccGenerateService.BatchShortfalls(sections, TwoSlots, Label, keyword, mentions, heading);

    [Fact]
    public void A_batch_that_delivers_its_words_its_keyword_share_and_its_heading_owes_nothing()
    {
        Section[] sections =
        [
            H2("What Automated Approval Workflows replace", Words(600), "Automated Approval Workflows route each invoice."),
            H2("How the routing is set up", Words(500), "With automated approval workflows the approver is named up front."),
        ];

        Assert.Empty(Owed(sections));
    }

    [Fact]
    public void Words_under_the_floor_are_reported_in_the_sentence_the_operator_already_reads()
    {
        Section[] sections =
        [
            H2("What Automated Approval Workflows replace", Words(470), "Automated Approval Workflows, Automated Approval Workflows."),
            H2("How it is set up", "Automated Approval Workflows again."),
        ];

        var shortfall = Assert.Single(Owed(sections));

        Assert.StartsWith(Label + " is ", shortfall.Report, StringComparison.Ordinal);
        Assert.EndsWith("words against a 1,100-word floor", shortfall.Report, StringComparison.Ordinal);
    }

    [Fact]
    public void The_keyword_is_counted_as_the_exact_phrase_and_a_variant_is_not_counted()
    {
        // What the Stampli page of 2026-10-05 did: "approval workflows" and "automated workflows"
        // throughout, the phrase itself rarely. The scorer counts the phrase; so does this.
        Section[] sections =
        [
            H2(
                "What Automated Approval Workflows replace",
                Words(650),
                "Approval workflows route each invoice. Automated workflows cut the wait. Automation helps approvals."),
            H2("How it is set up", Words(520), "automated  approval\nworkflows are configured once."),
        ];

        var shortfall = Assert.Single(Owed(sections, mentions: 6));

        // The heading and the one mention in the prose, whatever their case or spacing: two.
        Assert.Equal(
            Label + " uses \"Automated Approval Workflows\" 2 time(s) against the 6 it owes", shortfall.Report);
    }

    [Fact]
    public void An_ampersand_in_the_keyword_is_the_word_and()
    {
        Section[] sections =
        [
            H2("Why Automated Data Entry and Processing pays for itself", Words(650), "Automated data entry and processing removes rekeying."),
            H2("How it is set up", Words(520), "Automated Data Entry & Processing starts with capture."),
        ];

        Assert.Empty(Owed(sections, keyword: "Automated Data Entry & Processing", mentions: 3));
    }

    [Fact]
    public void The_batch_that_carries_the_keywords_heading_is_told_when_no_heading_has_it()
    {
        // The two headings the Stampli page actually wrote. Each is one word away from the keyword.
        Section[] sections =
        [
            H2("The Challenges of Manual Approval Workflows", Words(650), "Automated Approval Workflows. Automated Approval Workflows."),
            H2("How Stampli Transforms Approval Workflows", Words(520), "Automated Approval Workflows."),
        ];

        var shortfall = Assert.Single(Owed(sections));

        Assert.Equal(Label + " has no heading containing \"Automated Approval Workflows\"", shortfall.Report);
        // A later batch does not carry that heading and is not asked for it.
        Assert.Empty(Owed(sections, heading: false));
    }

    [Fact]
    public void A_page_with_no_keyword_owes_its_words_and_nothing_else()
    {
        Section[] sections = [H2("One", Words(300)), H2("Two", Words(300))];

        var shortfall = Assert.Single(Owed(sections, keyword: "  ", mentions: 0));
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("tool")]
    public void A_batchs_share_of_the_keyword_adds_up_to_a_page_inside_the_scorers_band(string type)
    {
        // Three batches of two sections on a six-section page: each owes a third of the page's count,
        // and the page's count sits between the scorer's 0.4% and 2.5% of its word floor.
        var (minWords, _, _) = GeekAPI.Services.ContentCreatorV2.ContentTypes.GccV2LongFormTypes.GetSeoLengthRules(type);
        var perBatch = ContentPromptBuilder.SeoKeywordMentionsFor(type, 2, 6);
        var wholePage = ContentPromptBuilder.SeoKeywordMentionsFor(type, 6, 6);

        Assert.InRange(perBatch * 3, wholePage - 1, wholePage + 1);
        Assert.InRange(100.0 * wholePage / minWords, 0.4, 2.5);
    }
}
