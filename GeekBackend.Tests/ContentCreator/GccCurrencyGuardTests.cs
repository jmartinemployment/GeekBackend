using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Money in a draft is in US dollars or it is not there (Jeff, 2026-10-05: "Any time a currency is
/// listed needs to be in USD"), after a tool page gave ApprovalMax's prices in Australian dollars.
/// </summary>
public sealed class GccCurrencyGuardTests
{
    private const string Scheduler = "#consultationAppointment2xl";

    private static GccGuardInputs Inputs(string evidence) =>
        new(
            new GccHeadingProvenanceEvidence(
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>()),
            [],
            Scheduler,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "geek.test" },
            evidence);

    private static ContentDocument Doc(string sentence) =>
        new(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [
                new Section("h2", "What it costs", [new TextParagraph([new Run(sentence)])], null, [], Provenance: "plan"),
                new Section("h2", "What to do next", [new TextParagraph([new Run("Book a free consultation.", Href: Scheduler)])], null, [], Provenance: "plan"),
            ]);

    private static GccGuardFinding? Currency(string sentence, string evidence) =>
        GccDraftGuard.Pillar(Doc(sentence), Inputs(evidence)).Findings.SingleOrDefault(f => f.Check == "currency");

    [Theory]
    [InlineData("A$39", "AUD")]
    [InlineData("AU$39", "AUD")]
    [InlineData("AUD 39", "AUD")]
    [InlineData("$39 AUD", "AUD")]
    [InlineData("39 AUD", "AUD")]
    [InlineData("39 Australian dollars", "AUD")]
    [InlineData("£12", "GBP")]
    [InlineData("€1,200", "EUR")]
    [InlineData("1,200 euros", "EUR")]
    [InlineData("C$15", "CAD")]
    [InlineData("US$15", "USD")]
    [InlineData("USD 15", "USD")]
    [InlineData("$15 USD", "USD")]
    [InlineData("15 US dollars", "USD")]
    [InlineData("$15", "")]
    public void The_currency_an_amount_is_written_in_is_read_off_the_text(string written, string currency)
    {
        var money = Assert.Single(GccCurrencyGrammar.Find($"Plans start at {written} per user each month."));

        Assert.Equal(currency, money.Currency);
    }

    [Fact]
    public void A_bare_dollar_takes_the_currency_its_own_line_names_and_no_other_lines()
    {
        var found = GccCurrencyGrammar.Find("Standard is $39 per month (billed in AUD).\nRamp starts at $15 per user.");

        Assert.Equal(["AUD", ""], found.Select(m => m.Currency));
    }

    [Theory]
    [InlineData("ApprovalMax starts at A$39 per month.")]
    [InlineData("ApprovalMax starts at 39 AUD per month.")]
    [InlineData("Prices are in AUD, from $39 per month.")]
    [InlineData("The plan is £12 per user.")]
    public void A_draft_that_states_money_in_another_currency_is_refused(string sentence)
    {
        var finding = Currency(sentence, evidence: "Standard A$39 per month. £12 per user.");

        Assert.NotNull(finding);
        Assert.True(finding!.Refuses);
        Assert.Contains("US dollars", finding.Detail);
    }

    [Fact]
    public void A_foreign_price_with_its_currency_dropped_is_refused_as_not_us_dollars()
    {
        // The evidence gives 39 only in Australian dollars; "$39" reads as US dollars and is not.
        var finding = Currency("ApprovalMax starts at $39 per month.", evidence: "Standard plan: AUD 39 per month.");

        Assert.NotNull(finding);
        Assert.Contains("the evidence gives that amount in AUD", finding!.Detail);
    }

    [Theory]
    [InlineData("Ramp starts at $15 per user.", "Ramp: $15 per user per month.")]
    [InlineData("Ramp starts at US$15 per user.", "Ramp: USD 15 per user per month.")]
    // The same number in both currencies, on separate lines of the evidence: the dollar one is a real
    // US price. (On one line, a bare $ beside "A$" is read as Australian -- the cautious reading.)
    [InlineData("The plan is $39 per month.", "US pricing: $39 per month.\nAustralia: A$39 per month.")]
    [InlineData("Approvals fell from nine days to two.", "Standard A$39 per month.")]
    public void Us_dollar_amounts_and_prose_with_no_money_pass(string sentence, string evidence)
    {
        Assert.Null(Currency(sentence, evidence));
    }

    /// <summary>Jeff, 2026-10-05: "Use of other currencies is acceptable in Blockquotes".</summary>
    [Fact]
    public void A_block_quotation_keeps_the_currency_it_was_published_in()
    {
        var quoted = new ContentDocument(
            new Section("h2", "Opening", [new TextParagraph([new Run("An opening.")])], null, []),
            [
                new Section("h2", "In their words",
                    [
                        new TextParagraph([new Run("ApprovalMax describes its entry plan this way.")]),
                        new QuoteParagraph([new Run("Standard starts at A$39 per month.")], Cite: "https://approvalmax.test/pricing"),
                    ],
                    null, [], Provenance: "plan"),
                new Section("h2", "What to do next", [new TextParagraph([new Run("Book a free consultation.", Href: Scheduler)])], null, [], Provenance: "plan"),
            ]);

        var verdict = GccDraftGuard.Tool(quoted, Inputs("Standard starts at A$39 per month."));

        Assert.DoesNotContain("currency", verdict.FailedChecks);
    }

    [Fact]
    public void The_same_check_runs_on_a_blog_and_a_tool_page()
    {
        var doc = Doc("ApprovalMax starts at A$39 per month.");
        var inputs = Inputs("Standard A$39 per month.");

        Assert.Contains("currency", GccDraftGuard.Blog(doc, inputs).FailedChecks);
        Assert.Contains("currency", GccDraftGuard.Tool(doc, inputs).FailedChecks);
    }

    /// <summary>
    /// A partner's extraction, serialized as it is stored: one line, with what is not ASCII escaped.
    /// </summary>
    private const string ExtractionJson =
        """{"pricingCatalog":[{"plan":"Starter","price":"$15 per user per month"},{"plan":"UK","price":"\u00A312 per user"}],"caseStudies":[{"customer":"Paddle Australia","outcome":"estimated yearly savings of $12,400 AUD"}],"pagesAttempted":21}""";

    [Fact]
    public void Json_evidence_is_read_as_the_text_in_it_a_value_to_a_line()
    {
        var text = GccJsonEvidence.TextOf(ExtractionJson);

        Assert.Equal(
            ["Starter", "$15 per user per month", "UK", "£12 per user", "Paddle Australia", "estimated yearly savings of $12,400 AUD", "21"],
            text.Split('\n'));
        // Property names are the schema's words, not the partner's.
        Assert.DoesNotContain("pricingCatalog", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, GccJsonEvidence.TextOf(null));
        Assert.Equal("not json { at all", GccJsonEvidence.TextOf("not json { at all"));
    }

    [Fact]
    public void One_foreign_amount_in_an_extraction_does_not_make_its_dollar_prices_foreign()
    {
        // Read as one line, the whole extraction named AUD once, so "$15" took AUD from "its line"
        // and a page that stated the partner's real US price was refused for it.
        var raw = GccCurrencyGrammar.Find(ExtractionJson);
        var read = GccCurrencyGrammar.Find(GccJsonEvidence.TextOf(ExtractionJson));

        Assert.Equal("AUD", raw.Single(m => m.Value == "15").Currency);
        Assert.True(read.Single(m => m.Value == "15").IsBareDollar);
        Assert.Equal("AUD", read.Single(m => m.Value == "12400").Currency);
        // And the pounds the serializer had escaped are read as pounds.
        Assert.DoesNotContain(raw, m => m.Currency == "GBP");
        Assert.Equal("GBP", read.Single(m => m.Value == "12").Currency);
    }

    [Fact]
    public void The_amounts_the_writer_may_not_state_are_named_one_to_a_line()
    {
        var note = GccCurrencyGrammar.ForeignAmountsInstruction(
            "Standard is $39 per month (billed in AUD).\nRamp starts at $15 per user.\n"
            + GccJsonEvidence.TextOf(ExtractionJson));

        Assert.NotNull(note);
        Assert.StartsWith("=== AMOUNTS THAT ARE NOT IN US DOLLARS -- DO NOT STATE THEM ===", note!, StringComparison.Ordinal);
        // A bare dollar that takes its line's currency is named with it; a US price is not named at all.
        Assert.Contains("- $39 (AUD)" + Environment.NewLine, note, StringComparison.Ordinal);
        Assert.Contains("- £12 (GBP)" + Environment.NewLine, note, StringComparison.Ordinal);
        Assert.Contains("- $12,400 AUD" + Environment.NewLine, note, StringComparison.Ordinal);
        Assert.DoesNotContain("$15", note, StringComparison.Ordinal);
        Assert.Contains("not with the currency left off", note, StringComparison.Ordinal);
        Assert.Contains("A block quotation is the one place", note, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_with_no_foreign_amount_names_nothing()
    {
        Assert.Null(GccCurrencyGrammar.ForeignAmountsInstruction("Ramp starts at $15 per user. US$20 for teams."));
        Assert.Null(GccCurrencyGrammar.ForeignAmountsInstruction(null));
    }

    [Fact]
    public void The_writer_is_told_before_it_writes()
    {
        Assert.StartsWith("MONEY IS IN US DOLLARS ONLY", ContentPromptBuilder.CurrencyInstruction);
        Assert.Contains("never convert it", ContentPromptBuilder.CurrencyInstruction);
    }
}
