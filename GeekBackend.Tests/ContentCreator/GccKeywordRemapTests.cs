using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The writer's shortenings of the keyword are turned back into the exact phrase, where the grammar
/// allows it, until each section carries its share of the page's count.
/// </summary>
/// <remarks>
/// Jeff, 2026-10-10: "Do not rely on the LLM to count its own keyword usage to hit a target of 6 or 7
/// mentions. Let the LLM write naturally, and write a light post-processing utility that replaces
/// natural semantic variations with the strict required target phrase where grammatically
/// appropriate." The variations are the ones the run of 2026-10-07 wrote: the phrase with a word left
/// out, fifty-five times "approval workflows" and seven times "automated workflows".
/// </remarks>
public sealed class GccKeywordRemapTests
{
    private const string Keyword = "Automated Approval Workflows";

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    private static TextParagraph Text(params Run[] runs) => new(runs);

    private static TextParagraph Text(string text) => new([new Run(text)]);

    private static Section H2(string heading, params Paragraph[] paragraphs) => new("h2", heading, paragraphs, null, []);

    private static Section Lede(string text) => H2("The opening", Text(text));

    /// <summary>A thousand words of opening carrying the phrase once: the page's count is then six, three to each of two sections.</summary>
    private static Section LongLede() => Lede("Automated Approval Workflows are the subject. " + Words(1000));

    private static string TextOf(Paragraph paragraph) => paragraph switch
    {
        TextParagraph t => string.Concat(t.Runs.Select(r => r.Text)),
        ListParagraph l => string.Join(" | ", l.Items.Select(i => string.Concat(i.Select(r => r.Text)))),
        QuoteParagraph q => string.Concat(q.Runs.Select(r => r.Text)),
        _ => string.Empty,
    };

    [Fact]
    public void A_dropped_first_word_goes_back_where_the_slot_before_the_phrase_is_open()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("The approval workflows in Ramp route each invoice."),
                Text("Approval workflows are set up once, by the controller."),
                Text("Teams rely on approval workflows, and on nothing else.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal("The automated approval workflows in Ramp route each invoice.", paragraphs[0]);
        Assert.Equal("Automated approval workflows are set up once, by the controller.", paragraphs[1]);
        Assert.Equal("Teams rely on automated approval workflows, and on nothing else.", paragraphs[2]);
        Assert.Equal(1, remapped.Before);
        Assert.Equal(4, remapped.After);
        Assert.Equal(3, remapped.Edits.Count);
        Assert.All(remapped.Edits, e => Assert.Equal("Where the hours go", e.Heading));
        Assert.Equal(("approval workflows", "automated approval workflows"), (remapped.Edits[0].From, remapped.Edits[0].To));
    }

