using System.Text.Json;
using GeekAPI.Services.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Every line a tool page ships with names the tool it is about.
/// </summary>
/// <remarks>
/// A run writes one tool page per partner and lists every page's lines together under "tool:". A
/// call's word line named its page ("Tool page 'Bill' sections 1-2 is ..."); the page's own lines did
/// not. The run of 2026-10-10 listed "The tool page uses ... (0.36%)" four times and "The page carries
/// no block quotation" once, and which of five pages each was about could be read only off the run log.
/// </remarks>
public sealed class GccToolPageLinesNameTheirPageTests
{
    [Fact]
    public void A_line_that_does_not_name_its_page_is_given_the_name_and_one_that_does_is_left()
    {
        const string page = "Tool page 'Bill'";

        Assert.Equal(
            "Tool page 'Bill': The tool page uses \"Automated Accounts Receivable\" 15 times in 4,173 words (0.36%).",
            GccGenerateService.NamedFor(page, "The tool page uses \"Automated Accounts Receivable\" 15 times in 4,173 words (0.36%)."));
        Assert.Equal(
            "Tool page 'Bill': The page carries no block quotation.",
            GccGenerateService.NamedFor(page, "The page carries no block quotation."));
        Assert.Equal(
            "Tool page 'Bill' sections 1-2 is 512 words against a 600-word floor.",
            GccGenerateService.NamedFor(page, "Tool page 'Bill' sections 1-2 is 512 words against a 600-word floor."));
    }

    [Fact]
    public async Task Every_line_a_written_tool_page_ships_with_names_the_tool()
    {
        var (tool, saved) = await BriefFieldReachTests.SavedToolPageAsync();

        using var envelope = JsonDocument.Parse(saved);
        var warnings = envelope.RootElement.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();

        // The scripted page is a few hundred words: its length, its calls and its keyword are all listed.
        Assert.Contains(warnings, w => w.Contains("Its SEO score needs 3,000.", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("word floor", StringComparison.Ordinal));
        Assert.All(warnings, w => Assert.StartsWith($"Tool page '{tool}'", w, StringComparison.Ordinal));
    }
}
