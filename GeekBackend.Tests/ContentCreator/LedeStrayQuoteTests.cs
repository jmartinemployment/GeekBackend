using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// One stray character cost a whole tool page. On 2026-10-07 the Stampli lede came back with a quote in front of an
/// object that followed a comma (<c>...supported by "},"{"text":"Stampli",...</c>), the reply did not parse, and the
/// page was lost with "Model did not return a valid lede". The quote carries no content, so it is removed before the
/// parse, the way literal newlines inside strings already are (a named repair in <see cref="JsonReplySanitizer"/>);
/// a reply that is still invalid is refused.
/// </summary>
public sealed class LedeStrayQuoteTests
{
    /// <summary>The reply as the model wrote it, from the run log (Jeff, 2026-10-07).</summary>
    private const string StampliLede = """
        {"ledeType":"anecdotal","heading":"The Invisible Burden of Manual Approval Processes","paragraphs":[{"type":"text","runs":[{"text":"In a small business nestled in the heart of Miami-Dade, the accounts payable team faces a relentless weekly ritual. With stacks of paper invoices piled high, each document is a potential source of error, delay, and frustration."}]},{"type":"text","runs":[{"text":"The problem with manual workflows is not just the errors they breed but the cost they impose. Delays in approval can result in late fees and strained vendor relationships."}]},{"type":"text","runs":[{"text":"Automated approval workflows offer a transformative solution to these challenges. Automated workflows provide a single source of truth, enhancing visibility and control over financial processes, as supported by "},"{"text":"Stampli","href":"https://www.stampli.com/blog/invoice-processing/automated-invoice-approval-workflow/"},{"text":". This shift not only saves time and reduces costs but also allows teams to focus on strategic tasks rather than administrative burdens."}]}]}
        """;

    [Fact]
    public void The_stampli_lede_that_cost_a_page_parses_with_every_paragraph_and_every_word()
    {
        var (lede, ledeType) = LlmResponseJsonParser.ParseLede(StampliLede, "tool page 'Stampli' lede");

        Assert.Equal(LedeType.Anecdotal, ledeType);
        Assert.Equal(3, lede.Paragraphs.Count);
        var last = Assert.IsType<TextParagraph>(lede.Paragraphs[2]);
        Assert.Equal(3, last.Runs.Count);
        Assert.Equal("Stampli", last.Runs[1].Text);
        // The href the writer typed is not its to write (Jeff, 2026-10-10): the words stay, the address goes.
        Assert.All(last.Runs, run => Assert.Null(run.Href));
        Assert.StartsWith(". This shift", last.Runs[2].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_added_the_text_of_the_repaired_reply_is_the_text_the_model_wrote()
    {
        var (lede, _) = LlmResponseJsonParser.ParseLede(StampliLede, "tool page 'Stampli' lede");

        var text = string.Concat(lede.Paragraphs.OfType<TextParagraph>().SelectMany(p => p.Runs).Select(r => r.Text));
        Assert.Contains("as supported by Stampli. This shift not only saves time", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_repair_changes_only_the_pattern_and_leaves_a_valid_reply_alone()
    {
        const string valid = """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"A"},{"text":"B","href":"https://x.test/"}]}]}""";

        Assert.Equal(valid, JsonReplySanitizer.RepairStrayQuoteBeforeObject(valid));
        Assert.Equal(
            """[{"a":1},{"text":"x"}]""",
            JsonReplySanitizer.RepairStrayQuoteBeforeObject("""[{"a":1},"{"text":"x"}]"""));
        Assert.Equal(
            """[{"a":1}, {"text":"x"}]""",
            JsonReplySanitizer.RepairStrayQuoteBeforeObject("""[{"a":1}, "{"text":"x"}]"""));
    }

    [Fact]
    public void A_reply_with_a_different_fault_is_still_refused()
    {
        const string truncated = """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"The reply stops here""";

        var ex = Assert.Throws<ContentGenerationException>(() =>
            LlmResponseJsonParser.ParseLede(truncated, "tool page 'Stampli' lede"));

        Assert.Contains("Model did not return a valid lede", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reply_with_the_stray_quote_but_no_paragraphs_is_still_refused()
    {
        const string empty = """{"ledeType":"anecdotal","paragraphs":[],"extra":[{"a":1},"{"b":2}]}""";

        Assert.Throws<ContentGenerationException>(() => LlmResponseJsonParser.ParseLede(empty, "lede"));
    }
}
