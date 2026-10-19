using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// A heading split by a colon keeps the part before it (Jeff, 2026-10-18: "no more colons"). The three
/// below are MarketMuse's tool page headings from the run of 15:06 UTC that day, the first run whose
/// writer was told a heading is one phrase.
/// </summary>
public sealed class GccHeadingColonTests
{
    private const string Keyword = "Intelligent Content Planning";

    private static Section H2(string heading, params Section[] children) =>
        new("h2", heading, [new TextParagraph([new Run("Body text.")])], null, children);

    private static ContentDocument Doc(params Section[] sections) => new(H2("The Opening"), sections);

    [Theory]
    [InlineData("What Happens Inside Marketmuse: The Real Process, Step by Step", "What Happens Inside Marketmuse")]
    [InlineData("How Marketmuse Is Built: Architecture, Data, and What It Connects", "How Marketmuse Is Built")]
    [InlineData("What Gets Configured in Marketmuse: Rules, Automation Logic, and Extension Limits", "What Gets Configured in Marketmuse")]
    [InlineData("The Real Cost of Doing Content Planning by Hand:", "The Real Cost of Doing Content Planning by Hand")]
    public void The_part_before_the_colon_is_kept(string written, string saved)
    {
        var (document, cuts) = GccHeadingColon.Remove(Doc(H2(written)), Keyword);

        Assert.Equal(saved, document.Sections[0].Heading);
        Assert.Equal(new GccHeadingColon.Cut(written, saved), Assert.Single(cuts));
    }

    [Theory]
    [InlineData("Can the tool post at 9:30 each morning?")]
    [InlineData("Overview: What This Page Settles")]
    [InlineData("Posting at 9:30 Every Morning")]
    [InlineData("What Manual Planning Costs a Small Team")]
    public void A_question_a_one_word_lead_and_a_heading_with_no_split_are_left_as_written(string heading)
    {
        var (document, cuts) = GccHeadingColon.Remove(Doc(H2(heading)), Keyword);

        Assert.Equal(heading, document.Sections[0].Heading);
        Assert.Empty(cuts);
    }

    [Fact]
    public void The_pages_only_keyword_heading_is_not_cut_away()
    {
        const string only = "How It Runs Once Automated: Intelligent Content Planning Step by Step";

        var (alone, aloneCuts) = GccHeadingColon.Remove(Doc(H2(only), H2("What It Costs a Small Team")), Keyword);
        Assert.Equal(only, alone.Sections[0].Heading);
        Assert.Empty(aloneCuts);

        var (withAnother, _) = GccHeadingColon.Remove(
            Doc(H2(only), H2("Where Intelligent Content Planning Pays Off First")), Keyword);
        Assert.Equal("How It Runs Once Automated", withAnother.Sections[0].Heading);
    }

    [Fact]
    public void The_openings_heading_and_a_subheading_are_cut_and_the_body_is_kept()
    {
        var document = new ContentDocument(
            H2("Breaking the Cycle: Content Chaos for Small Teams"),
            [H2("How Marketmuse Is Built", H2("The Data Layer: What It Holds and Where"))]);

        var (cut, cuts) = GccHeadingColon.Remove(document, Keyword);

        Assert.Equal("Breaking the Cycle", cut.Lede.Heading);
        Assert.Equal("How Marketmuse Is Built", cut.Sections[0].Heading);
        Assert.Equal("The Data Layer", cut.Sections[0].Children[0].Heading);
        Assert.Equal(2, cuts.Count);
        Assert.Equal("Body text.", ((TextParagraph)cut.Sections[0].Children[0].Paragraphs[0]).Runs[0].Text);
    }

    [Fact]
    public void A_page_with_no_colon_heading_is_the_same_page()
    {
        var document = Doc(H2("How Marketmuse Is Built"));

        var (same, cuts) = GccHeadingColon.Remove(document, Keyword);

        Assert.Same(document, same);
        Assert.Empty(cuts);
    }
}
