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

    /// <summary>The one partner tool page the writer was handed, at the path it is published under.</summary>
    private const string RampToolPage = "/tools/accounting/accounts-payable/ramp";

    private static GccGuardInputs Inputs(
        string numberEvidence = "",
        IReadOnlyList<string>? requiredTools = null,
        IReadOnlyList<GccQuoteCandidate>? candidates = null,
        int appended = 0,
        IReadOnlyList<string>? unlistedTools = null) =>
        new(
            NoEvidence,
            requiredTools ?? [],
            Scheduler,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { PartnerPage },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "geek.test" },
            numberEvidence,
            candidates,
            appended,
            ToolBasePath: "/tools",
            ToolPaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RampToolPage },
            UnlistedTools: unlistedTools);

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

    /// <summary>One paragraph of the Stampli tool page of 2026-10-05, which came back as a single link.</summary>
    private const string LinkedParagraph =
        "A critical aspect of implementing Stampli is configuring the approval chains and routing logic "
        + "that align with the company's specific needs. This involves setting up predefined business "
        + "rules and spending authority, which guide the approval process.";

    [Fact]
    public void A_link_on_a_whole_paragraph_is_refused_on_every_type()
    {
        var doc = Doc(Body("How it routes", Text(LinkedParagraph, PartnerPage)));

        foreach (var verdict in new[]
                 {
                     GccDraftGuard.Pillar(doc, Inputs()), GccDraftGuard.Blog(doc, Inputs()), GccDraftGuard.Tool(doc, Inputs()),
                 })
        {
            var finding = Assert.Single(verdict.Findings, f => f.Check == "link-text");
            Assert.True(finding.Refuses);
            // Named by its opening words and where it leads, so the writer can find the run.
            Assert.Contains("A critical aspect of implementing Stampli is configuring", finding.Detail, StringComparison.Ordinal);
            Assert.Contains(PartnerPage, finding.RetryInstruction, StringComparison.Ordinal);
            Assert.Contains("a short run that names the source", finding.RetryInstruction, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_link_on_the_name_of_its_source_passes()
    {
        var doc = Doc(Body(
            "How it routes",
            new TextParagraph(
            [
                new Run("According to "),
                new Run("Melio's bill pay page", Href: PartnerPage),
                new Run(", approvals route by amount. " + LinkedParagraph),
            ])));

        Assert.DoesNotContain("link-text", Failed(GccDraftGuard.Pillar(doc, Inputs())));
        Assert.DoesNotContain("link-text", Failed(GccDraftGuard.Blog(doc, Inputs())));
        Assert.DoesNotContain("link-text", Failed(GccDraftGuard.Tool(doc, Inputs())));
    }

    [Fact]
    public void A_link_may_sit_on_as_many_words_as_the_limit_and_no_more()
    {
        var atTheLimit = string.Join(' ', Enumerable.Repeat("word", GccDraftGuard.MaxLinkWords));

        Assert.DoesNotContain(
            "link-text", Failed(GccDraftGuard.Pillar(Doc(Body("A", Text(atTheLimit, PartnerPage))), Inputs())));
        Assert.Contains(
            "link-text", Failed(GccDraftGuard.Pillar(Doc(Body("A", Text(atTheLimit + " more", PartnerPage))), Inputs())));
    }

    [Fact]
    public void A_long_item_of_a_list_is_a_passage_too()
    {
        var doc = Doc(Body("Steps", new ListParagraph(false, [[new Run(LinkedParagraph, Href: PartnerPage)]])));

        Assert.Contains("link-text", Failed(GccDraftGuard.Tool(doc, Inputs())));
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
    [InlineData("https://www.geek.test/blog/ap")]
    [InlineData(Scheduler)]
    // A path on the publisher's own site (refused by host alone on 2026-10-04's Accounts Payable run).
    [InlineData("/blog/accounts-payable")]
    // A partner's tool page, at the path the writer was handed -- as a path, and as the same page's
    // full address on the publisher's site.
    [InlineData(RampToolPage)]
    [InlineData("https://geek.test" + RampToolPage)]
    // The tools index is a page of the site, not a tool page.
    [InlineData("/tools")]
    public void A_link_to_the_evidence_the_publisher_or_the_scheduler_is_allowed(string href)
    {
        var doc = Doc(Body("Choosing", Text("See the source.", href)));

        Assert.DoesNotContain("links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    [Theory]
    [InlineData("//competitor.test/pricing")]
    [InlineData("competitor.test/pricing")]
    public void An_address_with_its_scheme_left_off_is_not_a_publisher_path(string href)
    {
        var doc = Doc(Body("Choosing", Text("See the source.", href)));

        Assert.Contains("links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    /// <summary>
    /// Jeff, 2026-10-05, on a pillar: "links or anchor tags to Partners not listed". A tool page is
    /// linked when it is a declared partner's and at the path it is published under, or not at all.
    /// </summary>
    [Theory]
    // Not a partner of this project.
    [InlineData("/tools/accounting/accounts-payable/melio")]
    [InlineData("https://geek.test/tools/accounting/melio")]
    // A partner, at a path no page is published under.
    [InlineData("/tools/marketing/ramp")]
    public void A_tool_page_that_is_not_a_listed_partners_is_not_linked(string href)
    {
        var doc = Doc(Body("Choosing", Text("Ramp routes approvals.", href)));

        var finding = Assert.Single(GccDraftGuard.Pillar(doc, Inputs()).Findings, f => f.Check == "links");
        Assert.True(finding.Refuses);
        Assert.Contains(href, finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pillar_or_blog_that_names_a_tool_the_project_does_not_list_is_refused()
    {
        var doc = Doc(Body("Choosing", Text("With tools like ApprovalMax, Melio, and Ramp, teams tailor workflows.")));
        var inputs = Inputs(requiredTools: ["ApprovalMax", "Ramp"], unlistedTools: ["Melio", "Plooto"]);

        var pillar = Assert.Single(GccDraftGuard.Pillar(doc, inputs).Findings, f => f.Check == "unlisted-tools");
        Assert.True(pillar.Refuses);
        Assert.Contains("Melio", pillar.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Plooto", pillar.Detail, StringComparison.Ordinal);
        Assert.Contains("unlisted-tools", Failed(GccDraftGuard.Blog(doc, inputs)));
    }

    /// <summary>
    /// "Software like Lightyear, Ramp, and Bill offer powerful automation capabilities" on a project with
    /// five partners (Jeff, 2026-10-06: "Which implies the other two do not?").
    /// </summary>
    [Fact]
    public void A_sentence_naming_some_partners_and_not_the_rest_is_refused_and_the_retry_names_both_sides()
    {
        var doc = Doc(Body(
            "What the tools do",
            Text("Software like Lightyear, Ramp, and Bill offer powerful automation capabilities that streamline "
                 + "invoice processing. Stampli and Approvalmax are also options for approvals.")));
        var inputs = Inputs(requiredTools: ["Lightyear", "Ramp", "Bill", "Stampli", "Approvalmax"]);

        var finding = Assert.Single(GccDraftGuard.Pillar(doc, inputs).Findings, f => f.Check == "partner-subset");
        Assert.True(finding.Refuses);
        Assert.Contains("2 sentence(s)", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("names Lightyear, Ramp, Bill and not Stampli, Approvalmax", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("names Stampli, Approvalmax and not Lightyear, Ramp, Bill", finding.RetryInstruction, StringComparison.Ordinal);
        // Every partner is named on the page, so the mentions check is satisfied; this is a different finding.
        Assert.DoesNotContain("partner-mentions", Failed(GccDraftGuard.Blog(doc, inputs)));
        Assert.Contains("partner-subset", Failed(GccDraftGuard.Blog(doc, inputs)));
    }

    [Fact]
    public void Naming_all_partners_together_or_one_at_a_time_is_not_a_subset_finding()
    {
        var doc = Doc(Body(
            "What the tools do",
            Text("Lightyear, Ramp, Bill, Stampli and Approvalmax all route invoices to approvers. Ramp also issues cards. "
                 + "Bill.com syncs with QuickBooks; billing delays fall.")));
        var inputs = Inputs(requiredTools: ["Lightyear", "Ramp", "Bill", "Stampli", "Approvalmax"]);

        Assert.DoesNotContain("partner-subset", Failed(GccDraftGuard.Pillar(doc, inputs)));
        // With two partners every sentence names one or all, so there is nothing partial to find.
        var two = Doc(Body("Choosing", Text("ApprovalMax and Ramp both route approvals. ApprovalMax does so in Xero.")));
        Assert.DoesNotContain("partner-subset", Failed(GccDraftGuard.Pillar(two, Inputs(requiredTools: ["ApprovalMax", "Ramp"]))));
    }

    /// <summary>
    /// The publisher's questions are answered when the reader books; a closing that makes the booking
    /// conditional on a self-check is refused (Jeff, 2026-10-05 and 2026-10-06).
    /// </summary>
    [Fact]
    public void A_closing_that_turns_the_questions_into_a_quiz_is_refused()
    {
        var quiz = Doc(Body(
            "Next steps",
            Text("Consider asking yourself these questions. If these questions highlight inefficiencies in your "
                 + "process, it may be time to book a consultation.")));
        var booking = Doc(Body(
            "Next steps",
            Text("Book the appointment, and answer these questions when booking: how many invoices arrive each "
                 + "month, and who approves them today.")));

        var finding = Assert.Single(GccDraftGuard.Pillar(quiz, Inputs()).Findings, f => f.Check == "questions-quiz");
        Assert.True(finding.Refuses);
        Assert.Contains("\"asking yourself\", \"if these questions\"", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("questions-quiz", Failed(GccDraftGuard.Tool(quiz, Inputs(requiredTools: []))));
        Assert.DoesNotContain("questions-quiz", Failed(GccDraftGuard.Pillar(booking, Inputs())));
    }

    [Fact]
    public void Naming_only_listed_partners_is_not_an_unlisted_tool_finding()
    {
        // "Billing" is not the tool "Bill": names are matched as whole words, as the site writes them.
        var doc = Doc(Body("Choosing", Text("ApprovalMax and Ramp cut billing delays.")));

        var verdict = GccDraftGuard.Pillar(doc, Inputs(requiredTools: ["ApprovalMax", "Ramp"], unlistedTools: ["Bill", "Melio"]));

        Assert.DoesNotContain("unlisted-tools", Failed(verdict));
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
