using GeekApplication.Models.ContentCreator;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every block quotation on a page is the source's own words on a page the writer was shown, cited to
/// that page by code, or it is deleted and the page is kept (<see cref="GccQuotations"/>).
///
/// <para>
/// Jeff, 2026-10-22: "delete the invented quotation, do not make me pay for the page and get nothing!",
/// then "and blog posts". Until then an invented or misattributed quotation refused the whole tool page
/// (Jeff, 2026-09-26: "I want a blockquote in each tool"), and any quotation refused a pillar or a blog.
/// A point of law is the second kind of quotation, written out from a passage and found by its words
/// ("such a passage should be contained within a blockquote with cite url").
/// </para>
/// </summary>
public class GccQuotationsTests
{
    private const string PartnerUrl = "https://tipalti.com/customers";
    private const string OtherPartnerUrl = "https://tipalti.com/pricing";
    private const string SiteUrl = "https://geek.test/payroll-tax";
    private const string Said = "We cut approval time from nine days to two, and nobody has looked back.";
    private const string Law = "A remote seller’s sales above $100,000 into the state oblige it to register and collect.";

    /// <summary>The partner's published sentences as typed blocks: the list the writer is shown, numbered.</summary>
    private static IReadOnlyList<GccQuoteCandidate> Listed(params string[] sentences) =>
        GccQuoteCandidates.From(sentences
            .Select(sentence => new GccGroundedPassage(PartnerUrl, "Customers", [new TextParagraph([new Run(sentence)])]))
            .ToList());

