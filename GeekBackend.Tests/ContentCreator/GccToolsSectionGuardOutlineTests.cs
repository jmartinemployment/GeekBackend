using GeekAPI.Services.ContentCreator.Guardrail;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The tools-section ban applied to the <b>planned outline</b>, before a word is written.
/// </summary>
/// <remarks>
/// Jeff, three times, most recently 2026-10-02: a pillar and a blog do not contain a tools section,
/// period — the tools are named in the solution prose, saying how each helps solve the problem the Angle
/// identifies. It kept recurring because the guard only ran on the written body, and the body writer
/// receives the outline as assigned slots: it wrote the heading it was handed, and the retry re-wrote
/// against the same outline, so no retry could succeed.
/// </remarks>
public class GccToolsSectionGuardOutlineTests
{
    [Theory]
    // The heading from the live refusal, 2026-10-02.
    [InlineData("Choosing the Right AI Tools for Accounts Payable Automation")]
    [InlineData("Choosing the Right Tools")]
    [InlineData("Top Tools for Accounts Payable")]
    [InlineData("Top 5 AP Automation Tools")]
    [InlineData("Best Tools for Invoice Capture")]
    [InlineData("7 Tools to Consider")]
    [InlineData("AP Tools Compared")]
    public void A_planned_heading_that_announces_a_listing_is_caught(string heading)
    {
        Assert.True(GccToolsSectionGuard.IsToolsListingHeadingText(heading), heading);
        Assert.Contains(heading, GccToolsSectionGuard.FindToolsHeadings([heading]));
    }

    [Theory]
    // Prose sections that mention tools. The guard enforces the prompt's list and nothing beyond it --
    // a guard stricter than its prompt refuses work the writer was never told to avoid, and any honest
    // heading for this material contains the word.
    [InlineData("How AI Tools Simplify Your Accounts Payable Process")]
    [InlineData("What Manual Data Entry Really Costs")]
    [InlineData("Why Approval Delays Compound")]
    [InlineData("Automating Invoice Capture End to End")]
    [InlineData("Measuring the Return on Automation")]
    public void A_prose_heading_is_not_a_listing_even_when_it_says_tools(string heading)
    {
        Assert.False(GccToolsSectionGuard.IsToolsListingHeadingText(heading), heading);
        Assert.Empty(GccToolsSectionGuard.FindToolsHeadings([heading]));
    }

    [Fact]
    public void Only_the_offending_headings_come_back()
    {
        var outline = new[]
        {
            "What Manual Data Entry Costs You",
            "Choosing the Right AI Tools for Accounts Payable Automation",
            "How Automation Closes the Gap",
            "Top 5 Tools Compared",
        };

        var found = GccToolsSectionGuard.FindToolsHeadings(outline);

        Assert.Equal(
            ["Choosing the Right AI Tools for Accounts Payable Automation", "Top 5 Tools Compared"],
            found);
    }

    [Fact]
    public void The_retry_instruction_names_each_heading_and_says_where_tools_go()
    {
        var instruction = GccToolsSectionGuard.OutlineRetryInstruction(
            ["Choosing the Right AI Tools for Accounts Payable Automation"]);

        Assert.Contains("Choosing the Right AI Tools for Accounts Payable Automation", instruction, StringComparison.Ordinal);
        // Not just "don't": where they belong instead, or the re-plan has nothing to aim at.
        Assert.Contains("prose of those sections", instruction, StringComparison.Ordinal);
        Assert.Contains("never in a heading", instruction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_a_listing(string? heading)
    {
        Assert.False(GccToolsSectionGuard.IsToolsListingHeadingText(heading));
    }

    [Fact]
    public void An_empty_outline_offends_nothing()
    {
        Assert.Empty(GccToolsSectionGuard.FindToolsHeadings(null));
        Assert.Empty(GccToolsSectionGuard.FindToolsHeadings([]));
    }
}
