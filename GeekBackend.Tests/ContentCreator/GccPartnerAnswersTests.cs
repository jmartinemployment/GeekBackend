using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.ContentTypes;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// What the operator's research says each partner answers, as the pillar and the blog are told it
/// (Jeff, 2026-10-11). The rows entered under a partner were read for that partner's search and tool
/// page alone; the pages about every partner were told to name each one and nothing of which failure
/// each answers.
/// </summary>
public class GccPartnerAnswersTests
{
    private static readonly string[] PartnerUrls = ["https://www.chaserhq.com/", "https://upflow.io/", "https://www.bill.com/"];

    private const string Brief = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "coreProblem": "Cash arrives whenever the customer gets round to paying.",
            "painPoints": "Invoices go out late.",
            "automationToPitch": "Invoice-to-cash on a schedule.",
            "evidence": [
              { "problem": "Invoices go out late.", "solution": "Scheduled invoicing from the accounting system.", "terms": ["scheduled invoicing"] }
            ],
            "perTool": {
              "upflow.io": {
                "coreProblem": "Collections run on one person's memory.",
                "evidence": [
                  { "problem": "Disputes stall a payment for weeks.", "solution": "A customer portal where a dispute is raised against the invoice.", "terms": ["customer portal"] },
                  { "problem": "", "solution": "", "terms": [] },
                  { "problem": "Nobody knows which invoices to chase first.", "solution": "", "terms": ["prioritisation"] }
                ]
              },
              "chaserhq.com": {
                "automationToPitch": "Get paid without chasing: reminders that send themselves in 5 minutes of setup.",
                "evidence": [
                  { "problem": "Reminders are sent by hand, when someone remembers.", "solution": "Automated reminder schedules by email and SMS.", "terms": ["payment reminders"] },
                  { "problem": "", "solution": "Late payment prediction from each customer's history.", "terms": [] }
                ]
              },
              "bill.com": { "coreProblem": "Payables and receivables are run in two places.", "evidence": [] }
            }
          }
        }
        """;

    [Fact]
    public void Each_partner_is_listed_with_its_own_failures_and_remedies_in_the_projects_order()
    {
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls);

        Assert.NotNull(block);
        var lines = block!.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var partners = lines.SkipWhile(l => l != "Chaserhq:").ToList();
        Assert.Equal(
            [
                "Chaserhq:",
                "  1. Where it goes wrong: Reminders are sent by hand, when someone remembers.",
                "     What Chaserhq does about it: Automated reminder schedules by email and SMS.",
                "  2. What Chaserhq does: Late payment prediction from each customer's history.",
                // What to pitch it on comes last, as it does in the operator's research.
                "  What the operator would pitch Chaserhq on: Get paid without chasing: reminders that send themselves in 5 minutes of setup.",
                "Upflow:",
                // Its own problem comes first. The category's is never printed in its place.
                "  The problem Upflow is for: Collections run on one person's memory.",
                "  1. Where it goes wrong: Disputes stall a payment for weeks.",
                "     What Upflow does about it: A customer portal where a dispute is raised against the invoice.",
                // A failure with no remedy noted is listed alone; a row with neither is not a row.
                "  2. Where it goes wrong: Nobody knows which invoices to chase first.",
                // A partner with a problem of its own and no row is listed with what it has.
                "Bill:",
                "  The problem Bill is for: Payables and receivables are run in two places.",
                "",
            ],
            partners);
    }

    /// <summary>
    /// A pillar or a blog discussing a tool can use what the operator entered for it (Jeff, 2026-10-12:
    /// "pillars &amp; blog should have the research data"). A tool with a box left empty is not given the
    /// category's there: the page already carries the category's as its own framing.
    /// </summary>
    [Fact]
    public void A_partners_own_problem_and_pitch_are_listed_and_the_categorys_are_not_printed_in_their_place()
    {
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;

        Assert.Contains("The problem Upflow is for: Collections run on one person's memory.", block, StringComparison.Ordinal);
        Assert.Contains("What the operator would pitch Chaserhq on: Get paid without chasing", block, StringComparison.Ordinal);
        // Chaserhq has no problem of its own and Upflow no pitch: neither gets the category's.
        Assert.DoesNotContain("The problem Chaserhq is for", block, StringComparison.Ordinal);
        Assert.DoesNotContain("pitch Upflow on", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Cash arrives whenever the customer gets round to paying.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice-to-cash on a schedule.", block, StringComparison.Ordinal);
        // A pitch may name a capability: it is the operator's claim, stated only as passages show it.
        Assert.Contains("What a partner is pitched on is the case to make for it", block, StringComparison.Ordinal);
        Assert.Contains("state it only as that partner's passages show it", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_for_all_partners_names_no_partner_and_is_not_listed()
    {
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;

        Assert.DoesNotContain("Invoices go out late.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Scheduled invoicing", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partners_search_terms_are_not_listed_and_a_partner_with_nothing_of_its_own_has_no_heading()
    {
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;
        var withoutBill = GccPartnerAnswers.Instruction(
            Brief.Replace("\"coreProblem\": \"Payables and receivables are run in two places.\", ", string.Empty, StringComparison.Ordinal),
            PartnerUrls)!;

        Assert.DoesNotContain("customer portal\n", block, StringComparison.Ordinal);
        Assert.DoesNotContain("prioritisation", block, StringComparison.Ordinal);
        // A partner with no row, no problem and no pitch of its own is not given an empty heading.
        Assert.DoesNotContain("Bill:", withoutBill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_is_told_it_is_the_operators_research_and_what_a_partner_does_comes_from_its_passages()
    {
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;

        Assert.StartsWith("WHAT EACH PARTNER TOOL ANSWERS -- THE OPERATOR'S OWN RESEARCH", block, StringComparison.Ordinal);
        Assert.Contains("not retrieved evidence", block, StringComparison.Ordinal);
        Assert.Contains("take no figure from it", block, StringComparison.Ordinal);
        Assert.Contains("only as its own passages show it", block, StringComparison.Ordinal);
        Assert.Contains("not added to the ones a section is told to cover", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partner_is_spelled_as_the_required_mentions_block_spells_it()
    {
        var required = GccRequiredToolMentions.For(Brief, PartnerUrls);
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;

        Assert.Contains("Chaserhq", required);
        Assert.Contains("Upflow", required);
        foreach (var name in new[] { "Chaserhq", "Upflow" })
        {
            Assert.Contains($"{Environment.NewLine}{name}:{Environment.NewLine}", block, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_host_that_is_not_one_of_the_projects_partners_is_not_listed()
    {
        var block = GccPartnerAnswers.Instruction(Brief, ["https://upflow.io/"])!;

        Assert.Contains("Upflow:", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Chaserhq", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Reminders are sent by hand", block, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "nicheFraming": { "coreProblem": "x", "evidence": [ { "problem": "p", "solution": "s", "terms": [] } ] } }""")]
    [InlineData("""{ "nicheFraming": { "perTool": { "upflow.io": { "coreProblem": " ", "automationToPitch": "", "evidence": [] } } } }""")]
    [InlineData("""{ "nicheFraming": { "perTool": { "gone.example": { "coreProblem": "x", "automationToPitch": "y" } } } }""")]
    public void A_brief_with_nothing_under_any_partner_of_the_project_says_nothing(string? briefJson)
    {
        Assert.Null(GccPartnerAnswers.Instruction(briefJson, PartnerUrls));
    }

    [Fact]
    public void A_project_with_no_partners_says_nothing()
    {
        Assert.Null(GccPartnerAnswers.Instruction(Brief, []));
        Assert.Null(GccPartnerAnswers.Instruction(Brief, null));
    }

    [Fact]
    public void A_partners_own_rows_are_read_by_host_whatever_the_www_or_the_case()
    {
        Assert.Equal(2, GccNicheFramingReader.OwnRowsForHost(Brief, "upflow.io").Count);
        Assert.Equal(2, GccNicheFramingReader.OwnRowsForHost(Brief, "UPFLOW.io").Count);
        Assert.Equal(2, GccNicheFramingReader.OwnRowsForHost(Brief, GccRequiredToolMentions.HostKeyOf("https://www.chaserhq.com/pricing")).Count);
        // The category's rows are not a partner's own, and neither is a partner's with none.
        Assert.Empty(GccNicheFramingReader.OwnRowsForHost(Brief, "bill.com"));
        Assert.Empty(GccNicheFramingReader.OwnRowsForHost(Brief, "tipalti.com"));
        Assert.Empty(GccNicheFramingReader.OwnRowsForHost(Brief, ""));
        Assert.Empty(GccNicheFramingReader.OwnRowsForHost(null, "upflow.io"));
    }

    // ---- a brief with every failure under a partner and none for all of them (2026-10-11) ------------
    //
    // The operator's research is per partner, and the first brief entered that way had 25 failures under
    // five partners and none for the category. The pillar's and the blog's sections on where the work
    // goes wrong were told no failure at all.

    /// <summary>The brief with the category's own failures taken out: its paragraphs and its rows.</summary>
    private static string WithNoFailureForAllPartners(string briefJson)
    {
        var brief = (JsonObject)JsonNode.Parse(briefJson)!;
        var framing = (JsonObject)brief["nicheFraming"]!;
        framing["painPoints"] = "";
        framing["evidence"] = new JsonArray();
        return brief.ToJsonString();
    }

    [Fact]
    public void With_no_failure_for_all_partners_the_partners_failures_are_the_pages()
    {
        var brief = WithNoFailureForAllPartners(Brief);

        Assert.True(GccPartnerAnswers.CarryThePagesFailures(brief, PartnerUrls));
        var block = GccPartnerAnswers.Instruction(brief, PartnerUrls)!;
        Assert.Contains("The operator named no failure apart from these.", block, StringComparison.Ordinal);
        Assert.Contains("A section told to argue where the work goes wrong argues it from these", block, StringComparison.Ordinal);
        Assert.DoesNotContain("not added to the ones a section is told to cover", block, StringComparison.Ordinal);
    }

    [Fact]
    public void With_a_failure_for_all_partners_the_partners_failures_are_not_the_pages()
    {
        Assert.False(GccPartnerAnswers.CarryThePagesFailures(Brief, PartnerUrls));
        var block = GccPartnerAnswers.Instruction(Brief, PartnerUrls)!;
        Assert.Contains("not added to the ones a section is told to cover", block, StringComparison.Ordinal);
        Assert.DoesNotContain("The operator named no failure apart from these.", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_for_all_partners_entered_as_a_row_alone_is_still_the_categorys()
    {
        // The form derives the paragraphs from the rows; a brief written by hand may carry only the row.
        var brief = (JsonObject)JsonNode.Parse(Brief)!;
        ((JsonObject)brief["nicheFraming"]!)["painPoints"] = "";

        Assert.False(GccPartnerAnswers.CarryThePagesFailures(brief.ToJsonString(), PartnerUrls));
    }

    [Fact]
    public void Partners_that_carry_remedies_and_no_failure_give_the_page_no_failures()
    {
        const string brief = """
            { "nicheFraming": { "coreProblem": "x", "perTool": { "upflow.io": { "evidence": [
              { "problem": "", "solution": "A customer portal.", "terms": [] } ] } } } }
            """;

        Assert.False(GccPartnerAnswers.CarryThePagesFailures(brief, PartnerUrls));
        Assert.Contains("not added to the ones a section is told to cover", GccPartnerAnswers.Instruction(brief, PartnerUrls)!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_under_a_host_that_is_not_a_partner_of_the_project_gives_the_page_no_failures()
    {
        var brief = WithNoFailureForAllPartners(Brief);

        Assert.False(GccPartnerAnswers.CarryThePagesFailures(brief, ["https://www.bill.com/"]));
        Assert.False(GccPartnerAnswers.CarryThePagesFailures(brief, []));
        Assert.False(GccPartnerAnswers.CarryThePagesFailures(null, PartnerUrls));
    }

    [Fact]
    public void The_failure_sections_are_sent_to_the_partners_failures_by_the_blocks_own_heading()
    {
        var brief = WithNoFailureForAllPartners(Brief);
        var framing = GccNicheFramingReader.ForEveryPartner(brief, GccPartnerAnswers.CarryThePagesFailures(brief, PartnerUrls))!;

        Assert.True(framing.FailuresAreUnderThePartners);
        var guidance = framing.FailuresGuidance()!;
        var block = GccPartnerAnswers.Instruction(brief, PartnerUrls)!;
        // The section names the block and its lines as the block itself spells them.
        Assert.Contains(GccPartnerAnswers.Heading, guidance, StringComparison.Ordinal);
        Assert.StartsWith(GccPartnerAnswers.Heading, block, StringComparison.Ordinal);
        Assert.Contains($"\"{GccPartnerAnswers.FailureLabel}\" lines", guidance, StringComparison.Ordinal);
        Assert.Contains($"1. {GccPartnerAnswers.FailureLabel}: ", block, StringComparison.Ordinal);
        Assert.Contains("add no failure the operator did not name", guidance, StringComparison.Ordinal);
        Assert.Contains("never cite it", guidance, StringComparison.Ordinal);
        // Twenty-five failures do not fit two sections; it is not told to cover every one.
        Assert.DoesNotContain("every one", guidance, StringComparison.Ordinal);
        // The rest of the category's framing is as it was.
        Assert.Equal("Cash arrives whenever the customer gets round to paying.", framing.CoreProblem);
        Assert.Contains("Invoice-to-cash on a schedule.", framing.ApproachGuidance()!, StringComparison.Ordinal);
        Assert.DoesNotContain("Where it goes wrong", framing.ToGuidance()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_category_that_names_its_own_failures_keeps_them_whatever_it_is_told()
    {
        var category = GccNicheFramingReader.ForCategory(Brief)!;

        var framing = GccNicheFramingReader.ForEveryPartner(Brief, failuresAreUnderThePartners: true)!;

        Assert.False(framing.FailuresAreUnderThePartners);
        Assert.Equal(category.FailuresGuidance(), framing.FailuresGuidance());
        Assert.Contains("Cover every one of these", framing.FailuresGuidance()!, StringComparison.Ordinal);
        Assert.Contains("1. Invoices go out late.", framing.FailuresGuidance()!, StringComparison.Ordinal);
    }

    [Fact]
    public void Told_the_failures_are_not_under_the_partners_the_framing_is_the_categorys()
    {
        var brief = WithNoFailureForAllPartners(Brief);

        var framing = GccNicheFramingReader.ForEveryPartner(brief, failuresAreUnderThePartners: false)!;

        Assert.False(framing.FailuresAreUnderThePartners);
        Assert.Null(framing.FailuresGuidance());
    }

    [Fact]
    public void A_brief_with_nothing_for_the_category_still_sends_the_failure_sections_to_the_partners()
    {
        const string brief = """
            { "nicheFraming": { "perTool": { "upflow.io": { "evidence": [
              { "problem": "Disputes stall a payment for weeks.", "solution": "A customer portal.", "terms": [] } ] } } } }
            """;
        Assert.Null(GccNicheFramingReader.ForCategory(brief));

        var framing = GccNicheFramingReader.ForEveryPartner(brief, GccPartnerAnswers.CarryThePagesFailures(brief, PartnerUrls));

        Assert.NotNull(framing);
        Assert.True(framing!.HasAny);
        Assert.Contains(GccPartnerAnswers.Heading, framing.FailuresGuidance()!, StringComparison.Ordinal);
        // Nothing else was entered, and nothing else is said: no heading over an empty framing.
        Assert.Null(framing.ToGuidance());
        Assert.Null(framing.ShortFormGuidance());
        Assert.Null(framing.HookGuidance());
        Assert.Null(framing.ApproachGuidance());
        Assert.Null(framing.ApproachPointer());
    }

    [Fact]
    public void The_pillars_two_failure_sections_carry_it_and_no_other_section_does()
    {
        var brief = WithNoFailureForAllPartners(Brief);
        var framing = GccNicheFramingReader.ForEveryPartner(brief, GccPartnerAnswers.CarryThePagesFailures(brief, PartnerUrls));

        var outline = PillarPrompts.Outline(framing);

        var sent = outline
            .Select((slot, index) => (slot, index))
            .Where(s => (s.slot.Guidance ?? string.Empty).Contains(GccPartnerAnswers.Heading, StringComparison.Ordinal))
            .Select(s => s.index)
            .ToList();
        Assert.Equal([1, 2], sent);
        Assert.Equal(framing!.FailuresGuidance(), outline[1].Guidance);
    }
}