    private static GccQuoteablePage Page(string url, params string[] passages) =>
        new(url, "A page", [], passages, RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

    /// <summary>A tool page's evidence: the partner's listed spans, its pages, then the publisher's own.</summary>
    private static GccQuotations.Evidence ToolEvidence(IReadOnlyList<GccQuoteCandidate>? candidates = null) =>
        new(
            candidates ?? Listed(Said),
            [Page(PartnerUrl, "Customers say a lot.", Said), Page(OtherPartnerUrl, "Section: Thresholds\n" + Law), Page(SiteUrl, "Payroll tax is due monthly.")],
            "Tipalti");

    /// <summary>A pillar's or a blog's evidence: no listed spans, the partners' pages, then the publisher's own.</summary>
    private static GccQuotations.Evidence LongFormEvidence() =>
        new(null, [Page(OtherPartnerUrl, "Section: Thresholds\n" + Law), Page(SiteUrl, "Payroll tax is due monthly.")], null);

    private static Section Opening() => new("h2", "Opening", [Prose("Where do the hours go? They go to keying.")], null, []);

    private static Section SectionWith(params Paragraph[] paragraphs) =>
        new("h2", "Key Capabilities", [.. paragraphs], null, []);

    private static QuoteParagraph Quote(string text, string? cite = null) => new([new Run(text)], cite);

    private static TextParagraph Prose(string text) => new([new Run(text)]);

    private static IReadOnlyList<QuoteParagraph> QuotesOf(GccQuotations.Result result)
    {
        static IEnumerable<QuoteParagraph> Of(Section section) =>
            section.Paragraphs.OfType<QuoteParagraph>().Concat(section.Children.SelectMany(Of));

        return [.. Of(result.Lede), .. result.Sections.SelectMany(Of)];
    }

    // ---- chosen by number: the partner's quotation -----------------------------------------------------

    [Fact]
    public void A_quotation_chosen_by_number_is_the_spans_own_words_and_page()
    {
        // The design GccQuoteCandidates documents: the model selects, it never transcribes.
        var result = GccQuotations.Resolve(Opening(), [SectionWith(Prose("Approvals."), new QuoteParagraph([], null, Candidate: 1))], ToolEvidence());

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(Said, Assert.Single(quote.Runs).Text);
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.Empty(result.Deleted);
        var kept = Assert.Single(result.Kept);
        Assert.Equal(1, kept.Span);
        Assert.Equal("Key Capabilities", kept.Heading);
    }

    [Fact]
    public void A_number_wins_over_whatever_was_typed_beside_it()
    {
        var typed = new QuoteParagraph([new Run("We cut approvals to two days.")], "https://elsewhere.test", Candidate: 1);

        var result = GccQuotations.Resolve(Opening(), [SectionWith(typed)], ToolEvidence());

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(Said, Assert.Single(quote.Runs).Text);
        Assert.Equal(PartnerUrl, quote.Cite);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void A_number_that_names_no_listed_span_is_deleted_and_the_page_kept(int id)
    {
        var result = GccQuotations.Resolve(
            Opening(), [SectionWith(Prose("Approvals are the bottleneck."), new QuoteParagraph([], null, Candidate: id))], ToolEvidence());

        Assert.Empty(QuotesOf(result));
        var section = Assert.Single(result.Sections);
        Assert.Equal("Approvals are the bottleneck.", Assert.Single(section.Paragraphs.OfType<TextParagraph>()).Runs[0].Text);
        var deleted = Assert.Single(result.Deleted);
        Assert.Equal(GccQuotations.NamesNoListedSpan(id, 1), deleted.Why);
        Assert.Contains($"names quotable span {id}, but 1 were listed", GccQuotations.Line("tool page", result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_quotation_chosen_by_number_is_deleted_and_named()
    {
        var candidates = Listed(Said, "Approvals route by amount and by vendor, with no spreadsheet in the loop.");
        var sections = new[]
        {
            SectionWith(Prose("First."), new QuoteParagraph([], null, Candidate: 1)),
            SectionWith(Prose("Second."), new QuoteParagraph([], null, Candidate: 2)),
        };

        var result = GccQuotations.Resolve(Opening(), sections, ToolEvidence(candidates));

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(Said, quote.Runs[0].Text);
        var deleted = Assert.Single(result.Deleted);
        Assert.Equal(GccQuotations.ASecondOfThePartner("Tipalti"), deleted.Why);
        Assert.Equal(candidates[1].Text, deleted.Text);
    }

    [Fact]
    public void A_point_of_law_is_not_counted_against_the_partners_one_quotation()
    {
        var sections = new[]
        {
            SectionWith(Prose("First."), new QuoteParagraph([], null, Candidate: 1)),
            SectionWith(Prose("The threshold:"), Quote("sales above $100,000 into the state oblige it to register and collect")),
        };

        var result = GccQuotations.Resolve(Opening(), sections, ToolEvidence());

        Assert.Equal(2, QuotesOf(result).Count);
        Assert.Empty(result.Deleted);
    }

    // ---- written out: a point of law's quotation, found by its words -----------------------------------

    [Fact]
    public void A_quotation_written_out_is_found_on_a_page_and_cited_to_it_in_the_pages_own_characters()
    {
        // The writer types a straight apostrophe where the page has a curly one; the page's characters win.
        var typed = Quote("“a remote seller's sales above $100,000 into the state oblige it to register and collect”");

        var result = GccQuotations.Resolve(Opening(), [SectionWith(Prose("The rule:"), typed)], LongFormEvidence());

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal("remote seller’s sales above $100,000 into the state oblige it to register and collect", Assert.Single(quote.Runs).Text[2..]);
        Assert.Equal(OtherPartnerUrl, quote.Cite);
        var kept = Assert.Single(result.Kept);
        Assert.Null(kept.Span);
        Assert.Empty(result.Deleted);
        Assert.Null(GccQuotations.Line("pillar", result));
    }

    [Fact]
    public void A_cite_the_writer_typed_is_never_used()
    {
        var typed = Quote("sales above $100,000 into the state oblige it to register and collect", "https://irs.gov/made-up");

        var result = GccQuotations.Resolve(Opening(), [SectionWith(typed)], LongFormEvidence());

        Assert.Equal(OtherPartnerUrl, Assert.Single(QuotesOf(result)).Cite);
    }

    [Fact]
    public void Whitespace_quotation_marks_dashes_and_case_do_not_make_a_real_quotation_fail()
    {
        var evidence = new GccQuotations.Evidence(null, [Page(SiteUrl, "Payroll tax — the “employer’s share” — is due\n  monthly.")], null);

        var result = GccQuotations.Resolve(Opening(), [SectionWith(Quote("payroll tax - the \"employer's share\" - is due monthly."))], evidence);

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal("Payroll tax — the “employer’s share” — is due monthly.", quote.Runs[0].Text);
        Assert.Equal(SiteUrl, quote.Cite);
    }

    [Fact]
    public void A_quotation_that_differs_in_a_word_from_every_passage_is_deleted()
    {
        var result = GccQuotations.Resolve(
            Opening(), [SectionWith(Prose("The rule:"), Quote("sales above $500,000 into the state oblige it to register and collect"), Prose("So register."))], LongFormEvidence());

        Assert.Empty(QuotesOf(result));
        var section = Assert.Single(result.Sections);
        Assert.Equal(["The rule:", "So register."], section.Paragraphs.OfType<TextParagraph>().Select(p => p.Runs[0].Text));
        var deleted = Assert.Single(result.Deleted);
        Assert.Equal(GccQuotations.OnNoPage, deleted.Why);
        Assert.Equal(
            "The pillar: 1 block quotation was deleted because its words are on no page the writer was shown: "
            + "\"sales above $500,000 into the state oblige it to register and collect\", under \"Key Capabilities\". The rest of the page is as written.",
            GccQuotations.Line("pillar", result));
    }

    [Fact]
    public void An_invented_quotation_is_deleted_however_plausible_and_whatever_it_cites()
    {
        var result = GccQuotations.Resolve(
            Opening(), [SectionWith(Quote("Tipalti eliminates 80% of manual payables work.", PartnerUrl))], ToolEvidence());

        Assert.Empty(QuotesOf(result));
        Assert.Equal(GccQuotations.OnNoPage, Assert.Single(result.Deleted).Why);
    }

    [Fact]
    public void A_listed_span_typed_out_is_still_the_partners_quotation()
    {
        // Pages first, then the list: Said is on the partner's page too, so it is found there.
        var result = GccQuotations.Resolve(Opening(), [SectionWith(Quote("“We cut approval time from nine days…”"))], ToolEvidence());

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.StartsWith("We cut approval time from nine days", quote.Runs[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_listed_span_that_is_on_no_page_is_matched_from_the_list()
    {
        var evidence = new GccQuotations.Evidence(Listed(Said), [Page(SiteUrl, "Payroll tax is due monthly.")], "Tipalti");

        var result = GccQuotations.Resolve(Opening(), [SectionWith(Quote(Said))], evidence);

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(PartnerUrl, quote.Cite);
        Assert.Equal(1, Assert.Single(result.Kept).Span);
    }

    [Fact]
    public void An_empty_quotation_is_deleted()
    {
        var result = GccQuotations.Resolve(Opening(), [SectionWith(Quote("  "))], LongFormEvidence());

        Assert.Empty(QuotesOf(result));
        Assert.Equal(GccQuotations.IsEmpty, Assert.Single(result.Deleted).Why);
        Assert.Contains("a quotation with no words of its own", GccQuotations.Line("blog", result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_competitors_page_is_not_in_the_evidence_and_cannot_be_quoted()
    {
        // The evidence handed in is the partners' pages and the publisher's own; a competitor's words
        // are on no page here, whatever the writer was shown of them, so they match nothing.
        var result = GccQuotations.Resolve(Opening(), [SectionWith(Quote("A competitor's claim nobody can quote."))], LongFormEvidence());

        Assert.Equal(GccQuotations.OnNoPage, Assert.Single(result.Deleted).Why);
    }

    // ---- what goes with a deleted quotation --------------------------------------------------------------

    [Fact]
    public void A_heading_under_another_left_with_nothing_goes_with_its_quotation_and_is_named()
    {
        var child = new Section("h3", "What the statute says", [Quote("Invented words of a statute.")], null, []);
        var parent = new Section("h2", "The rule", [Prose("Routing first.")], null, [child]);

        var result = GccQuotations.Resolve(Opening(), [parent], LongFormEvidence());

        Assert.Empty(Assert.Single(result.Sections).Children);
        Assert.Equal(["What the statute says"], result.Headings);
        Assert.EndsWith(
            "The heading \"What the statute says\" went with it, having nothing else under it. The rest of the page is as written.",
            GccQuotations.Line("blog", result),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_opening_and_a_pages_own_section_stay_whatever_is_deleted_under_them()
    {
        var opening = new Section("h2", "Opening", [Quote("Invented.")], null, []);
        var own = new Section("h2", "Only a quotation", [Quote("Also invented.")], null, []);

        var result = GccQuotations.Resolve(opening, [own], LongFormEvidence());

        Assert.Equal("Opening", result.Lede.Heading);
        Assert.Empty(result.Lede.Paragraphs);
        Assert.Equal("Only a quotation", Assert.Single(result.Sections).Heading);
        Assert.Empty(result.Headings);
        Assert.Equal(2, result.Deleted.Count);
        Assert.StartsWith("The pillar: 2 block quotations were deleted: ", GccQuotations.Line("pillar", result), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_with_no_quotation_is_the_same_page()
    {
        var sections = new[] { SectionWith(Prose("No quotation here.")) };

        var result = GccQuotations.Resolve(Opening(), sections, LongFormEvidence());

        Assert.Same(sections[0], Assert.Single(result.Sections));
        Assert.Empty(result.Kept);
        Assert.Empty(result.Deleted);
        Assert.Null(GccQuotations.Line("pillar", result));
    }

    [Fact]
    public void A_quotation_nested_in_a_child_section_is_resolved()
    {
        var child = new Section("h3", "How approvals route", [new QuoteParagraph([], null, Candidate: 1)], null, []);
        var parent = new Section("h2", "How It Works", [Prose("Routing first.")], null, [child]);

        var result = GccQuotations.Resolve(Opening(), [parent], ToolEvidence());

        var quote = Assert.Single(QuotesOf(result));
        Assert.Equal(Said, quote.Runs[0].Text);
        Assert.Equal("How approvals route", Assert.Single(result.Kept).Heading);
    }

    // ---- the tool page's partner quotation gap -----------------------------------------------------------

    private static ContentDocument Doc(params Paragraph[] paragraphs) => new(Opening(), [SectionWith(paragraphs)]);

    [Fact]
    public void With_no_candidates_at_all_no_partner_quotation_is_asked_for()
    {
        Assert.Null(GccQuotations.MissingPartnerQuotation(Doc(Prose("Plain.")), null));
    }

    [Fact]
    public void A_page_with_no_quotation_of_the_partner_is_a_reported_gap_that_says_spans_went_unused()
    {
        var missing = GccQuotations.MissingPartnerQuotation(Doc(Prose("Plain body copy, no quotation here.")), Listed(Said));

        Assert.NotNull(missing);
        Assert.Contains("carries no block quotation of the partner", missing, StringComparison.Ordinal);
        Assert.Contains("1 quotable partner span(s) were supplied and none was used", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_quotable_retrieved_the_gap_names_what_was_checked()
    {
        var missing = GccQuotations.MissingPartnerQuotation(Doc(Prose("Plain.")), []);

        Assert.NotNull(missing);
        Assert.Contains("nothing retrieved", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void A_quotation_cited_to_any_page_on_the_partners_host_is_the_partners()
    {
        // By number, or a point of law from one of the partner's own pages: both are the partner's words
        // in a quote box.
        Assert.Null(GccQuotations.MissingPartnerQuotation(Doc(Quote(Said, PartnerUrl)), Listed(Said)));
        Assert.Null(GccQuotations.MissingPartnerQuotation(Doc(Quote(Law, OtherPartnerUrl)), Listed(Said)));
    }

    [Fact]
    public void A_quotation_of_the_publishers_own_page_alone_leaves_the_partners_missing()
    {
        var missing = GccQuotations.MissingPartnerQuotation(Doc(Quote("Payroll tax is due monthly.", SiteUrl)), Listed(Said));

        Assert.NotNull(missing);
        Assert.Contains("carries no block quotation of the partner", missing, StringComparison.Ordinal);
    }
}
