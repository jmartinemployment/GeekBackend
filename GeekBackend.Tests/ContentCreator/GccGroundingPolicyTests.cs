using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The grounding policy: which content types must be able to cite evidence before they may be
/// written. These assert the declared table itself, not a run — the table is the thing that gets
/// edited, and an accidental removal would silently return a type to ungrounded generation.
/// </summary>
/// <remarks>
/// Pillar/Blog/TechArticle moved from "must cite" to "declares no requirement" 2026-09-22 (Jeff:
/// "I told you to remove this from all content types" -- citing/quoting a source was never a
/// requirement of any content type). Tool/aiTool are the one exception, and stay required: this
/// table is the actual retrieval step that populates a create's evidence for Tool's own partner
/// extraction, not a redundant citation-format check.
/// </remarks>
public class GccGroundingPolicyTests
{
    [Theory]
    [InlineData("tool")]
    [InlineData("aitool")]
    public void ToolMustCitePartnerEvidence(string contentType)
    {
        var required = GccGroundingResolver.RequiredFor(contentType);

        Assert.Contains(CrawlTypes.Partner, required);
    }

    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("techarticle")]
    [InlineData("email")]
    [InlineData("linkedin")]
    [InlineData("facebook")]
    [InlineData("imageprompt")]
    public void EverythingExceptToolDeclaresNoRequirement(string contentType)
    {
        // A pillar/blog/social post is not refused for want of a partner crawl, because it never
        // declared one -- citing a source is a nice-to-have for these, never a gate.
        Assert.Empty(GccGroundingResolver.RequiredFor(contentType));
    }

    [Fact]
    public void ContentTypeMatchingIsCaseInsensitive()
    {
        // The controller lowercases before calling, but the table must not depend on that.
        Assert.Equal(
            GccGroundingResolver.RequiredFor("tool"),
            GccGroundingResolver.RequiredFor("Tool"));
    }

    [Fact]
    public void AnUnknownTypeIsNeverRefusedForMissingEvidence()
    {
        Assert.Empty(GccGroundingResolver.RequiredFor("some-future-type"));
    }

    [Fact]
    public void ThePartnerNeedIsTheKeywordAndNothingElse()
    {
        // From 2026-10-02 to 2026-10-08 twenty-two fixed words followed the keyword, a stand-in for
        // the problem the operator describes in the brief's niche framing. The index's keyword half
        // matched every one of them -- "cost", "manual", "capability" are on every ERP, pricing and
        // integration page -- and on Melio the second slot went to an accounts-receivable article.
        // Deleted at Jeff's instruction. Every word sent is a search term, so only the keyword is sent.
        var need = GccGroundingResolver.BuildNeed("AI implementation for SMBs", CrawlTypes.Partner);

        Assert.Equal("AI implementation for SMBs", need);
        Assert.DoesNotContain("status-quo", need, StringComparison.Ordinal);
        Assert.DoesNotContain("measured outcomes", need, StringComparison.Ordinal);
        Assert.DoesNotContain("--", need, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectSiteNeedIsTheKeywordToo()
    {
        var need = GccGroundingResolver.BuildNeed(
            "Accounts Payable: Automated Payment Execution", CrawlTypes.ProjectSite);

        Assert.Equal("Automated Payment Execution", need);
    }

    [Fact]
    public void CompetitorNeedIsDifferentiationNotPartnerResearch()
    {
        var need = GccGroundingResolver.BuildNeed("AI implementation", CrawlTypes.Competitors);

        Assert.Contains("competitor differentiation research", need);
        Assert.DoesNotContain("partner tool research", need);
    }

    [Fact]
    public void ALongTopicIsTruncatedSoTheQueryStaysBounded()
    {
        var need = GccGroundingResolver.BuildNeed(new string('x', 500), CrawlTypes.Partner);

        Assert.True(need.Length < 300, $"need was {need.Length} chars");
    }

    [Fact]
    public void RefusalCarriesAReasonAndNoPages()
    {
        var outcome = GccGroundingOutcome.Refuse("no indexed crawl");

        Assert.True(outcome.Refused);
        Assert.Equal("no indexed crawl", outcome.Refusal);
        Assert.Empty(outcome.Pages);
    }

    [Fact]
    public void NotRequiredIsNotARefusal()
    {
        var outcome = GccGroundingOutcome.NotRequired();

        Assert.False(outcome.Refused);
        Assert.Empty(outcome.Pages);
    }

    [Fact]
    public void The_partner_need_never_contradicts_itself()
    {
        // Topic is "descriptor: keyword" and the keyword names the SOLUTION, so interpolating the whole
        // string into a manual-pain frame produced "the problem of doing Accounts Payable: Automated Data
        // Entry & Processing manually" -- automated, manually. A pinned regression: 0a6ec94 shipped it.
        var need = GccGroundingResolver.BuildNeed(
            "Accounts Payable: Automated Data Entry & Processing", CrawlTypes.Partner);

        Assert.Contains("Automated Data Entry & Processing", need, StringComparison.Ordinal);
        Assert.DoesNotContain("Accounts Payable", need, StringComparison.Ordinal);
        Assert.DoesNotContain("Processing manually", need, StringComparison.Ordinal);
    }
}
