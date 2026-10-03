using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every tool page carries a block quotation and it is the partner's own published words
/// (Jeff, 2026-09-26: "I want a blockquote in each tool").
///
/// <para>
/// The prompt asking for one is not having one -- these assert the code that rejects the draft,
/// because an invented quote parses, renders and ships otherwise: ContentGuardrail passes quotes
/// through deliberately and SectionHtmlRenderer writes the cite straight onto the tag.
/// </para>
/// </summary>
public class GccToolQuoteGuardTests
{
    private const string PartnerUrl = "https://tipalti.com/customers";
    private const string OtherUrl = "https://tipalti.com/pricing";
    private const string Said = "We cut approval time from nine days to two, and nobody has looked back.";

    /// <summary>
    /// The partner's published sentences as typed blocks -- the guard's only source, and the same
    /// source the writer is shown.
    /// </summary>
    private static IReadOnlyList<GccQuoteCandidate> Published(string sentence, string url) =>
        GccQuoteCandidates.From([
            new GccGroundedPassage(url, "Customers", [new TextParagraph([new Run(sentence)])]),
        ]);

    private static IReadOnlyList<GccQuoteCandidate> Published() => Published(Said, PartnerUrl);

    private static Section SectionWith(params Paragraph[] paragraphs) =>
        new("h2", "Key Capabilities", [.. paragraphs], null, []);

    private static QuoteParagraph Quote(string text, string? cite) =>
        new([new Run(text)], cite);

    private static TextParagraph Prose(string text) => new([new Run(text)]);

