using GeekAPI.Services.ContentCreator.Guardrail;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The figure grammar, case by case: what the "a number may appear only if it is in the evidence"
/// check is about, and what it leaves alone.
/// </summary>
public class GccFigureGrammarTests
{
    [Theory]
    [InlineData("Teams close the month 73% faster.", "73")]
    [InlineData("Error rates fell 4.5 percent.", "4.5")]
    [InlineData("Plans start at $19 a month.", "19")]
    [InlineData("It costs €1,200 a year.", "1200")]
    [InlineData("Approvals move 3x faster.", "3")]
    [InlineData("Approvals move 2.5× faster.", "2.5")]
    [InlineData("It saves 40 hours a week.", "40")]
    [InlineData("Approvals take 9 days.", "9")]
    [InlineData("It handled 200 invoices on day one.", "200")]
    [InlineData("Licensed for 12 seats.", "12")]
    [InlineData("Teams process 3,000 a month.", "3000")]
    [InlineData("Over 250 firms use it.", "250")]
    [InlineData("Founded in 2019, it now serves accountants.", "2019")]
    // Not the list's name: no capital, and a word that only ends like one.
    [InlineData("It paid back a fortune 500 times over.", "500")]
    [InlineData("MisFortune 500 was the batch that failed.", "500")]
    public void A_figure_is_found_with_its_value(string text, string value)
    {
        var figures = GccFigureGrammar.Find(text);

        Assert.Contains(figures, f => f.Value == value);
    }

    [Theory]
    [InlineData("This is the 3rd option and the 100th review.")]
    [InlineData("1. Capture the invoice on arrival.")]
    [InlineData("2) Route it for approval.")]
    [InlineData("Step 3 is the approval.")]
    [InlineData("Read the vendor's own page (Melio, 2025).")]
    [InlineData("It runs on v2.1 of the API, or version 3.4, or build 1.2.3.")]
    [InlineData("There are 3 ways to do it, and two partners do it well.")]
    // The name of a list of companies or of a market index, which counts nothing. The 17:47 UTC run of
    // 2026-10-13 refused a tool page for the first of these.
    [InlineData("It is not built for the Fortune 500 with dedicated tax teams.")]
    [InlineData("Fortune 500 customers run it across every entity.")]
    [InlineData("Its parent is listed on the S&P 500 and the Nasdaq-100.")]
    public void Not_a_figure(string text) =>
        Assert.Empty(GccFigureGrammar.Find(text));

    [Fact]
    public void A_year_in_a_link_run_is_a_cite_and_a_year_in_prose_is_a_figure()
    {
        const string text = "Melio pricing page 2025 says so. It launched in 2024.";
        var citeEnd = "Melio pricing page 2025".Length;

        var figures = GccFigureGrammar.Find(text, [(0, citeEnd)]);

        var figure = Assert.Single(figures);
        Assert.Equal("2024", figure.Value);
    }

    [Theory]
    [InlineData("3,000", "3000")]
    [InlineData("19.00", "19")]
    [InlineData("4.50", "4.5")]
    public void Values_compare_without_separators_or_trailing_zeros(string written, string value) =>
        Assert.Equal(value, GccFigureGrammar.Normalize(written));

    [Fact]
    public void Evidence_licenses_by_value()
    {
        var known = GccFigureGrammar.NumbersIn("Melio processed 3000 invoices, from $19.00 a month.");

        Assert.Contains("3000", known);
        Assert.Contains("19", known);
    }

    [Theory]
    [InlineData("100K+ filings on-time 75K+ notices handled 200K+ hours saved", "200000")]
    [InlineData("100K+ filings on-time 75K+ notices handled 200K+ hours saved", "75000")]
    [InlineData("$300M+ sales tax remitted", "300000000")]
    [InlineData("Over 1.5M users", "1500000")]
    [InlineData("Plans from 2.75k seats", "2750")]
    [InlineData("It moved $2bn in payments, then $4B more", "2000000000")]
    [InlineData("It moved $2bn in payments, then $4B more", "4000000000")]
    [InlineData("It processed 3 million invoices", "3000000")]
    [InlineData("A market of 1.2 billion", "1200000000")]
    [InlineData("About 200 thousand filings", "200000")]
    [InlineData("A 5-million-row ledger", "5000000")]
    [InlineData("Trusted with 1,200K records", "1200000")]
    [InlineData("It raised $10m last year", "10000000")]
    [InlineData("Revenue of €1.5m", "1500000")]
    public void A_number_the_evidence_writes_with_a_scale_licenses_the_number_it_stands_for(
        string evidence, string value) =>
        Assert.Contains(value, GccFigureGrammar.NumbersIn(evidence));

    [Theory]
    [InlineData("200K+ hours saved", "200")]
    [InlineData("$300M+ sales tax remitted", "300")]
    [InlineData("It processed 3 million invoices", "3")]
    public void A_scaled_number_still_licenses_its_digits_as_written(string evidence, string value) =>
        Assert.Contains(value, GccFigureGrammar.NumbersIn(evidence));

    /// <summary>
    /// Each line's numbers are licensed as written and nothing more: the letter after the number is a
    /// unit, a label or part of an identifier, not a scale.
    /// </summary>
    [Theory]
    [InlineData("Commit 3K9F fixed it", new[] { "3", "9" })]
    [InlineData("A 5mm margin", new[] { "5" })]
    [InlineData("A desk 5m wide", new[] { "5" })]
    [InlineData("It runs over 802.11b", new[] { "802.11" })]
    [InlineData("Read rule 12b-1 first", new[] { "12", "1" })]
    [InlineData("It took 40 minutes", new[] { "40" })]
    [InlineData("Open from 8am to 5pm", new[] { "8", "5" })]
    public void A_letter_that_is_not_a_scale_adds_nothing(string evidence, string[] asWritten) =>
        Assert.Equal(asWritten.Order(StringComparer.Ordinal), GccFigureGrammar.NumbersIn(evidence).Order(StringComparer.Ordinal));
}
