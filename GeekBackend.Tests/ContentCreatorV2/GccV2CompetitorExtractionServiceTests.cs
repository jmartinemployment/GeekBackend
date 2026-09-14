using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2CompetitorExtractionServiceTests
{
    [Fact]
    public void ExtractFromPages_empty_returns_empty()
    {
        var doc = GccV2CompetitorExtractionService.ExtractFromPages([]);
        Assert.Equal(GccCompetitorExtractionDocument.CurrentExtractorVersion, doc.ExtractorVersion);
        Assert.Empty(doc.PricingCatalog);
        Assert.Empty(doc.DeficitRouter);
    }

    [Fact]
    public void ExtractFromPages_stamps_competitors_crawlType_and_builds_specific_assets()
    {
        var page = new GccQuoteablePage(
            "https://rival.example/pricing",
            "Competitor Legacy Writer",
            [
                new HeadingDto(1, "Best AP automation for enterprise"),
                new HeadingDto(2, "Implementation timeline"),
                new HeadingDto(2, "Pricing"),
            ],
            [
                "Unlike Tool Z, we are the #1 marketing suite for large teams.",
                "Enterprise plan is $99.00 monthly. Book a demo at https://rival.example/demo",
                "Completely lacks a native C# SDK and seat pricing is restrictive.",
                "Built for enterprise marketing orgs. Not for solopreneurs.",
                "As of 2022 we were named leader. Always guaranteed results.",
            ],
            PageId: "comp-page-1",
            RunId: Guid.NewGuid().ToString("D"),
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

        var doc = GccV2CompetitorExtractionService.ExtractFromPages(
            [page],
            ["Partner AI Writer Pro"],
            ["https://rival.example/pricing"]);

        Assert.Contains(doc.PricingCatalog, p => p.ListPrice == 99.00m);
        Assert.All(doc.PricingCatalog, p =>
            Assert.Equal(GccCompetitorExtractionDocument.CrawlTypeCompetitor, p.Provenance.CrawlType));
        Assert.Contains(doc.DeficitRouter, d =>
            d.RecommendedSwap.Contains("Partner AI Writer Pro")
            && d.TriggerDeficit.Contains("C# SDK", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doc.TypeLabels, t => t.CompetitorType is "direct" or "both");
        Assert.Contains(doc.FramingBank, f => f.FrameExcerpt.Contains("Unlike", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doc.ClaimRiskFlags, c =>
            c.RiskKind is "superlative" or "absolute"
            && c.WriteGuidance == "do_not_echo_as_fact");
        Assert.Contains(doc.GapMap, g => g.GapTopic.Contains("Implementation", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(doc.ComparisonAxes);
        Assert.NotEmpty(doc.DemandSignals);

        var node = GccV2CompetitorSoftwareApplicationJsonLd.TryBuild(doc, [page]);
        Assert.NotNull(node);
        Assert.Equal("SoftwareApplication", node["@type"]);
        Assert.Equal("Competitor Legacy Writer", node["name"]);
        Assert.True(node.ContainsKey("url"));
        GccV2CompetitorSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, doc);
    }

    [Fact]
    public void MergeCompetitorExtractionIntoBriefJson_round_trips()
    {
        var page = new GccQuoteablePage(
            "https://rival.example",
            "Rival",
            [],
            ["Enterprise is $49.00 monthly and lacks SSO on lower tiers."],
            RunId: "run-1");
        var extraction = GccV2CompetitorExtractionService.ExtractFromPages([page], ["Partner X"]);
        var brief = GccV2PartnerUrlResearchService.MergeCompetitorExtractionIntoBriefJson(
            "{\"title\":\"t\"}", extraction);
        Assert.NotNull(brief);
        var parsed = GccV2PartnerUrlResearchService.ParseCompetitorExtraction(brief);
        Assert.NotNull(parsed);
        Assert.Equal(extraction.ExtractorVersion, parsed.ExtractorVersion);
        Assert.NotEmpty(parsed.PricingCatalog);
    }

    [Fact]
    public void EnsureShipReadyOrThrow_rejects_price_without_catalog()
    {
        var empty = GccV2CompetitorExtractionService.EmptyDocument();
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = "X",
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["price"] = "99.00",
                ["priceCurrency"] = "USD",
            },
        };
        Assert.Throws<InvalidOperationException>(() =>
            GccV2CompetitorSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, empty));
    }
}
