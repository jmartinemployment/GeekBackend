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
}
