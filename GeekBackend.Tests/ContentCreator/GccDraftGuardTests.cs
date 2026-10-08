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
            Assert.Contains(PartnerPage, finding.Detail, StringComparison.Ordinal);
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
    /// There is no rule about how many partners one sentence may name. A guard that refused any sentence
    /// naming some of them and not all was added on 2026-10-06 from a remark of Jeff's ("Which implies the
    /// other two do not?") and removed on 2026-10-07: it took the remark out of its context and refused a
    /// whole pillar over four sentences. What is still checked is that every partner is named somewhere
    /// (a reported gap) and that no tool the project does not list is named (a refusal).
    /// </summary>
    [Fact]
    public void A_sentence_naming_some_of_the_partners_is_not_refused()
    {
        var doc = Doc(Body(
            "What the tools do",
            Text("Tools like Bill, Ramp, and ApprovalMax route invoices to the right approvers. "
                 + "Tools like Approvalmax and Stampli integrate with the ledger. Melio pays the approved bill.")));
        var inputs = Inputs(requiredTools: ["Bill", "Ramp", "Approvalmax", "Stampli", "Melio"]);

        foreach (var verdict in new[] { GccDraftGuard.Pillar(doc, inputs), GccDraftGuard.Blog(doc, inputs) })
        {
            Assert.DoesNotContain("partner-subset", Failed(verdict));
            Assert.Empty(verdict.Refusals);
        }
    }

    /// <summary>
    /// The 16:52 run of 2026-10-06 wrote "ApprovalMax" against a partner list spelling it "Approvalmax". A
    /// brand is itself whatever its capitals; a lower-case common word is not the brand.
    /// </summary>
    [Fact]
    public void A_partner_is_named_whatever_its_capitals_but_a_lower_case_word_is_not_the_brand()
    {
        Assert.True(GccRequiredToolMentions.NamesAsWord("Tools like Bill, Ramp, and ApprovalMax route approvals.", "Approvalmax"));
        Assert.True(GccRequiredToolMentions.NamesAsWord("Bill.com syncs with QuickBooks.", "Bill"));
        Assert.False(GccRequiredToolMentions.NamesAsWord("Every bill still needs a nudge.", "Bill"));
        Assert.False(GccRequiredToolMentions.NamesAsWord("A stampli-shaped nudge.", "Stampli"));
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
    }

    [Fact]
    public void A_tool_page_with_candidates_and_no_quotation_ships_with_the_gap_named()
    {
        // Jeff, 2026-10-08: do not fail the page when no fitting quotation was found. The gap is a
        // non-refusing finding the run reports; nothing is substituted for the quotation.
        var doc = Doc(Body("Melio at work", Text("Melio moves the money.")));

        var verdict = GccDraftGuard.Tool(doc, Inputs(requiredTools: ["Melio"], candidates: []));

        Assert.DoesNotContain("quotation", Failed(verdict));
        var gap = Assert.Single(verdict.Findings, f => f.Check == "blockquote-missing");
        Assert.False(gap.Refuses);
        Assert.Contains("carries no block quotation", gap.Detail, StringComparison.Ordinal);
    }

    // ---- the opening carries no links ----------------------------------------------------------------

    private static ContentDocument DocWithOpening(Section opening) =>
        new(opening, [Body("How it routes", Text("Approvals route by amount.")), Closing]);

    [Fact]
    public void A_link_in_the_opening_is_reported_with_the_draft_on_every_type_and_never_refuses_it()
    {
        var doc = DocWithOpening(new Section(
            "h2", "Opening", [new TextParagraph([new Run("As outlined on "), new Run("Geek's site", Href: PartnerPage)])], null, []));

        foreach (var verdict in new[]
                 {
                     GccDraftGuard.Pillar(doc, Inputs()), GccDraftGuard.Blog(doc, Inputs()), GccDraftGuard.Tool(doc, Inputs()),
                 })
        {
            var finding = Assert.Single(verdict.Findings, f => f.Check == "opening-links");
            Assert.False(finding.Refuses);
            Assert.Contains("Geek's site", finding.Detail, StringComparison.Ordinal);
            Assert.Contains(PartnerPage, finding.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain(verdict.Refusals, f => f.Check == "opening-links");
        }
    }

    [Fact]
    public void A_link_in_a_section_nested_under_the_opening_is_part_of_the_opening()
    {
        var child = new Section("h3", "Nested", [new TextParagraph([new Run("named", Href: PartnerPage)])], null, []);
        var doc = DocWithOpening(new Section("h2", "Opening", [Text("An opening.")], null, [child]));

        Assert.Contains("opening-links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
    }

    [Fact]
    public void An_opening_with_no_link_is_not_reported_and_links_in_the_body_are_not_the_openings()
    {
        var doc = DocWithOpening(new Section("h2", "Opening", [Text("An opening.")], null, []));
        var withBodyLink = new ContentDocument(
            doc.Lede, [Body("How it routes", Text("Approvals route by amount.", PartnerPage)), Closing]);

        Assert.DoesNotContain("opening-links", Failed(GccDraftGuard.Pillar(doc, Inputs())));
        Assert.DoesNotContain("opening-links", Failed(GccDraftGuard.Blog(withBodyLink, Inputs())));
        Assert.DoesNotContain("opening-links", Failed(GccDraftGuard.Tool(withBodyLink, Inputs())));
    }
}
