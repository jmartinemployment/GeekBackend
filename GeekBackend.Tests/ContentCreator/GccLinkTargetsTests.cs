using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Workflow.DTOs;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The ids a prompt prints and the ids the placer resolves are one numbering: S# over the quoteable
/// pages in order, T# over the known tools in order, a page without an address keeping its number
/// and being no target.
/// </summary>
public sealed class GccLinkTargetsTests
{
    private static GccQuoteablePage Page(string url, string title = "Page") => new(url, title, [], ["Text."]);

    [Fact]
    public void EvidencePagesAreNumberedInOrderFromOne()
    {
        var targets = GccLinkTargets.For(
            [Page("https://a.test/one", "One"), Page("https://a.test/two", "Two")], tools: null);

        Assert.Equal(["S1", "S2"], targets.All.Select(t => t.Id));
        Assert.True(targets.TryGet("S2", out var second));
        Assert.Equal("https://a.test/two", second.Href);
        Assert.Equal("Two", second.Name);
    }

    [Fact]
    public void ToolsAreNumberedInOrderAndLeadToTheirPublicPath()
    {
        var targets = GccLinkTargets.For(
            evidence: null,
            tools: [new KnownCrawlTool("Chaser", null, "/tools/accounting/chaserhq"), new KnownCrawlTool("Upflow", null, "/tools/accounting/upflow")]);

        Assert.Equal(["T1", "T2"], targets.All.Select(t => t.Id));
        Assert.True(targets.TryGet("T1", out var chaser));
        Assert.Equal("/tools/accounting/chaserhq", chaser.Href);
        Assert.Equal("Chaser", chaser.Name);
    }

    [Fact]
    public void APageWithoutAnAddressKeepsItsNumberAndIsNoTarget()
    {
        // The renderer prints every page, so the second page is still [S2] on screen; a link to it
        // is refused by name rather than resolved to a guess.
        var targets = GccLinkTargets.For(
            [Page("https://a.test/one"), Page("   "), Page("https://a.test/three")], tools: null);

        Assert.Equal(["S1", "S3"], targets.All.Select(t => t.Id));
        Assert.False(targets.TryGet("S2", out _));
    }

    [Fact]
    public void AToolWithoutAPublicPathKeepsItsNumberAndIsNoTarget()
    {
        var targets = GccLinkTargets.For(
            evidence: null,
            tools: [new KnownCrawlTool("Melio", "https://melio.com", PublicPath: null), new KnownCrawlTool("Ramp", null, "/tools/ramp")]);

        Assert.Equal(["T2"], targets.All.Select(t => t.Id));
        Assert.False(targets.TryGet("T1", out _));
    }

    [Fact]
    public void LookupIgnoresCaseAndSpace()
    {
        var targets = GccLinkTargets.For([Page("https://a.test/one")], tools: null);

        Assert.True(targets.TryGet(" s1 ", out var found));
        Assert.Equal("S1", found.Id);
        Assert.False(targets.TryGet(null, out _));
        Assert.False(targets.TryGet("https://a.test/one", out _));
    }

    [Fact]
    public void TheIdsMatchWhatTheRenderersPrint()
    {
        Assert.Equal("S1", GccLinkTargets.EvidenceId(0));
        Assert.Equal("T3", GccLinkTargets.ToolId(2));
        Assert.Empty(GccLinkTargets.None.All);
    }
}
