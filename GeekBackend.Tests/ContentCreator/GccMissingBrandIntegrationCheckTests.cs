using GeekAPI.Services.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Stage 8b, rescoped: "partner domain absent from top N organic results" plus a weak title
/// mention check. Not the original missing-brand-integration ask (does a competitor listicle
/// mention this partner) -- that needed a fetch that was explicitly rejected. This is the honest,
/// computable substitute, and these tests are about proving it is exactly that, not more.
/// </summary>
public class GccMissingBrandIntegrationCheckTests
{
    [Fact]
    public void APartnerRankingInTheTopNIsMarkedPresentWithItsRank()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test/product"],
            organicTitles: ["Other Result", "Acme — Best AI Tool", "Another Result"],
            organicUrls: ["https://other.test", "https://acme.test/landing", "https://third.test"],
            topN: 10);

        var presence = Assert.Single(result);
        Assert.True(presence.DomainInTopNOrganics);
        Assert.Equal(2, presence.OrganicRank);
    }

    [Fact]
    public void APartnerAbsentFromTheTopNIsMarkedAbsentWithNoRank()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test/product"],
            organicTitles: ["Other Result", "Another Result"],
            organicUrls: ["https://other.test", "https://third.test"],
            topN: 10);

        var presence = Assert.Single(result);
        Assert.False(presence.DomainInTopNOrganics);
        Assert.Null(presence.OrganicRank);
    }

    [Fact]
    public void OnlyTheTopNOrganicsCountEvenIfThePartnerRanksLower()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test"],
            organicTitles: ["First", "Second", "Acme shows up here"],
            organicUrls: ["https://a.test", "https://b.test", "https://acme.test/page"],
            topN: 2);

        var presence = Assert.Single(result);
        Assert.False(presence.DomainInTopNOrganics, "acme.test ranks 3rd, outside topN=2");
    }

    [Fact]
    public void ADomainAbsentButNamedInATitleIsMentionedWithoutBeingPresent()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test"],
            organicTitles: ["Why Acme Isn't the Best Choice — A Comparison"],
            organicUrls: ["https://competitor.test/acme-comparison"],
            topN: 10);

        var presence = Assert.Single(result);
        Assert.False(presence.DomainInTopNOrganics, "acme.test itself does not rank");
        Assert.True(presence.MentionedInTitle, "but a competitor's title names it");
    }

    [Fact]
    public void NeitherPresentNorMentionedIsTheGenuineGapCase()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test"],
            organicTitles: ["Totally Unrelated Result"],
            organicUrls: ["https://other.test"],
            topN: 10);

        var presence = Assert.Single(result);
        Assert.False(presence.DomainInTopNOrganics);
        Assert.False(presence.MentionedInTitle);
    }

    [Fact]
    public void WwwPrefixDoesNotDefeatTheDomainMatch()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://www.acme.test/product"],
            organicTitles: ["Acme"],
            organicUrls: ["https://acme.test/landing"],
            topN: 10);

        Assert.True(Assert.Single(result).DomainInTopNOrganics);
    }

    [Fact]
    public void RaggedTitleUrlInputIsTruncatedToTheShorterListRatherThanThrowing()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["https://acme.test"],
            organicTitles: ["Only One Title"],
            organicUrls: ["https://a.test", "https://b.test", "https://c.test"],
            topN: 10);

        Assert.Single(result);
    }

    [Fact]
    public void AnUnparsableePartnerUrlIsSkippedRatherThanThrowing()
    {
        var result = GccMissingBrandIntegrationCheck.Evaluate(
            partnerUrls: ["not-a-url", "https://acme.test"],
            organicTitles: ["Acme"],
            organicUrls: ["https://acme.test"],
            topN: 10);

        var presence = Assert.Single(result);
        Assert.Equal("acme.test", presence.PartnerDomain);
    }
}
