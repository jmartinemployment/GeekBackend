using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Each check in <see cref="GccDraftGuard"/>, on a document built to fail exactly that check.
/// </summary>
public class GccDraftGuardTests
{
    private const string Scheduler = "#consultationAppointment2xl";
    private const string PartnerPage = "https://melio.test/pay-bills";

    private static readonly GccHeadingProvenanceEvidence NoEvidence = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());

    private static GccGuardInputs Inputs(
        string numberEvidence = "",
        IReadOnlyList<string>? requiredTools = null,
        IReadOnlyList<GccQuoteCandidate>? candidates = null,
        int appended = 0) =>
        new(
            NoEvidence,
            requiredTools ?? [],
            Scheduler,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PartnerPage },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "geek.test" },
            numberEvidence,
            candidates,
            appended);

    private static Section Body(string heading, params Paragraph[] paragraphs) =>
        new("h2", heading, paragraphs, null, [], Provenance: "plan");

    private static TextParagraph Text(string text, string? href = null) => new([new Run(text, Href: href)]);

    private static readonly Section Closing = Body("What to do next", Text("Book a free consultation.", Scheduler));

    private static ContentDocument Doc(params Section[] sections) =>
        new(new Section("h2", "Opening", [Text("An opening.")], null, []), [.. sections, Closing]);

    private static IReadOnlySet<string> Failed(GccGuardVerdict verdict) => verdict.FailedChecks;

    [Fact]
    public void A_clean_pillar_passes_every_check()
    {
        var verdict = GccDraftGuard.Pillar(Doc(Body("Where the hours go", Text("Invoices are keyed twice."))), Inputs());

        Assert.True(verdict.Clean, string.Join(" ", verdict.Findings.Select(f => f.Detail)));
    }

    [Fact]
    public void A_quotation_on_a_pillar_or_blog_is_refused()
    {
        var doc = Doc(Body("Proof", new QuoteParagraph([new Run("We love it.")], PartnerPage)));

        Assert.Contains("no-quotation", Failed(GccDraftGuard.Pillar(doc, Inputs())));
        Assert.Contains("no-quotation", Failed(GccDraftGuard.Blog(doc, Inputs())));
        Assert.All(GccDraftGuard.Blog(doc, Inputs()).Findings.Where(f => f.Check == "no-quotation"), f => Assert.True(f.Refuses));
    }

    [Theory]
    [InlineData("https://competitor.test/pricing")]
    [InlineData("https://melio.test/a-page-nobody-retrieved")]
    [InlineData("/relative/path")]
    public void A_link_outside_the_evidence_is_refused(string href)
    {
        var doc = Doc(Body("Choosing", Text("See the comparison.", href)));

        var verdict = GccDraftGuard.Pillar(doc, Inputs());

        var finding = Assert.Single(verdict.Findings, f => f.Check == "links");
        Assert.True(finding.Refuses);
        Assert.Contains(href, finding.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PartnerPage)]
    [InlineData("https://geek.test/tools/accounting/melio")]
    [InlineData("https://www.geek.test/blog/ap")]
    [InlineData(Scheduler)]
    public void A_link_to_the_evidence_the_publisher_or_the_scheduler_is_allowed(string href)
    {
        var doc = Doc(Body("Choosing", Text("See the source.", href)));

        Assert.DoesNotContain("links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    [Fact]
    public void A_section_href_is_checked_as_well_as_a_run_href()
    {
        var doc = Doc(new Section("h2", "Choosing", [Text("Prose.")], "https://competitor.test", [], Provenance: "plan"));

        Assert.Contains("links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    [Fact]
    public void A_figure_the_evidence_does_not_contain_is_refused_naming_its_sentence()
    {
        var doc = Doc(Body("Outcomes", Text("Teams close the month 73% faster. Approvals move.")));

        var verdict = GccDraftGuard.Pillar(doc, Inputs(numberEvidence: "Approvals fell from nine days to two."));

        var finding = Assert.Single(verdict.Findings, f => f.Check == "numbers");
        Assert.True(finding.Refuses);
        Assert.Contains("Teams close the month 73% faster.", finding.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("It handles 3,000 invoices a month.", "up to 3000 invoices")]
    [InlineData("It costs $19.00 a month.", "starts at $19 per month")]
    [InlineData("Approvals fell to 2 days.", "fell from 9 days to 2")]
    public void A_figure_the_evidence_contains_passes_whatever_its_separators(string draft, string evidence)
    {
        var doc = Doc(Body("Outcomes", Text(draft)));

        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Pillar(doc, Inputs(numberEvidence: evidence))));
    }

    [Fact]
    public void A_number_in_a_heading_is_a_list_title_not_a_claim()
    {
        var doc = Doc(Body("7 signs your process is broken", Text("Prose.")));

        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    [Fact]
    public void A_missing_partner_is_a_gap_not_a_refusal()
    {
        var doc = Doc(Body("Where the hours go", Text("Melio pays the bill.")));

        var verdict = GccDraftGuard.Pillar(doc, Inputs(requiredTools: ["Melio", "Ramp"]));

        var finding = Assert.Single(verdict.Findings);
        Assert.Equal("partner-mentions", finding.Check);
        Assert.False(finding.Refuses);
        Assert.Contains("Ramp", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unlinked_closing_is_a_gap_not_a_refusal()
    {
        var doc = new ContentDocument(
            new Section("h2", "Opening", [Text("An opening.")], null, []),
            [Body("What to do next", Text("Book a free consultation."))]);

        var finding = Assert.Single(GccDraftGuard.Pillar(doc, Inputs()).Findings);
        Assert.Equal("closing-link", finding.Check);
        Assert.False(finding.Refuses);
    }

    [Fact]
    public void An_appended_faq_is_exempt_from_heading_provenance_and_nothing_else()
    {
        // The People Also Ask section's headings are the questions; they carry no provenance tag.
        var faq = new Section(
            "h2", "People Also Ask", [],
            null,
            [new Section("h3", "Is it secure?", [Text("See https://competitor.test.", "https://competitor.test")], null, [])]);
        var doc = new ContentDocument(
            new Section("h2", "Opening", [Text("An opening.")], null, []),
            [Closing, faq]);

        var verdict = GccDraftGuard.Pillar(doc, Inputs(appended: 1));

        Assert.DoesNotContain("heading-provenance", Failed(verdict));
        Assert.Contains("links", Failed(verdict));
    }

    [Fact]
    public void A_tool_page_with_two_quotations_is_refused()
    {
        var candidates = new[] { new GccQuoteCandidate(1, "Melio pays bills from your bank.", PartnerPage, "Pay bills") };
        var quote = new QuoteParagraph([new Run("Melio pays bills from your bank.")], PartnerPage, Candidate: 1);
        var doc = Doc(Body("Melio at work", Text("Melio moves the money."), quote, quote));

        var verdict = GccDraftGuard.Tool(doc, Inputs(requiredTools: ["Melio"], candidates: candidates));

        Assert.Equal(["one-quotation"], Failed(verdict));
    }

    [Fact]
    public void A_tool_page_that_never_names_its_product_is_refused()
    {
        var doc = Doc(Body("Paying bills", Text("Bills get paid.")));

        var verdict = GccDraftGuard.Tool(doc, Inputs(requiredTools: ["Melio"]));

        var finding = Assert.Single(verdict.Findings, f => f.Check == "names-product");
        Assert.True(finding.Refuses);
    }

    [Fact]
    public void A_tool_page_with_no_candidates_is_the_legacy_path_and_needs_no_quotation()
    {
        var doc = Doc(Body("Melio at work", Text("Melio moves the money.")));

        Assert.True(GccDraftGuard.Tool(doc, Inputs(requiredTools: ["Melio"], candidates: null)).Clean);
        Assert.Contains("quotation", Failed(GccDraftGuard.Tool(doc, Inputs(requiredTools: ["Melio"], candidates: []))));
    }

    // ---- RetryReplaces ------------------------------------------------------------------------

    private static GccGuardVerdict Verdict(params (string Check, bool Refuses)[] findings) =>
        new([.. findings.Select(f => new GccGuardFinding(f.Check, f.Check, f.Refuses, f.Check))]);

    [Fact]
    public void A_retry_that_fails_a_check_the_draft_passed_is_not_taken()
    {
        // The shape the old per-guard retries let through: fixed the link, lost a partner.
        var draft = Verdict(("closing-link", false));
        var retry = Verdict(("partner-mentions", false));

        Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
    }

    [Fact]
    public void A_retry_with_a_new_refusal_is_not_taken_even_when_it_fixes_a_gap()
    {
        var draft = Verdict(("closing-link", false));
        var retry = Verdict(("links", true));

        Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
    }

    [Fact]
    public void A_retry_that_trades_a_refusal_for_a_new_failure_is_not_taken()
    {
        // Fewer refusals, but it fails a check the draft passed: a different fault, not a fix.
        var draft = Verdict(("numbers", true), ("closing-link", false));
        var retry = Verdict(("partner-mentions", false));

        Assert.False(GccGuardVerdict.RetryReplaces(draft, retry));
    }

    [Fact]
    public void A_retry_that_fixes_a_refusal_and_breaks_nothing_is_taken()
    {
        var draft = Verdict(("numbers", true), ("closing-link", false));
        var retry = Verdict(("closing-link", false));

        Assert.True(GccGuardVerdict.RetryReplaces(draft, retry));
    }

    [Fact]
    public void A_retry_that_fails_a_strict_subset_is_taken_and_an_equal_one_is_not()
    {
        var draft = Verdict(("closing-link", false), ("partner-mentions", false));

        Assert.True(GccGuardVerdict.RetryReplaces(draft, Verdict(("closing-link", false))));
        Assert.False(GccGuardVerdict.RetryReplaces(draft, Verdict(("closing-link", false), ("partner-mentions", false))));
    }
}
