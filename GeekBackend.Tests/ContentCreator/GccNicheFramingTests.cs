using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The operator's framing of a niche, read off the brief and resolved per product.
/// </summary>
/// <remarks>
/// <para>
/// The brief had no problem field, so on a <c>problem_solution</c> angle the writer invented the
/// problem, its cost and its failure modes on every page while Jeff researched exactly those three
/// things by hand and the answer was discarded.
/// </para>
/// <para>
/// Two shapes have to work from one set of fields, because Jeff's own research arrives both ways:
/// Perplexity repeated the three sections per tool, Claude desktop stated them once for the category
/// and listed five tools beneath. Hence category-level with optional per-tool overrides.
/// </para>
/// </remarks>
public class GccNicheFramingTests
{
    private static readonly string[] PartnerUrls =
        ["https://dext.com", "https://bill.com", "https://melio.com"];

    /// <summary>Desktop's shape: the framing stated once, no per-tool entries at all.</summary>
    private const string CategoryOnlyBrief = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "taxonomyPath": "Accounting -> Cash Flow Forecasting -> Accounts Receivable",
            "coreProblem": "Revenue is booked when the invoice goes out, cash arrives whenever the customer gets round to paying.",
            "painPoints": "Nobody owns collections. Follow-ups depend on whoever has time that week.\n\nThey expect the accounting system to collect, and it does not chase.\n\nThey make it hard to pay, so the customer has a reason to wait.",
            "automationToPitch": "Invoice-to-cash on a schedule: reminders, payment links, reconciliation, a weekly collection forecast."
          }
        }
        """;

    /// <summary>Perplexity's shape: a category set plus a distinct frame for one tool.</summary>
    private const string PerToolBrief = """
        {
          "angle": "problem_solution",
          "nicheFraming": {
            "taxonomyPath": ["Accounting","Cash Flow Forecasting","Accounts Receivable"],
            "coreProblem": "Category level problem.",
            "painPoints": "Category pain.",
            "automationToPitch": "Category automation.",
            "perTool": {
              "bill.com": {
                "coreProblem": "The SMB needs AR now and AP next.",
                "painPoints": "Billing is triggered by a person remembering.\n\nTerms drift per customer.",
                "automationToPitch": "One connected finance workflow from invoice through reconciliation."
              }
            }
          }
        }
        """;

    [Fact]
    public void A_category_only_brief_frames_every_product()
    {
        // Desktop's arrangement. No per-tool entries, so all five pages share the category frame --
        // which is correct: the operator judged one problem for the category, not five.
        foreach (var product in new[] { "Dext", "Bill", "Melio" })
        {
            var framing = GccNicheFramingReader.ForProduct(CategoryOnlyBrief, PartnerUrls, product);

            Assert.NotNull(framing);
            Assert.Contains("Revenue is booked", framing!.CoreProblem, StringComparison.Ordinal);
            Assert.Equal(3, framing.PainPoints.Count);
        }
    }

    [Fact]
    public void A_per_tool_override_wins_for_that_product_only()
    {
        var bill = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Bill");
        var dext = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Dext");

        // The Core Problem is replaced -- two core problems on one page is incoherent.
        Assert.Equal("The SMB needs AR now and AP next.", bill!.CoreProblem);
        // The pain points are ADDED to the category's, not substituted. Changed deliberately on
        // 2026-10-03: this asserted 2 (the override's alone) when the override replaced the whole set.
        // PerToolBrief carries one category pain point and two for Bill.
        Assert.Equal(3, bill.PainPoints.Count);
        Assert.Equal("Category pain.", bill.PainPoints[0]);

        // Dext has no entry, so it inherits rather than coming back empty.
        Assert.Equal("Category level problem.", dext!.CoreProblem);
        Assert.Equal(["Category pain."], dext.PainPoints);
    }

    [Fact]
    public void A_partial_override_keeps_the_category_fields_it_did_not_touch()
    {
        // The question that found this: does the backend combine these? It has to, per field. Bill.com
        // owns a distinct problem -- approval treated as an email reply -- while the category's pain
        // points and automation still apply to it. Replacing the whole set discarded both.
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "Cash arrives whenever the customer gets round to paying.",
                "painPoints": "Nobody owns collections.\n\nThey make it hard to pay.",
                "automationToPitch": "Invoice-to-cash on a schedule.",
                "perTool": {
                  "bill.com": {
                    "coreProblem": "Approval is an email reply, a verbal instruction, or bank access.",
                    "painPoints": "",
                    "automationToPitch": ""
                  }
                }
              }
            }
            """;

        var bill = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill");

        Assert.StartsWith("Approval is an email reply", bill!.CoreProblem, StringComparison.Ordinal);
        // The two it said nothing about are inherited, not blanked.
        Assert.Equal(2, bill.PainPoints.Count);
        Assert.Equal("Invoice-to-cash on a schedule.", bill.AutomationToPitch);
    }

    [Fact]
    public void A_tools_pain_points_are_added_to_the_categorys_not_substituted_for_them()
    {
        // Settled by the research, 2026-10-03. One category query for "Accounts Payable: Automated
        // Approval Workflows" returned fourteen pain points -- nine tabular failures plus five
        // consequence paragraphs -- every one true of all five AP tools. A tool-specific failure is
        // additional to those, never a replacement, so substituting would discard almost everything the
        // operator gathered.
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "Approval lives across email, paper and the ledger with no controlled path.",
                "painPoints": "Approvals stuck with one person.\n\nConstant chasing and follow-up.\n\nNo single invoice-status view.",
                "automationToPitch": "A controlled invoice-to-payment workflow.",
                "perTool": {
                  "bill.com": { "painPoints": "Approval thresholds are unclear above $5,000." }
                }
              }
            }
            """;

        var bill = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill");

        Assert.Equal(4, bill!.PainPoints.Count);
        // Category first, then the tool's: the shared problem is established before the slice.
        Assert.Equal("Approvals stuck with one person.", bill.PainPoints[0]);
        Assert.Equal("Approval thresholds are unclear above $5,000.", bill.PainPoints[3]);
        // The single-statement fields still come from the category, which said something and the tool did not.
        Assert.StartsWith("Approval lives across", bill.CoreProblem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_failure_restated_in_an_override_is_not_argued_twice()
    {
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "x",
                "painPoints": "Approvals stuck with one person.\n\nConstant chasing.",
                "perTool": {
                  "bill.com": { "painPoints": "Constant chasing.\n\nNo mobile approval." }
                }
              }
            }
            """;

        var bill = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill");

        Assert.Equal(
            ["Approvals stuck with one person.", "Constant chasing.", "No mobile approval."],
            bill!.PainPoints);
    }

    [Fact]
    public void Guidance_lists_the_pain_points_rather_than_running_them_together()
    {
        // Fourteen paragraphs joined on " | " is a wall of text the model parses before it can use any
        // of it. Numbered, one per line.
        var framing = GccNicheFramingReader.ForCategory(
            """
            {"nicheFraming":{"coreProblem":"x","painPoints":"First failure.\n\nSecond failure.\n\nThird failure."}}
            """);

        var guidance = framing!.ToGuidance()!;

        Assert.DoesNotContain(" | ", guidance, StringComparison.Ordinal);
        Assert.Contains("1. First failure.", guidance, StringComparison.Ordinal);
        Assert.Contains("3. Third failure.", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void The_override_is_keyed_by_host_not_by_a_typed_name()
    {
        // The catch this exists to prevent. The fan-out buckets by host via
        // GccRequiredToolMentions.HostKeyOf and names products via AnchorLookup, so a brief keyed off a
        // free-typed product name must still resolve to the right slice -- "Bill" is the product name
        // AnchorLookup derives for bill.com, and the override is keyed "bill.com".
        var framing = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Bill");

        Assert.Equal("The SMB needs AR now and AP next.", framing!.CoreProblem);
    }

    [Fact]
    public void A_product_this_project_declares_no_partner_for_falls_back_to_the_category()
    {
        var framing = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Notion");

        // Not null and not the override: an unknown product still gets the category's problem, because
        // the problem is the category's regardless of which tool is being written about.
        Assert.Equal("Category level problem.", framing!.CoreProblem);
    }

    [Fact]
    public void An_override_opened_and_left_blank_inherits_rather_than_blanking_the_frame()
    {
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "Category level problem.",
                "painPoints": "Category pain.",
                "automationToPitch": "Category automation.",
                "perTool": { "bill.com": { "coreProblem": "", "painPoints": "", "automationToPitch": "" } }
              }
            }
            """;

        var framing = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill");

        // Handing the writer an empty frame is worse than handing it the category's -- it would have to
        // invent around the hole, which is the behaviour this whole field exists to remove.
        Assert.Equal("Category level problem.", framing!.CoreProblem);
    }

    [Fact]
    public void No_framing_in_the_brief_is_null_not_an_empty_frame()
    {
        Assert.Null(GccNicheFramingReader.ForCategory("""{"angle":"problem_solution"}"""));
        Assert.Null(GccNicheFramingReader.ForCategory(null));
        Assert.Null(GccNicheFramingReader.ForCategory("not json at all"));
        // All three present but blank is not framing.
        Assert.Null(GccNicheFramingReader.ForCategory(
            """{"nicheFraming":{"coreProblem":"  ","painPoints":"","automationToPitch":null}}"""));
    }

    [Theory]
    [InlineData("Accounting -> Cash Flow Forecasting -> Accounts Receivable")]
    [InlineData("Accounting > Cash Flow Forecasting > Accounts Receivable")]
    [InlineData("Accounting › Cash Flow Forecasting › Accounts Receivable")]
    public void The_taxonomy_path_reads_in_every_separator_the_research_uses(string path)
    {
        var brief =
            "{\"nicheFraming\":{\"taxonomyPath\":\"" + path + "\",\"coreProblem\":\"x\"}}";

        var parts = GccNicheFramingReader.TaxonomyPath(brief);

        Assert.Equal(["Accounting", "Cash Flow Forecasting", "Accounts Receivable"], parts);
    }

    [Fact]
    public void The_taxonomy_paths_first_level_is_a_department_slug()
    {
        // Jeff, 2026-10-02: the research template lines up with the departmental tool directory. This is
        // what makes Department derivable instead of a field nobody sets -- every live create is
        // "marketing" today, so accounting pages publish to /tools/marketing/.
        var parts = GccNicheFramingReader.TaxonomyPath(CategoryOnlyBrief);

        Assert.Equal("Accounting", parts[0]);
        Assert.Contains(
            parts[0].ToLowerInvariant(),
            GeekAPI.Services.Workflow.Services.Departments.Slugs);
    }

    [Fact]
    public void The_guidance_tells_the_writer_to_argue_from_it_and_never_cite_it()
    {
        // The one way this becomes a citation problem: a writer that cannot tell operator framing from
        // retrieved evidence may attribute it to a source. The instruction travels with the payload
        // rather than sitting in a separate prompt line that one call site could miss.
        var guidance = GccNicheFramingReader.ForCategory(CategoryOnlyBrief)!.ToGuidance();

        Assert.NotNull(guidance);
        Assert.Contains("never cite it", guidance!, StringComparison.Ordinal);
        Assert.Contains("not retrieved evidence", guidance, StringComparison.Ordinal);
        Assert.Contains("Revenue is booked", guidance, StringComparison.Ordinal);
        Assert.Contains("Nobody owns collections.", guidance, StringComparison.Ordinal);
        Assert.Contains("Invoice-to-cash on a schedule", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_is_a_paragraph_not_a_line()
    {
        // Jeff, 2026-10-03: "the data I am inputting is a paragraph" -- and it is. The research states
        // each failure as a lead plus the paragraph explaining it, so splitting on every newline turned
        // one failure into several fragments, none of them substantial enough to argue from.
        var framing = GccNicheFramingReader.ForCategory(
            """
            {"nicheFraming":{"coreProblem":"x","painPoints":"Nobody owns collections. Follow-ups depend on whoever has time that week.\n\nThey expect the ledger to collect. It sends a reminder and nothing else."}}
            """);

        Assert.Equal(2, framing!.PainPoints.Count);
        Assert.StartsWith("Nobody owns collections.", framing.PainPoints[0], StringComparison.Ordinal);
        Assert.Contains("whoever has time that week", framing.PainPoints[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Soft_wrapped_lines_inside_one_paragraph_stay_one_failure()
    {
        // A textarea wraps, and an operator pasting from a document brings hard line breaks with it.
        // Those are one failure, not three, and they are joined into prose rather than left as
        // fragments -- the writer is given something to argue from.
        var framing = GccNicheFramingReader.ForCategory(
            """
            {"nicheFraming":{"coreProblem":"x","painPoints":"Nobody owns collections.\nFollow-ups depend on whoever\nhas time that week."}}
            """);

        var only = Assert.Single(framing!.PainPoints);
        Assert.Equal(
            "Nobody owns collections. Follow-ups depend on whoever has time that week.",
            only);
    }

    [Fact]
    public void A_single_unbroken_paragraph_is_one_failure()
    {
        // Jeff's real Bill.com data, 2026-10-03: "They all do not come formatted in that way." The
        // research states this one as a single paragraph with no list and no blank line, so the reader
        // must take it whole. Under a line split this became three fragments, and the middle one
        // ("That creates slow approvals, late fees...") means nothing on its own.
        var framing = GccNicheFramingReader.ForCategory(
            """
            {"nicheFraming":{"coreProblem":"x","painPoints":"They treat approval as an email reply, a verbal instruction, or access to the company bank account. That creates slow approvals, late fees, duplicate payments, weak separation of duties, and no defensible approval history. The bookkeeper gets blamed for payment delays but has no authority to move invoices through the process."}}
            """);

        var only = Assert.Single(framing!.PainPoints);
        Assert.StartsWith("They treat approval", only, StringComparison.Ordinal);
        Assert.EndsWith("through the process.", only, StringComparison.Ordinal);
    }

    [Fact]
    public void An_array_is_still_accepted_one_entry_per_failure()
    {
        var framing = GccNicheFramingReader.ForCategory(
            """{"nicheFraming":{"coreProblem":"x","painPoints":["one","two","three"]}}""");

        Assert.Equal(["one", "two", "three"], framing!.PainPoints);
    }
}
