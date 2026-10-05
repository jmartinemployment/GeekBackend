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
        Assert.Contains("never convert it", finding.RetryInstruction);
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

    [Fact]
    public void The_same_check_runs_on_a_blog_and_a_tool_page()
    {
        var doc = Doc("ApprovalMax starts at A$39 per month.");
        var inputs = Inputs("Standard A$39 per month.");

        Assert.Contains("currency", GccDraftGuard.Blog(doc, inputs).FailedChecks);
        Assert.Contains("currency", GccDraftGuard.Tool(doc, inputs).FailedChecks);
    }

    [Fact]
    public void The_writer_is_told_before_it_writes()
    {
        Assert.StartsWith("MONEY IS IN US DOLLARS ONLY", ContentPromptBuilder.CurrencyInstruction);
        Assert.Contains("never convert it", ContentPromptBuilder.CurrencyInstruction);
    }
}
