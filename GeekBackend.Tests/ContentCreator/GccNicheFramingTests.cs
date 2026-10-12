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
/// and listed five tools beneath. Hence one set for the category and a section for each tool, which
/// adds to the category's and replaces none of it (2026-10-12).
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
            Assert.Null(framing!.Own);
            Assert.Contains("Revenue is booked", framing.Category!.CoreProblem, StringComparison.Ordinal);
            Assert.Equal(3, framing.Category.PainPoints.Count);
            Assert.Equal([framing.Category.CoreProblem], framing.Problems);
        }
    }

    /// <summary>
    /// Jeff, 2026-10-12: "it is not an OR it is both PAIN POINTS". Until then a tool's own problem and
    /// pitch were read in the category's place, so one of the two he entered reached that tool's search
    /// and page.
    /// </summary>
    [Fact]
    public void A_tools_own_section_is_kept_beside_the_categorys_and_replaces_none_of_it()
    {
        var bill = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Bill");
        var dext = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Dext");

        // Both statements of the problem, the tool's own first: what its site is searched with.
        Assert.Equal(["The SMB needs AR now and AP next.", "Category level problem."], bill!.Problems);
        Assert.Equal("Category level problem.", bill.Category!.CoreProblem);
        Assert.Equal("Category automation.", bill.Category.AutomationToPitch);
        Assert.Equal(["Category pain."], bill.Category.PainPoints);
        Assert.Equal("The SMB needs AR now and AP next.", bill.Own!.CoreProblem);
        Assert.Equal("One connected finance workflow from invoice through reconciliation.", bill.Own.AutomationToPitch);
        Assert.Equal(2, bill.Own.PainPoints.Count);

        // Dext has no entry, so it has the category's alone rather than coming back empty.
        Assert.Null(dext!.Own);
        Assert.Equal(["Category level problem."], dext.Problems);
    }

    [Fact]
    public void One_statement_entered_in_both_boxes_is_one_statement()
    {
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "Cash arrives whenever the customer gets round to paying.",
                "automationToPitch": "Invoice-to-cash on a schedule.",
                "perTool": {
                  "bill.com": {
                    "coreProblem": "cash arrives whenever the customer gets round to paying.",
                    "automationToPitch": "Invoice-to-cash on a schedule."
                  }
                }
              }
            }
            """;

        var bill = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill")!;

        Assert.Single(bill.Problems);
        var block = GccPartnerAnswers.ForToolPage(bill, "Bill")!;
        Assert.Contains("The problem: Cash arrives whenever", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Bill's own pain point", block, StringComparison.Ordinal);
        Assert.Contains("What answers it: Invoice-to-cash on a schedule.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("pitch Bill on", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_with_one_box_filled_still_has_everything_the_category_holds()
    {
        // The question that found this: does the backend combine these? It has to. Bill.com owns a
        // distinct problem -- approval treated as an email reply -- while the category's pain points
        // and automation still apply to it.
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

        var block = GccPartnerAnswers.ForToolPage(bill, "Bill")!;
        Assert.Contains("The problem: Cash arrives whenever the customer gets round to paying.", block, StringComparison.Ordinal);
        Assert.Contains("Bill's own pain point: Approval is an email reply", block, StringComparison.Ordinal);
        Assert.Contains("1. Where it goes wrong: Nobody owns collections.", block, StringComparison.Ordinal);
        Assert.Contains("2. Where it goes wrong: They make it hard to pay.", block, StringComparison.Ordinal);
        Assert.Contains("What answers it: Invoice-to-cash on a schedule.", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tools_failures_are_added_to_the_categorys_not_substituted_for_them()
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

        var block = GccPartnerAnswers.ForToolPage(GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill"), "Bill")!;

        // Category first, then the tool's: the shared problem is established before the slice.
        Assert.Contains("1. Where it goes wrong: Approvals stuck with one person.", block, StringComparison.Ordinal);
        Assert.Contains("3. Where it goes wrong: No single invoice-status view.", block, StringComparison.Ordinal);
        Assert.Contains("4. Where it goes wrong: Approval thresholds are unclear above $5,000.", block, StringComparison.Ordinal);
        // The category said what the problem is and the tool did not: one statement, not two.
        Assert.Contains("The problem: Approval lives across", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Bill's own pain point", block, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shared_failure_restated_under_a_tool_is_not_argued_twice()
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

        var block = GccPartnerAnswers.ForToolPage(GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill"), "Bill")!;

        var failures = block.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains($"{GccPartnerAnswers.FailureLabel}: ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
            [
                "1. Where it goes wrong: Approvals stuck with one person.",
                "2. Where it goes wrong: Constant chasing.",
                "3. Where it goes wrong: No mobile approval.",
            ],
            failures);
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
    public void A_tools_section_is_keyed_by_host_not_by_a_typed_name()
    {
        // The catch this exists to prevent. The fan-out buckets by host via
        // GccRequiredToolMentions.HostKeyOf and names products via AnchorLookup, so a brief keyed off a
        // free-typed product name must still resolve to the right slice -- "Bill" is the product name
        // AnchorLookup derives for bill.com, and the tool's section is keyed "bill.com".
        var framing = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Bill");

        Assert.Equal("The SMB needs AR now and AP next.", framing!.Own!.CoreProblem);
        // By the host, whatever its case or a www. in front of it, and for no other host.
        Assert.NotNull(GccNicheFramingReader.OwnForHost(PerToolBrief, "BILL.com"));
        Assert.NotNull(GccNicheFramingReader.OwnForHost(PerToolBrief, GccRequiredToolMentions.HostKeyOf("https://www.bill.com/pricing")));
        Assert.Null(GccNicheFramingReader.OwnForHost(PerToolBrief, "dext.com"));
        Assert.Null(GccNicheFramingReader.OwnForHost(PerToolBrief, ""));
        Assert.Null(GccNicheFramingReader.OwnForHost(null, "bill.com"));
    }

    [Fact]
    public void A_product_this_project_declares_no_partner_for_has_the_categorys_alone()
    {
        var framing = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Notion");

        // Not null and not another tool's section: an unknown product still gets the category's problem,
        // because the problem is the category's regardless of which tool is being written about.
        Assert.Null(framing!.Own);
        Assert.Equal("Category level problem.", framing.Category!.CoreProblem);
    }

    [Fact]
    public void A_section_opened_and_left_blank_is_not_a_section()
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
        Assert.Null(framing!.Own);
        Assert.Equal(["Category level problem."], framing.Problems);
    }

    [Fact]
    public void A_brief_with_nothing_for_the_category_or_the_tool_gives_a_tool_no_framing()
    {
        Assert.Null(GccNicheFramingReader.ForProduct("""{"angle":"problem_solution"}""", PartnerUrls, "Bill"));
        Assert.Null(GccNicheFramingReader.ForHost(null, "bill.com"));
        Assert.Null(GccPartnerAnswers.ForToolPage(null, "Bill"));
    }

    [Fact]
    public void A_tool_pages_hook_is_told_the_problem_and_the_tools_own_pain_point()
    {
        var bill = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Bill")!;
        var dext = GccNicheFramingReader.ForProduct(PerToolBrief, PartnerUrls, "Dext")!;

        var hook = bill.HookGuidance("Bill")!;

        Assert.Contains("The problem: Category level problem.", hook, StringComparison.Ordinal);
        Assert.Contains("Bill's own pain point: The SMB needs AR now and AP next.", hook, StringComparison.Ordinal);
        Assert.Contains("The hook opens on Bill's own pain point and stays inside the problem.", hook, StringComparison.Ordinal);
        // Neither the failures nor either pitch: a hook told to cover them is no longer a hook.
        Assert.DoesNotContain("Category pain.", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Category automation.", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("One connected finance workflow", hook, StringComparison.Ordinal);

        // A tool with nothing of its own is told the category's, worded as every other page's hook is.
        Assert.Equal(GccNicheFramingReader.ForCategory(PerToolBrief)!.HookGuidance(), dext.HookGuidance("Dext"));
    }

    [Fact]
    public void A_tool_with_a_pain_point_and_no_category_problem_has_it_as_the_problem()
    {
        const string brief = """
            { "nicheFraming": { "perTool": { "bill.com": { "coreProblem": "Approval is an email reply." } } } }
            """;

        var bill = GccNicheFramingReader.ForProduct(brief, PartnerUrls, "Bill")!;

        Assert.Null(bill.Category);
        Assert.Equal(["Approval is an email reply."], bill.Problems);
        Assert.EndsWith("The problem: Approval is an email reply.", bill.HookGuidance("Bill")!, StringComparison.Ordinal);
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
    public void A_short_piece_is_given_the_same_framing_and_told_to_build_on_one_failure()
    {
        // An email of 150 words told to cover every failure either ignores the instruction or stops being
        // an email. The problem, the failures and the answer are the same ones the pages argue; only the
        // instruction over the failures differs.
        var framing = GccNicheFramingReader.ForCategory(CategoryOnlyBrief)!;

        var shortForm = framing.ShortFormGuidance()!;

        Assert.Contains("never cite it", shortForm, StringComparison.Ordinal);
        Assert.Contains("Revenue is booked", shortForm, StringComparison.Ordinal);
        Assert.Contains("Nobody owns collections.", shortForm, StringComparison.Ordinal);
        Assert.Contains("Invoice-to-cash on a schedule", shortForm, StringComparison.Ordinal);
        Assert.Contains("build it on the one of these", shortForm, StringComparison.Ordinal);
        Assert.DoesNotContain("Cover these", shortForm, StringComparison.Ordinal);
        Assert.Contains("Cover these in your own prose", framing.ToGuidance()!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hook_is_given_the_problem_and_neither_the_failures_nor_the_answer()
    {
        // A blog's and a tool page's hook is a call of its own, a few sentences long. It is told the
        // problem the operator named so it opens on that one; a hook told to cover every failure is no
        // longer a hook, and those stay on the body section that carries them.
        var framing = GccNicheFramingReader.ForCategory(CategoryOnlyBrief)!;

        var hook = framing.HookGuidance()!;

        Assert.Contains("Revenue is booked", hook, StringComparison.Ordinal);
        Assert.Contains("not retrieved evidence", hook, StringComparison.Ordinal);
        Assert.Contains("never verbatim", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Nobody owns collections.", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoice-to-cash on a schedule", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void A_framing_with_no_problem_stated_gives_a_hook_nothing()
    {
        var framing = GccNicheFramingReader.ForCategory(
            """
            {"nicheFraming":{"painPoints":"Nobody owns collections.","automationToPitch":"Invoice-to-cash on a schedule."}}
            """)!;

        Assert.Null(framing.HookGuidance());
        Assert.NotNull(framing.ToGuidance());
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

    // ----------------------------------------------------------------------------------------------
    // The practical client diagnosis. One question per LINE -- the opposite of pain points, which split
    // on blank lines. Two readers, because the two formats genuinely differ, and these tests are what
    // keeps them from being collapsed into one by someone tidying up.
    // ----------------------------------------------------------------------------------------------

    /// <summary>Jeff's eight for Accounts Payable approval workflows, as he supplied them.</summary>
    private const string DiagnosisBrief = """
        {
          "nicheFraming": {
            "coreProblem": "Approvals happen in email.",
            "diagnosisQuestions": "How many invoices per month require someone's approval?\nWho approves spending, and what happens when they are unavailable?\nHow long does an invoice wait for approval today?\nAre approval limits defined by amount, department or vendor?\nIs there an audit trail sufficient to answer who approved this payment and why?\nHow often does a duplicate or wrong-amount invoice reach payment?\nWho chases an approver who has not responded?\nWhat happens to an invoice when the approver leaves the company?"
          }
        }
        """;

    [Fact]
    public void Eight_question_lines_are_eight_questions()
    {
        // The mutation that matters: point this at ReadParagraphs and the count becomes 1, because there
        // is no blank line anywhere in the set. If that still passes, this test pins nothing.
        var questions = GccNicheFramingReader.DiagnosisQuestions(DiagnosisBrief);

        Assert.Equal(8, questions.Count);
        Assert.Equal("How many invoices per month require someone's approval?", questions[0]);
        Assert.Equal("What happens to an invoice when the approver leaves the company?", questions[7]);
    }

    [Fact]
    public void Blank_lines_between_questions_are_skipped_not_treated_as_grouping()
    {
        // An operator pasting a researched set often leaves gaps. Reading those as structure would invent
        // grouping the closing has no use for, and dropping an empty entry into the prompt would render
        // as a bare "- ".
        var questions = GccNicheFramingReader.DiagnosisQuestions(
            """{"nicheFraming":{"diagnosisQuestions":"First?\n\n\nSecond?\n   \nThird?\n"}}""");

        Assert.Equal(["First?", "Second?", "Third?"], questions);
    }

    [Fact]
    public void The_two_readers_stay_distinct_in_both_directions()
    {
        // Asserted together, on one brief, because the failure is always a swap: whichever reader is
        // reused for both, one of these two numbers is wrong. Eight lines of questions and one paragraph
        // of failure, from the same nicheFraming object.
        const string brief = """
            {
              "nicheFraming": {
                "coreProblem": "x",
                "painPoints": "They treat approval as an email reply. That creates slow approvals, late fees and no defensible approval history.",
                "diagnosisQuestions": "One?\nTwo?\nThree?"
              }
            }
            """;

        Assert.Equal(3, GccNicheFramingReader.DiagnosisQuestions(brief).Count);
        Assert.Single(GccNicheFramingReader.ForCategory(brief)!.PainPoints);
    }

    [Fact]
    public void An_array_of_questions_is_accepted()
    {
        var questions = GccNicheFramingReader.DiagnosisQuestions(
            """{"nicheFraming":{"diagnosisQuestions":["One?","Two?"]}}""");

        Assert.Equal(["One?", "Two?"], questions);
    }

    [Fact]
    public void No_diagnosis_reads_as_none_rather_than_failing()
    {
        // Silent and empty, per the no-exceptions rule: a brief without the field means the closing gets
        // no material, which is what every closing had before this existed.
        Assert.Empty(GccNicheFramingReader.DiagnosisQuestions(CategoryOnlyBrief));
        Assert.Empty(GccNicheFramingReader.DiagnosisQuestions(null));
        Assert.Empty(GccNicheFramingReader.DiagnosisQuestions("not json at all"));
        Assert.Empty(GccNicheFramingReader.DiagnosisQuestions("""{"nicheFraming":{"diagnosisQuestions":"   "}}"""));
    }

    [Fact]
    public void The_diagnosis_is_category_level_and_a_per_tool_entry_cannot_shadow_it()
    {
        // Category-level by construction: the reader looks at the framing root only. Every question is
        // about the reader's own operation and none names a product, so there is nothing for a partner to
        // override -- and a diagnosisQuestions written inside perTool is operator error that must not
        // silently replace the real set.
        var questions = GccNicheFramingReader.DiagnosisQuestions(
            """
            {"nicheFraming":{"diagnosisQuestions":"Category question?","perTool":{"bill.com":{"coreProblem":"y","diagnosisQuestions":"Tool question?"}}}}
            """);

        Assert.Equal(["Category question?"], questions);
    }
}
