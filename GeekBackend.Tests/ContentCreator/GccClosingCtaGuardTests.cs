using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every page links the scheduler (Jeff, 2026-09-27: "CTA is on every page and should be
/// referenced"). Every live type carries it, because the scheduler component is on every page.
///
/// <para>
/// Asserted on the finished document, because the prompt already asked and a draft still came back
/// closing on "book a consultation with our team" as plain text. A run with no href is ordinary
/// prose and the renderer draws it correctly as such, so nothing downstream would have noticed.
/// </para>
/// </summary>
public class GccClosingCtaGuardTests
{
    private const string Anchor = "#consultationAppointment2xl";

    private static Run Plain(string text) => new(text);

    private static Section Section(string heading, params Paragraph[] paragraphs) =>
        new("h2", heading, [.. paragraphs], null, []);

    private static ContentDocument Document(params Section[] sections) =>
        new(Section("Lede", new TextParagraph([Plain("Opening.")])), [.. sections]);

    [Fact]
    public void A_closing_that_links_the_scheduler_passes()
    {
        var document = Document(Section(
            "Next steps",
            new TextParagraph([Plain("Start with your worst week of invoices. "),
                new Run("Book a free consultation.", Href: Anchor)])));

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void A_closing_with_the_words_but_no_link_is_refused()
    {
        // Verbatim shape of what shipped: the ask is there, the reader cannot act on it.
        var document = Document(Section(
            "Next steps",
            new TextParagraph([Plain("If you're ready, book a consultation with our team.")])));

        var only = Assert.Single(GccClosingCtaGuard.FindViolations(document, Anchor));
        Assert.Contains("No run in the draft links", only, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_to_somewhere_else_does_not_count()
    {
        var document = Document(Section(
            "Next steps",
            new TextParagraph([new Run("Visit our contact page.", Href: "/contact")])));

        Assert.NotEmpty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void The_link_may_sit_in_a_list_item()
    {
        // Excluding a paragraph kind here would only refuse a draft that put the ask somewhere
        // perfectly reasonable.
        var document = Document(Section(
            "Next steps",
            new ListParagraph(true, [[new Run("Book a free consultation.", Href: Anchor)]])));

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void The_link_may_sit_in_a_nested_subsection()
    {
        var child = new Section("h3", "What happens next", [
            new TextParagraph([new Run("Book a free consultation.", Href: Anchor)])], null, []);
        var document = new ContentDocument(
            Section("Lede", new TextParagraph([Plain("Opening.")])),
            [new Section("h2", "Next steps", [new TextParagraph([Plain("Here is the path.")])], null, [child])]);

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void A_lede_that_links_it_counts_too()
    {
        // The instruction asks for the closing, and a draft that also opens on it is not a violation
        // of this rule -- rejecting that would be enforcing placement, which the prompt owns.
        var document = new ContentDocument(
            Section("Lede", new TextParagraph([new Run("Book a free consultation.", Href: Anchor)])),
            [Section("Next steps", new TextParagraph([Plain("Here is the path.")]))]);

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void A_publisher_with_no_scheduler_configured_has_nothing_to_enforce()
    {
        // The instruction already handles this by asking for no destination at all, so a guard that
        // refused here would refuse every draft for a publisher that has no scheduler.
        var document = Document(Section("Next steps", new TextParagraph([Plain("Call us.")])));

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, null));
        Assert.Empty(GccClosingCtaGuard.FindViolations(document, "   "));
    }

    [Fact]
    public void Case_and_surrounding_space_do_not_defeat_the_match()
    {
        var document = Document(Section(
            "Next steps",
            new TextParagraph([new Run("Book.", Href: " #ConsultationAppointment2xl ")])));

        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void A_missing_document_is_reported_rather_than_passing_quietly()
    {
        Assert.NotEmpty(GccClosingCtaGuard.FindViolations(null, Anchor));
    }
}