    [Fact]
    public void A_taken_slot_is_left_as_written()
    {
        // The words the writer actually put before "approval workflows" on 2026-10-07. "Manual automated
        // approval workflows" and "automating automated approval workflows" are wrong, so none of these
        // is touched.
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("Manual approval workflows cost hours every week."),
                Text("Automating approval workflows is the first step."),
                Text("Ramp's approval workflows are customizable."),
                Text("Multi-level approval workflows need a map."),
                Text("Smart approval workflows learn the exceptions.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        Assert.Empty(remapped.Edits);
        Assert.Equal(remapped.Before, remapped.After);
        Assert.Equal(document.Sections[0].Paragraphs.Select(TextOf), remapped.Document.Sections[0].Paragraphs.Select(TextOf));
    }

    [Fact]
    public void A_dropped_middle_word_goes_back_in_its_place_in_the_case_around_it()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("Automated workflows cut the wait to a day."),
                Text("With automated workflows the approver is named up front."),
                Text("Both Automated Workflows and the audit trail are kept.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal("Automated approval workflows cut the wait to a day.", paragraphs[0]);
        Assert.Equal("With automated approval workflows the approver is named up front.", paragraphs[1]);
        Assert.Equal("Both Automated Approval Workflows and the audit trail are kept.", paragraphs[2]);
    }

    [Fact]
    public void An_exact_use_is_never_extended_and_a_paragraph_that_has_one_gets_no_second()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("Automated approval workflows route each invoice, and the approval workflows are logged."),
                Text("The approval workflows are logged.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        // The first paragraph already carries the phrase: never twice in a paragraph.
        Assert.Equal("Automated approval workflows route each invoice, and the approval workflows are logged.", paragraphs[0]);
        Assert.Equal("The automated approval workflows are logged.", paragraphs[1]);
        Assert.Single(remapped.Edits);
    }

    [Fact]
    public void A_section_at_its_share_keeps_what_it_has_and_nothing_is_ever_removed()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("Automated approval workflows. Automated approval workflows. Automated approval workflows."),
                Text("Automated approval workflows, again, and automated approval workflows once more."),
                Text("The approval workflows are logged.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        Assert.Empty(remapped.Edits);
        Assert.Equal(6, remapped.Before);
        Assert.Equal(6, remapped.After);
        Assert.Equal("The approval workflows are logged.", TextOf(remapped.Document.Sections[0].Paragraphs[2]));
    }

    [Fact]
    public void The_edits_are_spread_over_the_paragraphs_that_offer_one()
    {
        // Six to the page, three to each of two sections; the section has four paragraphs offering an
        // edit and needs three, so the first, second and third of the four are taken -- not whichever
        // come first for a section needing two of four: that is the first and the third.
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("The approval workflows route."),
                Text("The approval workflows log."),
                Text("The approval workflows escalate."),
                Text("The approval workflows close.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal(3, remapped.Edits.Count);
        Assert.Equal("The automated approval workflows route.", paragraphs[0]);
        Assert.Equal("The automated approval workflows log.", paragraphs[1]);
        Assert.Equal("The automated approval workflows escalate.", paragraphs[2]);
        Assert.Equal("The approval workflows close.", paragraphs[3]);

        // Two needed of four offered: the first and the third.
        var two = new ContentDocument(
            Lede("Automated Approval Workflows are the subject. " + Words(500)),
            [document.Sections[0]]);
        var spread = GccKeywordRemap.Apply(two, Keyword).Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal("The automated approval workflows route.", spread[0]);
        Assert.Equal("The approval workflows log.", spread[1]);
        Assert.Equal("The automated approval workflows escalate.", spread[2]);
        Assert.Equal("The approval workflows close.", spread[3]);
    }

    [Fact]
    public void A_list_item_and_a_subsection_are_remapped_like_a_paragraph()
    {
        var document = new ContentDocument(LongLede(),
        [
            new Section("h2", "Where the hours go",
                [new ListParagraph(false, [[new Run("The approval workflows route.")], [new Run("The approval workflows log.")]])],
                null,
                [H2("Deeper", Text("The approval workflows escalate."))]),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        var list = Assert.IsType<ListParagraph>(remapped.Document.Sections[0].Paragraphs[0]);
        Assert.Equal("The automated approval workflows route.", string.Concat(list.Items[0].Select(r => r.Text)));
        Assert.Equal("The automated approval workflows log.", string.Concat(list.Items[1].Select(r => r.Text)));
        Assert.Equal("The automated approval workflows escalate.", TextOf(remapped.Document.Sections[0].Children[0].Paragraphs[0]));
        Assert.Equal("Deeper", remapped.Edits[2].Heading);
    }

    [Fact]
    public void Headings_quotations_links_and_formatted_runs_are_never_touched()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Approval Workflows That Scale",
                new QuoteParagraph([new Run("The approval workflows are the partner's words.")], "https://partner.test/page"),
                Text(new Run("The approval workflows", Href: "https://geek.test/tools/ramp"), new Run(" are linked.")),
                Text(new Run("The approval workflows", Bold: true), new Run(" are bold."))),
        ]);

        var remapped = GccKeywordRemap.Apply(document, Keyword);

        Assert.Empty(remapped.Edits);
        Assert.Equal("Approval Workflows That Scale", remapped.Document.Sections[0].Heading);
        Assert.Equal(document.Sections[0].Paragraphs.Select(TextOf), remapped.Document.Sections[0].Paragraphs.Select(TextOf));
    }

