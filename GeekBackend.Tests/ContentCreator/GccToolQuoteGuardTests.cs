using GeekApplication.Models.ContentCreator;
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
    private const string Said = "We cut approval time from nine days to two.";

    private static GccPartnerExtractionProvenance Prov(string? quote) =>
        new(PartnerUrl, "partner", null, null, null, null, null, quote);

    private static GccPartnerExtractionDocument Extraction(
        IReadOnlyList<GccPartnerTestimonialAsset>? testimonials = null,
        IReadOnlyList<GccPartnerCitableAsset>? citables = null) =>
        new("test", citables ?? [], [], [], [], [], [], [], [], [], testimonials ?? [],
            [], [], [], [], [], [], [], [], [], [], [], []);

    private static GccPartnerExtractionDocument WithTestimonial(string quoteText, string url) =>
        Extraction(testimonials: [new GccPartnerTestimonialAsset(quoteText, "A CFO", null, null, url, Prov(quoteText))]);

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
            WithTestimonial(Said, PartnerUrl));

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
            WithTestimonial(Said, PartnerUrl));

        Assert.Empty(violations);
    }

    [Fact]
    public void An_invented_quote_is_refused_however_plausible()
    {
        // The failure mode this exists for: wording nobody published, with a real URL on the cite
        // telling the reader where to go and check it.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("Tipalti eliminates 80% of manual payables work.", PartnerUrl))],
            WithTestimonial(Said, PartnerUrl));

        Assert.Contains(violations, v => v.Contains("not a verbatim span", StringComparison.Ordinal));
    }

    [Fact]
    public void A_paraphrase_of_a_real_quote_is_still_not_that_quote()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("We cut approval time from nine days down to two days.", PartnerUrl))],
            WithTestimonial(Said, PartnerUrl));

        Assert.Contains(violations, v => v.Contains("not a verbatim span", StringComparison.Ordinal));
    }

    [Fact]
    public void A_real_quote_cited_to_the_wrong_page_is_refused()
    {
        // Not a near miss: it attributes the words to a page that does not carry them.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote(Said, OtherUrl))],
            WithTestimonial(Said, PartnerUrl));

        var only = Assert.Single(violations);
        Assert.Contains("cites https://tipalti.com/pricing", only, StringComparison.Ordinal);
        Assert.Contains("extracted from https://tipalti.com/customers", only, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_quote_with_no_cite_is_refused()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote(Said, null))],
            WithTestimonial(Said, PartnerUrl));

        Assert.Contains(violations, v => v.Contains("carries no cite", StringComparison.Ordinal));
    }

    [Fact]
    public void A_citables_verify_span_is_quotable_too()
    {
        const string claim = "Invoices post to the ledger without rekeying.";
        var extraction = Extraction(citables: [new GccPartnerCitableAsset(claim, PartnerUrl, Prov(claim))]);

        Assert.Empty(GccToolQuoteGuard.FindViolations([SectionWith(Quote(claim, PartnerUrl))], extraction));
    }

    [Fact]
    public void A_quote_nested_in_a_child_section_is_found()
    {
        // Tool sections carry h3/h4 children; a quote that only counts at the top level would let
        // the requirement be satisfied or dodged depending on where the writer put it.
        var child = new Section("h3", "How approvals route", [Quote(Said, PartnerUrl)], null, []);
        var parent = new Section("h2", "How It Works", [Prose("Routing first.")], null, [child]);

        Assert.Empty(GccToolQuoteGuard.FindViolations([parent], WithTestimonial(Said, PartnerUrl)));
    }

    [Fact]
    public void With_no_evidence_at_all_the_message_says_so_rather_than_blaming_the_writer()
    {
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Prose("Tipalti automates payables."))],
            null);

        var only = Assert.Single(violations);
        Assert.Contains("holds no quotable span", only, StringComparison.Ordinal);
    }

    [Fact]
    public void Whitespace_differences_do_not_make_a_real_quote_fail()
    {
        // Line wrapping in the model's JSON is not a change of wording.
        var violations = GccToolQuoteGuard.FindViolations(
            [SectionWith(Quote("We cut approval time\n  from nine days to two.", PartnerUrl))],
            WithTestimonial(Said, PartnerUrl));

        Assert.Empty(violations);
    }
}
