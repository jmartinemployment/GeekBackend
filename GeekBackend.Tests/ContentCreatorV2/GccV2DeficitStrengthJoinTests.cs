using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// The counterweight join: a competitor deficit may only be paired with a partner strength when both
/// sit on the same axis and each carries its own source chunk id.
/// </summary>
public sealed class GccV2DeficitStrengthJoinTests
{
    private static GccCompetitorExtractionProvenance CompetitorProv(string? pageId) =>
        new("https://rival.example",
            GccCompetitorExtractionDocument.CrawlTypeCompetitor,
            RunId: "run-1", PageId: pageId, SectionTitle: null, SourceDigest: null,
            TemporalAnchorUtc: null);

    private static GccPartnerExtractionProvenance PartnerProv(string? pageId) =>
        new("https://partner.example",
            GccPartnerExtractionDocument.CrawlTypePartner,
            RunId: "run-2", PageId: pageId, SectionTitle: null, SourceDigest: null,
            TemporalAnchorUtc: null);

    private static GccCompetitorExtractionDocument WithDeficit(
        string axisId, string? competitorChunkId) =>
        GccV2CompetitorExtractionService.EmptyDocument() with
        {
            DeficitRouter =
            [
                new GccCompetitorDeficitRouterAsset(
                    axisId,
                    "No localised compliance support",
                    "https://rival.example",
                    [],
                    null,
                    CompetitorDeficitChunkId: competitorChunkId,
                    PartnerStrengthChunkId: null,
                    Provenance: CompetitorProv(competitorChunkId)),
            ],
        };

    private static GccPartnerExtractionDocument PartnerWithStrength(string axisId, string? chunkId)
    {
        var empty = GccV2PartnerExtractionService.EmptyDocument();
        return empty with
        {
            Comparisons =
            [
                new GccPartnerComparisonAsset(
                    axisId, "Built-in compliance guardrails", null, PartnerProv(chunkId)),
            ],
        };
    }

    [Fact]
    public void Binds_when_axis_matches_and_both_chunk_ids_present()
    {
        var bound = GccV2DeficitStrengthJoin.Bind(
            WithDeficit("compliance-automation", "chunk-c1"),
            PartnerWithStrength("compliance-automation", "chunk-p1"),
            ["ApprovalMax"]);

        var joined = GccV2DeficitStrengthJoin.JoinedOnly(bound);
        Assert.Single(joined);
        Assert.Equal("chunk-c1", joined[0].CompetitorDeficitChunkId);
        Assert.Equal("chunk-p1", joined[0].PartnerStrengthChunkId);
        Assert.Contains("ApprovalMax", joined[0].RecommendedSwap);
        Assert.Equal("Built-in compliance guardrails", joined[0].PivotCopy);
    }

    [Fact]
    public void Drops_pair_when_axis_differs()
    {
        var bound = GccV2DeficitStrengthJoin.Bind(
            WithDeficit("compliance-automation", "chunk-c1"),
            PartnerWithStrength("reporting-depth", "chunk-p1"),
            ["ApprovalMax"]);

        Assert.Empty(GccV2DeficitStrengthJoin.JoinedOnly(bound));
        // The deficit is retained unbound rather than deleted or back-filled.
        Assert.Single(bound.DeficitRouter);
        Assert.Empty(bound.DeficitRouter[0].RecommendedSwap);
    }

    [Fact]
    public void Drops_pair_when_competitor_chunk_id_missing()
    {
        var bound = GccV2DeficitStrengthJoin.Bind(
            WithDeficit("compliance-automation", null),
            PartnerWithStrength("compliance-automation", "chunk-p1"),
            ["ApprovalMax"]);

        Assert.Empty(GccV2DeficitStrengthJoin.JoinedOnly(bound));
    }

    [Fact]
    public void Drops_pair_when_partner_chunk_id_missing()
    {
        var bound = GccV2DeficitStrengthJoin.Bind(
            WithDeficit("compliance-automation", "chunk-c1"),
            PartnerWithStrength("compliance-automation", null),
            ["ApprovalMax"]);

        Assert.Empty(GccV2DeficitStrengthJoin.JoinedOnly(bound));
    }

    [Fact]
    public void Provenance_bridge_keeps_competitor_crawl_type()
    {
        var bridged = GccV2DeficitStrengthJoin.ToPartnerProvenance(CompetitorProv("chunk-c1"));

        Assert.Equal(GccCompetitorExtractionDocument.CrawlTypeCompetitor, bridged.CrawlType);
        Assert.Equal("chunk-c1", bridged.PageId);
    }
}