    [Fact]
    public void A_two_word_keyword_has_no_variation_and_a_synonym_is_not_one()
    {
        var invoice = new ContentDocument(LongLede(),
            [H2("Where the hours go", Text("The automation of invoices is the point, and invoice capture is not."))]);
        Assert.Empty(GccKeywordRemap.Apply(invoice, "Invoice Automation").Edits);

        var receivable = new ContentDocument(LongLede(),
            [H2("Where the hours go", Text("AR automation and automated invoicing are what collections processing needs."))]);
        Assert.Empty(GccKeywordRemap.Apply(receivable, "Automated Accounts Receivable").Edits);

        var lastWord = new ContentDocument(LongLede(),
            [H2("Where the hours go", Text("The automated approval of every invoice is logged."))]);
        // "automated approval" is a different noun; the last word is never put back.
        Assert.Empty(GccKeywordRemap.Apply(lastWord, Keyword).Edits);
    }

    [Fact]
    public void An_ampersand_and_the_word_and_are_the_same_word()
    {
        var document = new ContentDocument(LongLede(),
            [H2("Where the hours go", Text("The data entry and processing step runs nightly."), Text("The Data Entry & Processing step is logged."))]);

        var remapped = GccKeywordRemap.Apply(document, "Automated Data Entry & Processing");

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal("The automated data entry and processing step runs nightly.", paragraphs[0]);
        Assert.Equal("The Automated Data Entry & Processing step is logged.", paragraphs[1]);
    }

    [Fact]
    public void A_keyword_word_with_a_capital_of_its_own_keeps_it_wherever_it_goes()
    {
        var document = new ContentDocument(LongLede(),
        [
            H2("Where the hours go",
                Text("The QuickBooks sync runs nightly."),
                Text("QuickBooks sync runs nightly, and nothing else does."),
                Text("The automated sync runs nightly.")),
        ]);

        var remapped = GccKeywordRemap.Apply(document, "Automated QuickBooks Sync");

        var paragraphs = remapped.Document.Sections[0].Paragraphs.Select(TextOf).ToList();
        Assert.Equal("The automated QuickBooks sync runs nightly.", paragraphs[0]);
        Assert.Equal("Automated QuickBooks sync runs nightly, and nothing else does.", paragraphs[1]);
        Assert.Equal("The automated QuickBooks sync runs nightly.", paragraphs[2]);
    }

    [Fact]
    public void The_target_is_a_share_of_the_pages_words_inside_the_scorers_band_and_the_shares_add_up()
    {
        Assert.Equal(21, GccKeywordRemap.TargetFor(3500));
        Assert.Equal(4, GccKeywordRemap.TargetFor(100));
        foreach (var words in new[] { 700, 1800, 3000, 3500, 5000 })
        {
            Assert.InRange(100.0 * GccKeywordRemap.TargetFor(words) / words, 0.4, 2.5);
        }

        var shares = GccKeywordRemap.Shares(18, 11);
        Assert.Equal(11, shares.Count);
        Assert.Equal(18, shares.Sum());
        Assert.InRange(shares.Max() - shares.Min(), 0, 1);
        Assert.Empty(GccKeywordRemap.Shares(18, 0));
    }

    [Fact]
    public void No_keyword_means_nothing_is_done_and_nothing_is_counted()
    {
        var document = new ContentDocument(LongLede(), [H2("Where the hours go", Text("The approval workflows route."))]);

        var remapped = GccKeywordRemap.Apply(document, "  ");

        Assert.Same(document, remapped.Document);
        Assert.Equal(0, remapped.Target);
        Assert.Empty(remapped.Edits);
    }
}
