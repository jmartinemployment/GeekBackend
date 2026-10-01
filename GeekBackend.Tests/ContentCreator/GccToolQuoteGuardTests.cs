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
}
