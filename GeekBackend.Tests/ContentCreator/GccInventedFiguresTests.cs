using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A sentence that states a figure the writer was shown nowhere is taken out of the page, and the
/// page is kept (Jeff, 2026-10-13: "Stop the sentence, not the pillar or blog?").
/// </summary>
public sealed class GccInventedFiguresTests
{
    private const string Texas =
        "If you sell 1,000 units of a digital service in Texas, but the tool reads those as exempt, you could miss a nexus trigger entirely.";

    /// <summary>What three partner pages said, and so what the writer was shown.</summary>
    private const string Evidence = "California and Texas both have a $500,000 threshold with no transaction count. Most states use $100,000 or 200 transactions.";

    private static Section Section(string heading, IReadOnlyList<Paragraph> paragraphs, params Section[] children) =>
        new("h2", heading, paragraphs, null, children);

    private static TextParagraph Text(params Run[] runs) => new(runs);

    private static TextParagraph Text(string text) => new([new Run(text)]);

    private static ContentDocument Page(params Section[] sections) =>
        new(Section("Opening", [Text("An opening.")]), sections);

    private static string TextOf(Paragraph paragraph) => paragraph switch
    {
        TextParagraph text => string.Concat(text.Runs.Select(run => run.Text)),
        ListParagraph list => string.Join(" | ", list.Items.Select(item => string.Concat(item.Select(run => run.Text)))),
        _ => string.Empty,
    };

    [Fact]
    public void The_sentence_goes_and_the_sentences_either_side_stay_as_written()
    {
        var page = Page(Section("Where nexus is missed", [
            Text($"Most states use $100,000 or 200 transactions. {Texas} Texas sets its own threshold at $500,000."),
        ]));

        var result = GccInventedFigures.Remove(page, Evidence);

        Assert.Equal(
            "Most states use $100,000 or 200 transactions. Texas sets its own threshold at $500,000.",
            TextOf(result.Document.Sections[0].Paragraphs[0]));
        var removed = Assert.Single(result.Sentences);
        Assert.Equal(Texas, removed.Sentence);
        Assert.Equal(["1,000"], removed.Figures);
        Assert.Equal("Where nexus is missed", removed.Heading);
        Assert.Empty(result.Headings);
    }

    [Fact]
    public void A_page_with_no_such_figure_comes_back_as_it_was()
    {
        var page = Page(Section("Thresholds", [Text("Texas sets its threshold at $500,000. It counts 200 transactions nowhere.")]));

        var result = GccInventedFigures.Remove(page, Evidence);

        Assert.Same(page, result.Document);
        Assert.Empty(result.Sentences);
    }

    [Fact]
    public void A_last_sentence_leaves_no_space_behind_and_a_paragraph_that_was_all_one_sentence_goes()
    {
        var page = Page(Section("Thresholds", [
            Text($"Texas sets its own threshold. {Texas}"),
            Text(Texas),
            Text("A paragraph that stays."),
        ]));

        var result = GccInventedFigures.Remove(page, Evidence);

        Assert.Equal(
            ["Texas sets its own threshold.", "A paragraph that stays."],
            result.Document.Sections[0].Paragraphs.Select(TextOf));
        Assert.Equal(2, result.Sentences.Count);
    }

    [Fact]
    public void A_sentence_that_runs_across_bold_and_linked_text_goes_whole_and_the_formatting_either_side_stays()
    {
        var page = Page(Section("Thresholds", [
            Text(
                new Run("Zamp files in every state. If you sell "),
                new Run("1,000 units", Bold: true),
                new Run(" in Texas you may owe. "),
                new Run("Zamp", Href: "/tools/finance/zamp"),
                new Run(" tracks the threshold.")),
        ]));

        var result = GccInventedFigures.Remove(page, Evidence);

        var runs = Assert.IsType<TextParagraph>(result.Document.Sections[0].Paragraphs[0]).Runs;
        Assert.Equal(
            [("Zamp files in every state. ", false, null), ("Zamp", false, "/tools/finance/zamp"), (" tracks the threshold.", false, null)],
            runs.Select(run => (run.Text, run.Bold, run.Href)));
        Assert.Equal("If you sell 1,000 units in Texas you may owe.", Assert.Single(result.Sentences).Sentence);
    }

