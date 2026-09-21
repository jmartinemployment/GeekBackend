using GeekAPI.Services.ContentCreator;
using GeekApplication.Models.GeekCrawler;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The grounding policy: which content types must be able to cite evidence before they may be
/// written. These assert the declared table itself, not a run — the table is the thing that gets
/// edited, and an accidental removal would silently return a type to ungrounded generation.
/// </summary>
public class GccGroundingPolicyTests
{
    [Theory]
    [InlineData("pillar")]
    [InlineData("blog")]
    [InlineData("techarticle")]
    [InlineData("tool")]
    [InlineData("aitool")]
    public void LongFormTypesMustCitePartnerEvidence(string contentType)
    {
        var required = GccGroundingResolver.RequiredFor(contentType);

        Assert.Contains(CrawlTypes.Partner, required);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("linkedin")]
    [InlineData("facebook")]
    [InlineData("imageprompt")]
    public void ShortFormTypesDeclareNoRequirement(string contentType)
    {
        // A social post is not refused for want of a partner crawl, because it never declared one.
        Assert.Empty(GccGroundingResolver.RequiredFor(contentType));
    }

    [Fact]
    public void ContentTypeMatchingIsCaseInsensitive()
    {
        // The controller lowercases before calling, but the table must not depend on that.
        Assert.Equal(
            GccGroundingResolver.RequiredFor("pillar"),
            GccGroundingResolver.RequiredFor("Pillar"));
    }

    [Fact]
    public void AnUnknownTypeIsNeverRefusedForMissingEvidence()
    {
        Assert.Empty(GccGroundingResolver.RequiredFor("some-future-type"));
    }

    [Fact]
    public void NeedNamesTheRoleAndTheTopic()
    {
        var need = GccGroundingResolver.BuildNeed("AI implementation for SMBs", CrawlTypes.Partner);

        Assert.Contains("partner tool research", need);
        Assert.Contains("AI implementation for SMBs", need);
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
}
