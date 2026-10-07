using GeekAPI.Services.Workflow.Services;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The opening is this page's first H2 and the writer is asked to write its heading. <c>ParseLede</c> built the
/// section with an empty heading, so the Blog and every tool page opened with no H2 of their own while the pillar
/// (<c>ParseLedeAndIntroduction</c>, which keeps it) did not. Jeff, 2026-10-07, on the Melio tool page: the document's
/// h2 was another heading and the opening had none. The heading was already deserialized and thrown away.
/// </summary>
public sealed class GccLedeHeadingTests
{
    [Fact]
    public void The_blog_and_tool_opening_keeps_the_heading_the_writer_wrote()
    {
        var (lede, _) = LlmResponseJsonParser.ParseLede(
            """{"ledeType":"directAddress","heading":"Eliminate the Chaos in Your Accounts Payable","paragraphs":[{"type":"text","runs":[{"text":"An opening paragraph."}]}]}""",
            "blog lede");

        Assert.Equal("Eliminate the Chaos in Your Accounts Payable", lede.Heading);
        Assert.Equal("h2", lede.Tag);
        Assert.Single(lede.Paragraphs);
    }

    [Fact]
    public void An_opening_with_no_heading_is_still_an_opening_and_carries_none()
    {
        // No heading is not invented. The renderers skip a blank one rather than emit an empty tag.
        var (lede, _) = LlmResponseJsonParser.ParseLede(
            """{"ledeType":"anecdotal","paragraphs":[{"type":"text","runs":[{"text":"An opening paragraph."}]}]}""",
            "tool page 'Ramp' lede");

        Assert.Equal(string.Empty, lede.Heading);
        Assert.Single(lede.Paragraphs);
    }
}
