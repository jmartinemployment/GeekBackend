using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The line every page ends on is built by code (Jeff, 2026-10-07): "Answer these questions when booking
/// your free consultation." with the booking words linked to the scheduler, then the questions he entered
/// under "One question per line". The writer does not write it, so it cannot be wrong in the three ways the
/// Bill tool page's was: the brief's internal "book_now" printed as the ask, the questions written three
/// times (once as a self-quiz the quiz check refused), and the link's visible words being the anchor.
/// </summary>
public sealed class GccClosingLineTests
{
    private const string Anchor = "#consultationAppointment2xl";

    private static CompanyProfileOptions Company(string? anchor = Anchor) => new() { ConsultationAnchorHref = anchor! };

    private static readonly string[] Questions =
    [
        "What business objective should this automation serve?",
        "How clean is the data the approvals draw on today?",
    ];

    // ---- the paragraphs -------------------------------------------------------------------------

    [Fact]
    public void With_questions_the_line_is_the_fixed_lead_the_linked_words_and_a_full_stop_then_the_questions_in_order()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);

        Assert.Equal(2, closing.Count);
        var line = Assert.IsType<TextParagraph>(closing[0]);
        Assert.Equal(
            [new Run("Answer these questions when "), new Run("booking your free consultation", Href: Anchor), new Run(".")],
            line.Runs);
        var list = Assert.IsType<ListParagraph>(closing[1]);
        Assert.False(list.Ordered);
        Assert.Equal(Questions, list.Items.Select(item => Assert.Single(item).Text));
    }

    [Fact]
    public void The_linked_words_are_words_never_the_anchor_itself()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);

        var link = Assert.Single(((TextParagraph)closing[0]).Runs, run => run.Href is not null);
        Assert.Equal(Anchor, link.Href);
        Assert.DoesNotContain("#", link.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("consultationAppointment2xl", link.Text, StringComparison.Ordinal);
        Assert.Equal(4, link.Text.Split(' ').Length);
        Assert.True(link.Text.Split(' ').Length <= GccDraftGuard.MaxLinkWords);
    }

    [Fact]
    public void With_no_questions_it_is_the_plain_booking_line_linked_the_same_way()
    {
        foreach (var none in new IReadOnlyList<string>[] { [], ["", "   "] })
        {
            var closing = GccClosing.Paragraphs(Company(), none);

            var line = Assert.IsType<TextParagraph>(Assert.Single(closing));
            Assert.Equal([new Run("Book your free consultation", Href: Anchor), new Run(".")], line.Runs);
        }
    }

    [Fact]
    public void A_question_is_kept_as_the_operator_wrote_it_apart_from_surrounding_space()
    {
        var closing = GccClosing.Paragraphs(Company(), ["  Is there an audit trail sufficient to answer \"who approved this payment and why?\"  "]);

        var list = Assert.IsType<ListParagraph>(closing[1]);
        Assert.Equal(
            "Is there an audit trail sufficient to answer \"who approved this payment and why?\"",
            Assert.Single(Assert.Single(list.Items)).Text);
    }

    [Fact]
    public void The_wording_comes_from_the_companys_settings()
    {
        var company = Company();
        company.ConsultationClosingLead = "Bring your answers to these when";
        company.ConsultationClosingLink = "you book a call";

        var closing = GccClosing.Paragraphs(company, Questions);

        var line = Assert.IsType<TextParagraph>(closing[0]);
        Assert.Equal(
            [new Run("Bring your answers to these when "), new Run("you book a call", Href: Anchor), new Run(".")],
            line.Runs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_publisher_with_no_scheduler_has_no_closing_because_there_is_nowhere_for_it_to_go(string? anchor)
    {
        Assert.Empty(GccClosing.Paragraphs(Company(anchor), Questions));
        Assert.Empty(GccClosing.Paragraphs(Company(anchor), []));
    }

    [Fact]
    public void A_blank_wording_builds_nothing_rather_than_a_line_that_reads_wrongly()
    {
        var noLead = Company();
        noLead.ConsultationClosingLead = " ";
        var noLink = Company();
        noLink.ConsultationClosingLink = "";
        var noPlain = Company();
        noPlain.ConsultationClosingLinkWithoutQuestions = "";

        Assert.Empty(GccClosing.Paragraphs(noLead, Questions));
        Assert.Empty(GccClosing.Paragraphs(noLink, Questions));
        Assert.Empty(GccClosing.Paragraphs(noPlain, []));
        // The blank wording that is not in use does not stop the other form.
        Assert.NotEmpty(GccClosing.Paragraphs(noPlain, Questions));
    }

    // ---- what the guards make of it -------------------------------------------------------------

    [Fact]
    public void The_built_closing_contains_none_of_the_quiz_checks_phrases_and_satisfies_the_closing_link_check()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);
        var document = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            GccClosing.AppendTo([Body("Where the hours go", "Invoices are keyed twice.")], closing));

        var verdict = GccDraftGuard.Pillar(document, Inputs());

        Assert.True(verdict.Clean, string.Join(" ", verdict.Findings.Select(f => f.Check + ": " + f.Detail)));
        Assert.Empty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    [Fact]
    public void A_question_the_operator_wrote_is_not_a_quiz_the_writer_framed_but_the_same_words_in_the_body_still_are()
    {
        // "If your answers ..." is one of the quiz check's phrasings. In the operator's own question it is
        // the operator's wording on the page's own closing; in the writer's body it is the failure the check
        // exists for.
        string[] questions = ["If your answers to these are all no, who approves spending today?"];
        var closing = GccClosing.Paragraphs(Company(), questions);

        var operators = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            GccClosing.AppendTo([Body("Where the hours go", "Invoices are keyed twice.")], closing));
        var writers = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            GccClosing.AppendTo([Body("Where the hours go", "If your answers are mostly no, it may be time to book.")], closing));

        Assert.DoesNotContain("questions-quiz", GccDraftGuard.Pillar(operators, Inputs()).FailedChecks);
        var finding = Assert.Single(GccDraftGuard.Pillar(writers, Inputs()).Findings, f => f.Check == "questions-quiz");
        Assert.True(finding.Refuses);
    }

    [Fact]
    public void Without_the_closing_the_same_document_has_no_link_to_the_scheduler()
    {
        var document = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [Body("Where the hours go", "Invoices are keyed twice.")]);

        Assert.NotEmpty(GccClosingCtaGuard.FindViolations(document, Anchor));
    }

    // ---- where it goes --------------------------------------------------------------------------

    [Fact]
    public void It_goes_at_the_end_of_the_last_section_and_leaves_the_others_alone()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);
        var first = Body("First", "One.");
        var last = Body("Last", "Two.");

        var result = GccClosing.AppendTo([first, last], closing);

        Assert.Equal(first, result[0]);
        Assert.Equal(1 + closing.Count, result[1].Paragraphs.Count);
        Assert.Equal(closing, result[1].Paragraphs.Skip(1));
    }

    [Fact]
    public void It_goes_in_the_deepest_last_subsection_because_a_section_renders_its_paragraphs_before_its_children()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);
        var inner = Sub("h4", "Inner", "Deepest.");
        var middle = Sub("h3", "Middle", "Middle text.") with { Children = [inner] };
        var top = Body("Top", "Top text.") with { Children = [Sub("h3", "Earlier", "Earlier."), middle] };

        var result = GccClosing.AppendTo([top], closing);

        var rendered = top.Children.Count;
        Assert.Equal(rendered, result[0].Children.Count);
        Assert.Single(result[0].Paragraphs);
        Assert.Single(result[0].Children[0].Paragraphs);
        Assert.Single(result[0].Children[1].Paragraphs);
        var deepest = result[0].Children[1].Children[0];
        Assert.Equal(1 + closing.Count, deepest.Paragraphs.Count);
        Assert.Equal(closing, deepest.Paragraphs.Skip(1));
    }

    [Fact]
    public void Nothing_to_append_to_or_no_closing_changes_nothing()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);

        Assert.Empty(GccClosing.AppendTo([], closing));
        var sections = new[] { Body("Only", "Text.") };
        Assert.Equal(sections, GccClosing.AppendTo(sections, []));
    }

    // ---- a revision carries it through unchanged ------------------------------------------------

    [Fact]
    public void A_stored_closing_is_taken_off_before_a_revision_and_put_back_where_it_was()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);
        var faq = Body("People Also Ask", "Is it secure? Yes.");
        var stored = GccClosing.AppendTo([Body("One", "A."), Body("Two", "B.")], closing);
        stored.Add(faq);

        var (without, at, taken) = GccClosing.Detach(stored, Anchor);

        Assert.Equal(1, at);
        Assert.Equal(closing, taken);
        Assert.Equal(["A.", "B."], without.Take(2).Select(s => ((TextParagraph)s.Paragraphs[0]).Runs[0].Text));
        Assert.All(without.Take(2), s => Assert.Single(s.Paragraphs));
        Assert.Equal(faq, without[2]);

        // The writer returns the same number of sections, revised.
        var revised = new List<Section> { Body("One", "A, revised."), Body("Two", "B, revised."), faq };
        var restored = GccClosing.Reattach(revised, at, taken);

        Assert.Single(restored[0].Paragraphs);
        Assert.Equal(1 + closing.Count, restored[1].Paragraphs.Count);
        Assert.Equal(closing, restored[1].Paragraphs.Skip(1));
        Assert.Equal(faq, restored[2]);
    }

    [Fact]
    public void A_closing_with_no_questions_is_taken_off_and_put_back_too()
    {
        var closing = GccClosing.Paragraphs(Company(), []);
        var stored = GccClosing.AppendTo([Body("Only", "Text.")], closing);

        var (without, at, taken) = GccClosing.Detach(stored, Anchor);

        Assert.Equal(0, at);
        Assert.Equal(closing, taken);
        Assert.Single(without[0].Paragraphs);
        Assert.Equal(stored[0].Paragraphs, GccClosing.Reattach(without, at, taken)[0].Paragraphs);
    }

    [Fact]
    public void A_body_with_no_closing_is_left_as_it_is_so_a_page_written_before_this_is_still_revisable()
    {
        var stored = new List<Section> { Body("One", "A."), Body("Two", "B.") };

        var (without, at, taken) = GccClosing.Detach(stored, Anchor);

        Assert.Null(at);
        Assert.Empty(taken);
        Assert.Equal(stored, without);
        Assert.Equal(stored, GccClosing.Reattach(without, at, taken));
    }

    [Fact]
    public void A_revision_that_returns_fewer_sections_puts_the_closing_on_the_last_one()
    {
        var closing = GccClosing.Paragraphs(Company(), Questions);

        var restored = GccClosing.Reattach([Body("Only", "Text.")], at: 4, closing);

        Assert.Equal(1 + closing.Count, restored[0].Paragraphs.Count);
    }

    [Fact]
    public void Detach_with_no_scheduler_configured_takes_nothing()
    {
        var stored = GccClosing.AppendTo([Body("Only", "Text.")], GccClosing.Paragraphs(Company(), Questions));

        var (without, at, taken) = GccClosing.Detach(stored, null);

        Assert.Null(at);
        Assert.Empty(taken);
        Assert.Equal(stored, without);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static Section Body(string heading, string text) =>
        new("h2", heading, [new TextParagraph([new Run(text)])], null, [], Provenance: "plan");

    private static Section Sub(string tag, string heading, string text) =>
        new(tag, heading, [new TextParagraph([new Run(text)])], null, []);

    private static readonly GccHeadingProvenanceEvidence NoEvidence = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

    private static GccGuardInputs Inputs() => new(
        NoEvidence,
        [],
        Anchor,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "geek.test" },
        string.Empty);
}