    [Fact]
    public void A_page_with_no_block_quotation_is_refused()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Prose("Tipalti automates payables."))],
            Published());

        var only = Assert.Single(violations);
        Assert.Contains("carries no block quotation", only, StringComparison.Ordinal);
        // The message says a quote was available and unused, because that is the actionable half.
        Assert.Contains("quotable partner span(s) were supplied", only, StringComparison.Ordinal);
    }

    [Fact]
    public void A_verbatim_testimonial_cited_to_its_own_page_passes()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Prose("Approvals are the bottleneck."), Quote(Said, PartnerUrl))],
            Published());

        Assert.Empty(violations);
    }

    [Fact]
    public void An_invented_quote_is_refused_however_plausible()
    {
        // The failure mode this exists for: wording nobody published, with a real URL on the cite
        // telling the reader where to go and check it.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("Tipalti eliminates 80% of manual payables work.", PartnerUrl))],
            Published());

        Assert.Contains(violations, v => v.Contains("not a verbatim span", StringComparison.Ordinal));
    }

    [Fact]
    public void A_paraphrase_of_a_real_quote_is_still_not_that_quote()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("We cut approval time from nine days down to two days.", PartnerUrl))],
            Published());

        Assert.Contains(violations, v => v.Contains("not a verbatim span", StringComparison.Ordinal));
    }

    [Fact]
    public void A_real_quote_cited_to_the_wrong_page_is_refused()
    {
        // Not a near miss: it attributes the words to a page that does not carry them.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote(Said, OtherUrl))],
            Published());

        var only = Assert.Single(violations);
        Assert.Contains("cites https://tipalti.com/pricing", only, StringComparison.Ordinal);
        Assert.Contains("extracted from https://tipalti.com/customers", only, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_quote_with_no_cite_is_refused()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote(Said, null))],
            Published());

        Assert.Contains(violations, v => v.Contains("carries no cite", StringComparison.Ordinal));
    }


    [Fact]
    public void A_quote_nested_in_a_child_section_is_found()
    {
        // Tool sections carry h3/h4 children; a quote that only counts at the top level would let
        // the requirement be satisfied or dodged depending on where the writer put it.
        var child = new Section("h3", "How approvals route", [Quote(Said, PartnerUrl)], null, []);
        var parent = new Section("h2", "How It Works", [Prose("Routing first.")], null, [child]);

        Assert.Empty(GccToolQuoteGuard.FindViolations([parent], Published()));
    }

    [Fact]
    public void With_no_evidence_at_all_the_message_says_so_rather_than_blaming_the_writer()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Prose("Tipalti automates payables."))],
            null);

        var only = Assert.Single(violations);
        // Was "holds no quotable span", a claim about the partner's whole evidence made after
        // inspecting two of the extraction's twenty-two categories. It now names what it checked.
        Assert.Contains("nothing retrieved", only, StringComparison.Ordinal);
        
    }

    [Fact]
    public void Whitespace_differences_do_not_make_a_real_quote_fail()
    {
        // Line wrapping in the model's JSON is not a change of wording.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("We cut approval time\n  from nine days to two, and nobody has looked back.", PartnerUrl))],
            Published());

        Assert.Empty(violations);
    }

    [Fact]
    public void A_span_from_the_retrieved_pages_is_quotable_without_any_extraction()
    {
        // The live refusal. tipalti.com carries 5,133 indexed chunks; its extraction produced
        // features and pricing but no testimonial and no citable, and the page was refused as
        // though the partner had published nothing quotable at all.
        var candidates = Published();

        var sections = new[]
        {
            SectionWith(Quote(
                "We cut approval time from nine days to two, and nobody has looked back.",
                PartnerUrl)),
        };

        Assert.Empty(GccToolQuoteGuard.FindViolations(sections, candidates));
    }

    [Fact]
    public void A_quotation_chosen_by_number_is_resolved_to_that_candidates_words_and_page()
    {
        // The design GccQuoteCandidates documents: the model selects, it never transcribes. A
        // retyped sentence lost Stampli's page on 2026-10-03; a number cannot be paraphrased.
        var chosen = new QuoteParagraph([], null, Candidate: 1);

        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates([SectionWith(Prose("Approvals."), chosen)], Published());

        var quote = Assert.Single(snapped.Single().Paragraphs.OfType<QuoteParagraph>());
        Assert.Equal(Said, Assert.Single(quote.Runs).Text);
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.Null(quote.Candidate);
        Assert.Empty(GccToolQuoteGuard.FindViolations(snapped, Published()));
    }

    [Fact]
    public void A_number_wins_over_whatever_was_typed_beside_it()
    {
        // Typed text in a numbered quotation is discarded, so a model that both selects and
        // "helpfully" paraphrases still ships the published words.
        var chosen = new QuoteParagraph([new Run("We cut approvals to two days.")], "https://elsewhere.test", Candidate: 1);

        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates([SectionWith(chosen)], Published());

        var quote = Assert.Single(snapped.Single().Paragraphs.OfType<QuoteParagraph>());
        Assert.Equal(Said, Assert.Single(quote.Runs).Text);
        Assert.Equal(PartnerUrl, quote.Cite);
    }

    [Fact]
    public void A_number_that_names_no_listed_span_is_refused()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(new QuoteParagraph([], null, Candidate: 7))],
            Published());

        var only = Assert.Single(violations);
        Assert.Contains("names quotable span 7, but 1 were listed", only, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unresolved_number_is_still_refused_after_snapping()
    {
        // Snap leaves an unknown number alone, and the guard then refuses it rather than reading
        // the empty runs as an empty quotation.
        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates(
            [SectionWith(new QuoteParagraph([], null, Candidate: 0))], Published());

        Assert.Contains(
            GccToolQuoteGuard.FindViolations(snapped, Published()),
            v => v.Contains("names quotable span 0", StringComparison.Ordinal));
    }

    [Fact]
    public void With_candidates_available_the_refusal_says_they_went_unused()
    {
        var candidates = Published();

        var violation = Assert.Single(
            GccToolQuoteGuard.FindViolations([SectionWith(Prose("Plain body copy, no quotation here."))], candidates));

        Assert.Contains("none was used", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_anywhere_the_refusal_names_what_was_checked()
    {
        // It used to assert "the partner evidence holds no quotable span", having inspected two of
        // the extraction's twenty-two categories and nothing else.
        var violation = Assert.Single(
            GccToolQuoteGuard.FindViolations([SectionWith(Prose("Plain body copy, no quotation here."))], []));

        Assert.Contains("nothing retrieved", violation, StringComparison.Ordinal);
        
    }

    [Fact]
    public void A_retrieved_span_cited_to_the_wrong_page_is_still_refused()
    {
        // Widening where candidates come from does not weaken what makes one valid.
        var candidates = Published();

        var sections = new[]
        {
            SectionWith(Quote(
                "We cut approval time from nine days to two, and nobody has looked back.",
                OtherUrl)),
        };

        Assert.NotEmpty(GccToolQuoteGuard.FindViolations(sections, candidates));
    }

    [Fact]
    public void A_shortened_span_with_an_ellipsis_is_snapped_back_not_refused()
    {
        // The live failure: the model copied a real span, shortened it, and added an ellipsis --
        // ""We offer an end-to-end accounts payable automation solution…" -- so it was no longer a
        // verbatim substring of anything and the page was refused. The text was never the model's to
        // type; snapping restores the candidate's own string.
        var candidates = Published();
        var sections = new[]
        {
            SectionWith(Quote("\u201CWe cut approval time from nine days\u2026\u201D", PartnerUrl)),
        };

        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates(sections, candidates);

        var quote = Assert.IsType<QuoteParagraph>(Assert.Single(snapped[0].Paragraphs));
        Assert.Equal(Said, Assert.Single(quote.Runs).Text);
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.Empty(GccToolQuoteGuard.FindViolations(snapped, candidates));
    }

    [Fact]
    public void Snapping_takes_the_cite_from_the_candidate_so_a_wrong_url_cannot_survive()
    {
        var candidates = Published();
        var sections = new[] { SectionWith(Quote(Said, OtherUrl)) };

        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates(sections, candidates);

        var quote = Assert.IsType<QuoteParagraph>(Assert.Single(snapped[0].Paragraphs));
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.Empty(GccToolQuoteGuard.FindViolations(snapped, candidates));
    }

    [Fact]
    public void A_quote_that_differs_in_the_middle_is_not_snapped_and_is_still_refused()
    {
        // Only the edges are set aside. Invented wording inside matches no candidate, so it is left
        // alone and the guard refuses it -- snapping is the design being enforced, not a repair.
        var candidates = Published();
        var sections = new[]
        {
            SectionWith(Quote("We cut approval time from ninety days to two, and nobody looked back.", PartnerUrl)),
        };

        var snapped = GccToolQuoteGuard.SnapQuotesToCandidates(sections, candidates);

        Assert.NotEmpty(GccToolQuoteGuard.FindViolations(snapped, candidates));
    }

    [Fact]
    public void With_no_candidates_nothing_is_snapped()
    {
        var sections = new[] { SectionWith(Quote(Said, PartnerUrl)) };

        Assert.Same(sections, GccToolQuoteGuard.SnapQuotesToCandidates(sections, []));
    }
}
