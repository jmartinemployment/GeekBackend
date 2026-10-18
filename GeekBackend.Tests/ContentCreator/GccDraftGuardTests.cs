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
        int? pageFloorWords = null,
        string? keyword = null) =>
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
            Keyword: keyword,
            PageFloorWords: pageFloorWords);

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

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("word", count));

    /// <summary>
    /// The deletion and the check read a figure the same way: a page the made-up number's sentence was
    /// taken out of passes the check that would have refused it, and what could not be taken out (one
    /// of the page's own sections left with nothing) is still refused.
    /// </summary>
    [Fact]
    public void A_page_a_made_up_numbers_sentence_was_taken_out_of_passes_the_check_that_refused_it()
    {
        const string evidence = "Texas has a $500,000 threshold.";
        const string madeUp = "If you sell 1,000 units in Texas you could miss the trigger.";
        var doc = Doc(Body("Thresholds", Text($"Texas has a $500,000 threshold. {madeUp} Plan for it.")));
        var bare = Doc(Body("Thresholds", Text(madeUp)));

        Assert.Contains("numbers", Failed(GccDraftGuard.Pillar(doc, Inputs(numberEvidence: evidence))));

        var cleaned = GccInventedFigures.Remove(doc, evidence).Document;
        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Pillar(cleaned, Inputs(numberEvidence: evidence))));
        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Blog(cleaned, Inputs(numberEvidence: evidence))));
        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Tool(cleaned, Inputs(numberEvidence: evidence))));

        var left = GccInventedFigures.Remove(bare, evidence).Document;
        Assert.Contains("numbers", Failed(GccDraftGuard.Pillar(left, Inputs(numberEvidence: evidence))));
    }

    [Fact]
    public void A_page_under_its_floor_ships_and_says_how_many_words_it_has_and_how_many_its_score_needs()
    {
        var doc = Doc(Body("Where the hours go", Text(Words(2400))));

        foreach (var (verdict, named) in new[]
                 {
                     (GccDraftGuard.Pillar(doc, Inputs(pageFloorWords: 3000)), "pillar"),
                     (GccDraftGuard.Blog(doc, Inputs(pageFloorWords: 3000)), "blog"),
                     (GccDraftGuard.Tool(doc, Inputs(pageFloorWords: 3000)), "tool page"),
                 })
        {
            var finding = Assert.Single(verdict.Findings, f => f.Check == GccDraftGuard.PageLengthCheck);
            Assert.False(finding.Refuses);
            Assert.StartsWith($"The {named} is 2,4", finding.Detail, StringComparison.Ordinal);
            Assert.EndsWith("words. Its SEO score needs 3,000.", finding.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_pages_words_are_the_ones_its_score_counts()
    {
        // The same reader and tokenizer as the report's "Draft length" check: a page the report passes
        // is not listed here, and one it fails is, at the same count.
        var doc = Doc(Body("Where the hours go", Text(Words(2400))));
        var json = System.Text.Json.JsonSerializer.Serialize(doc, GccDocumentJson.Options);
        var counted = GeekAPI.Services.Gcw.GcwSeoAnalyzer.CountWords(json);
        var report = GeekAPI.Services.Gcw.GcwSeoAnalyzer.Analyze(json, "anything", "pillar");

        Assert.Equal(report.WordCount, counted);
        Assert.DoesNotContain(GccDraftGuard.PageLengthCheck, Failed(GccDraftGuard.Pillar(doc, Inputs(pageFloorWords: counted))));
        var finding = Assert.Single(
            GccDraftGuard.Pillar(doc, Inputs(pageFloorWords: counted + 1)).Findings, f => f.Check == GccDraftGuard.PageLengthCheck);
        Assert.StartsWith($"The pillar is {counted:N0} words.", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_caller_that_names_no_floor_gets_no_length_line()
    {
        var doc = Doc(Body("Where the hours go", Text("Invoices are keyed twice.")));

        Assert.DoesNotContain(GccDraftGuard.PageLengthCheck, Failed(GccDraftGuard.Pillar(doc, Inputs())));
        Assert.DoesNotContain(GccDraftGuard.PageLengthCheck, Failed(GccDraftGuard.Tool(doc, Inputs())));
    }

    private const string Receivable = "Automated Accounts Receivable";

    private static IEnumerable<GccGuardVerdict> EveryType(ContentDocument doc, GccGuardInputs inputs) =>
        [GccDraftGuard.Pillar(doc, inputs), GccDraftGuard.Blog(doc, inputs), GccDraftGuard.Tool(doc, inputs)];

    [Fact]
    public void A_page_with_no_heading_carrying_the_keyword_says_so_once_and_ships()
    {
        var doc = Doc(
            Body("Where the hours go", Text("Invoices are chased by hand.")),
            Body("How accounts receivable gets automated", Text("Reminders go out on a schedule.")));

        foreach (var verdict in EveryType(doc, Inputs(keyword: Receivable)))
        {
            var finding = Assert.Single(verdict.Findings, f => f.Check == "keyword-heading");
            Assert.False(finding.Refuses);
            Assert.EndsWith(
                "has no heading containing \"Automated Accounts Receivable\". Its SEO score needs one.",
                finding.Detail,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_keyword_heading_anywhere_on_the_page_is_enough_whichever_call_wrote_it()
    {
        // The pillar of 2026-10-10: its first call is two sections about the work done by hand, the
        // writer rightly put no "Automated ..." heading on them, and the page was reported for it with
        // eight later headings carrying the phrase.
        var doc = Doc(
            Body("What is going wrong in collections today", Text("Invoices are chased by hand.")),
            Body("What late payment costs the business", Text("Cash arrives weeks late.")),
            Body("How automated accounts receivable works end to end", Text("Reminders go out on a schedule.")));

        foreach (var verdict in EveryType(doc, Inputs(keyword: Receivable)))
        {
            Assert.DoesNotContain("keyword-heading", Failed(verdict));
            Assert.DoesNotContain("keyword-heading-by-hand", Failed(verdict));
        }

        // The page's score agrees: it is the score's own check that is read.
        var report = GeekAPI.Services.Gcw.GcwSeoAnalyzer.Analyze(
            System.Text.Json.JsonSerializer.Serialize(doc, GccDocumentJson.Options), Receivable, "pillar");
        Assert.True(report.Checks.Single(c => c.Id == "keyword-in-heading").Passed);
    }

    [Fact]
    public void A_heading_that_puts_the_keyword_after_manual_is_named_and_the_page_ships()
    {
        // Every tool page of 2026-10-10. The score passes the heading, since the phrase is in it, and a
        // reader does not.
        var doc = Doc(
            Body("The Challenges of Manual Automated Accounts Receivable", Text("Invoices are chased by hand.")),
            Body("How Upflow Runs Automated Accounts Receivable", Text("Reminders go out on a schedule.")));

        foreach (var verdict in EveryType(doc, Inputs(keyword: Receivable)))
        {
            var finding = Assert.Single(verdict.Findings, f => f.Check == "keyword-heading-by-hand");
            Assert.False(finding.Refuses);
            Assert.Contains("\"The Challenges of Manual Automated Accounts Receivable\"", finding.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("How Upflow Runs", finding.Detail, StringComparison.Ordinal);
            Assert.DoesNotContain("keyword-heading", verdict.Findings.Where(f => f != finding).Select(f => f.Check));
        }
    }

    [Fact]
    public void Manual_elsewhere_in_a_heading_or_before_a_keyword_of_another_shape_is_not_named()
    {
        var apart = Doc(Body("How Manual Chasing Gives Way to Automated Accounts Receivable", Text("Reminders go out.")));
        Assert.DoesNotContain("keyword-heading-by-hand", Failed(GccDraftGuard.Tool(apart, Inputs(keyword: Receivable))));

        // "Manual" contradicts a keyword that begins "Automated". It contradicts nothing in this one.
        var otherShape = Doc(Body("Manual Invoice Capture and What Replaces It", Text("Capture is keyed twice.")));
        Assert.DoesNotContain("keyword-heading-by-hand", Failed(GccDraftGuard.Tool(otherShape, Inputs(keyword: "Invoice Capture"))));
    }

    [Fact]
    public void A_page_with_no_keyword_is_held_to_no_heading_check()
    {
        var doc = Doc(Body("The Challenges of Manual Automated Accounts Receivable", Text("Invoices are chased by hand.")));

        Assert.DoesNotContain("keyword-heading", Failed(GccDraftGuard.Pillar(doc, Inputs())));
        Assert.DoesNotContain("keyword-heading-by-hand", Failed(GccDraftGuard.Pillar(doc, Inputs())));
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

    /// <summary>
    /// No check reads a page for a tool that is not one of the project's partners (Jeff, 2026-10-13:
    /// "You are removing this stupid rule?"). The check that did refused a pillar and a blog that day
    /// for naming Stripe, which the operator's own Brief names.
    /// </summary>
    [Fact]
    public void A_pillar_or_blog_that_names_a_tool_the_project_does_not_list_is_neither_refused_nor_listed_for_it()
    {
        var doc = Doc(Body("Choosing", Text("With tools like ApprovalMax, Melio, and Ramp, teams tailor workflows.")));
        var inputs = Inputs(requiredTools: ["ApprovalMax", "Ramp"]);

        Assert.DoesNotContain(GccDraftGuard.Pillar(doc, inputs).Findings, f => f.Detail.Contains("Melio", StringComparison.Ordinal));
        Assert.DoesNotContain(GccDraftGuard.Blog(doc, inputs).Findings, f => f.Detail.Contains("Melio", StringComparison.Ordinal));
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
    public void A_heading_split_by_a_colon_is_listed_and_the_page_is_not_refused()
    {
        var split = Doc(
            Body("The Real Cost of Manual Planning: Time, Errors, and Who Pays", Text("Body text.")),
            Body("What Manual Planning Costs a Small Team", Text("Body text.")),
            Body("Can the tool post at 9:30 each morning?", Text("An operator's question keeps its colon.")));
        var plain = Doc(Body("What Manual Planning Costs a Small Team", Text("Body text.")));

        var finding = Assert.Single(GccDraftGuard.Pillar(split, Inputs()).Findings, f => f.Check == "heading-colon");
        Assert.False(finding.Refuses);
        Assert.Contains("\"The Real Cost of Manual Planning: Time, Errors, and Who Pays\"", finding.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("9:30", finding.Detail, StringComparison.Ordinal);
        Assert.Contains(GccDraftGuard.Tool(split, Inputs(requiredTools: [])).Findings, f => f.Check == "heading-colon");
        Assert.DoesNotContain(GccDraftGuard.Pillar(plain, Inputs()).Findings, f => f.Check == "heading-colon");
    }

    /// <summary>
    /// The writer's own checklist about a tool is the writer's own words, and nothing refuses a page for it.
    /// The 12:56 UTC run of 2026-10-18 lost MarketMuse's tool page to "When judging fit, ask yourself these
    /// questions:", when a check that has since been removed matched the phrase.
    /// </summary>
    [Fact]
    public void A_page_that_asks_the_reader_to_ask_themselves_questions_is_not_refused()
    {
        var checklist = Doc(Body(
            "What to Weigh When Judging a Tool for a Small Team",
            Text("When judging fit, ask yourself these questions. If any of these describe your team, the tool is "
                 + "likely a strong fit.")));

        Assert.DoesNotContain(GccDraftGuard.Pillar(checklist, Inputs()).Findings, f => f.Refuses);
        Assert.DoesNotContain(GccDraftGuard.Tool(checklist, Inputs(requiredTools: [])).Findings, f => f.Refuses);
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

    /// <summary>
    /// The 15:29 UTC run of 2026-10-12, its sentence and the line of Zamp's page it came from. The
    /// page states its totals as "100K+" and "200K+"; the writer wrote them out, and the blog was
    /// refused for "200,000 hours" as a figure in none of its evidence. "100,000" was let through only
    /// because the same evidence holds "$100,000" elsewhere, the sales tax threshold.
    /// </summary>
    [Fact]
    public void A_figure_the_evidence_writes_with_a_scale_passes_when_the_draft_writes_it_out()
    {
        const string evidence =
            "$300M+ sales tax remitted 100K+ filings on-time 75K+ notices handled 200K+ hours saved";
        var doc = Doc(Body(
            "Outcomes",
            Text("For instance, data from Zamp highlights over 100,000 on-time filings and 200,000 hours saved."),
            Text("It has remitted more than $300,000,000 in sales tax and handled 75,000 notices.")));

        Assert.DoesNotContain("numbers", Failed(GccDraftGuard.Blog(doc, Inputs(numberEvidence: evidence))));
    }

    [Fact]
    public void A_figure_no_scaled_number_in_the_evidence_stands_for_is_still_refused()
    {
        const string evidence = "100K+ filings on-time 200K+ hours saved";
        var doc = Doc(Body("Outcomes", Text("Zamp has saved its customers 500,000 hours.")));

        var verdict = GccDraftGuard.Blog(doc, Inputs(numberEvidence: evidence));

        var finding = Assert.Single(verdict.Findings, f => f.Check == "numbers");
        Assert.True(finding.Refuses);
        Assert.Contains("500,000 hours", finding.Detail, StringComparison.Ordinal);
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