    [Fact]
    public void A_list_item_goes_and_a_list_left_with_no_item_goes()
    {
        var list = new ListParagraph(false, [[new Run("Register in each state.")], [new Run("Expect 40 hours of filing a month.")]]);
        var lone = new ListParagraph(true, [[new Run("Budget $9,999 for setup.")]]);
        var page = Page(Section("What to do", [Text("Three steps follow."), list, lone]));

        var result = GccInventedFigures.Remove(page, Evidence);

        Assert.Equal(["Three steps follow.", "Register in each state."], result.Document.Sections[0].Paragraphs.Select(TextOf));
        Assert.Equal(["40 hours", "$9,999"], result.Sentences.SelectMany(s => s.Figures));
    }

    /// <summary>
    /// A question of the page's FAQ whose whole answer was the sentence: the question goes with it and
    /// is named, and the questions beside it stay.
    /// </summary>
    [Fact]
    public void A_heading_under_another_that_is_left_with_nothing_goes_with_its_sentence_and_is_named()
    {
        var faq = Section(
            "Frequently Asked Questions",
            [],
            new Section("h3", "When does Texas apply?", [Text(Texas)], null, []),
            new Section("h3", "What is the common rule?", [Text("Most states use $100,000 or 200 transactions.")], null, []));

        var result = GccInventedFigures.Remove(Page(faq), Evidence);

        Assert.Equal(["What is the common rule?"], result.Document.Sections[0].Children.Select(child => child.Heading));
        Assert.Equal(["When does Texas apply?"], result.Headings);
        Assert.Contains(
            "The heading \"When does Texas apply?\" went with it, having nothing else under it.",
            GccInventedFigures.Line("pillar", result),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What is left for the check to refuse, as it always did: one of the page's own sections that
    /// would have nothing under it, the opening likewise, and a quotation, which is not edited.
    /// </summary>
    [Fact]
    public void One_of_the_pages_own_sections_is_never_left_bare_and_a_quotation_is_never_edited()
    {
        var bare = Page(Section("Texas", [Text(Texas)]));
        var opening = new ContentDocument(Section("Opening", [Text(Texas)]), [Section("Body", [Text("A body.")])]);
        var quoted = Page(Section("What they say", [new QuoteParagraph([new Run(Texas)], "https://partner.test/page")]));

        foreach (var page in new[] { bare, opening, quoted })
        {
            var result = GccInventedFigures.Remove(page, Evidence);

            Assert.Same(page, result.Document);
            Assert.Empty(result.Sentences);
            Assert.Empty(result.Headings);
        }
    }

    [Theory]
    [InlineData("It costs 4.5 times more. That is real.", 2)]
    [InlineData("Read bill.com first. Then decide.", 2)]
    [InlineData("Some tools, e.g. a late invoice tracker, help. Others do not.", 2)]
    [InlineData("The U.S. Supreme Court ruled in Wayfair. States followed.", 2)]
    [InlineData("J. Smith filed late. The penalty followed.", 2)]
    [InlineData("Is that enough? It is not! Register now.", 3)]
    [InlineData("She said \"file now.\" He did.", 2)]
    [InlineData("One sentence with no stop", 1)]
    public void A_paragraph_is_divided_into_its_sentences(string text, int count)
    {
        var sentences = GccInventedFigures.SentencesOf(text);

        Assert.Equal(count, sentences.Count);
        // They cover the text exactly, in order, each taking the space after it.
        Assert.Equal(text, string.Concat(sentences.Select(s => text[s.Start..s.End])));
    }

    [Fact]
    public void The_operators_line_names_each_sentence_with_its_number_and_its_heading()
    {
        var page = Page(
            Section("Where nexus is missed", [Text($"A rule. {Texas}")]),
            Section("Costs", [Text("Filing takes 40 hours a month. It adds up.")]));

        var result = GccInventedFigures.Remove(page, Evidence);

        Assert.Equal(
            "The blog: 2 sentences were deleted because each states a number that is in neither the Brief nor the research: "
            + $"\"{Texas}\" (1,000), under \"Where nexus is missed\"; "
            + "\"Filing takes 40 hours a month.\" (40 hours), under \"Costs\". The rest of the page is as written.",
            GccInventedFigures.Line("blog", result));
    }
}
