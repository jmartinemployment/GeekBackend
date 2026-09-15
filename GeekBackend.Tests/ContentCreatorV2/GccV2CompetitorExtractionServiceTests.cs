using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Shape and separation guarantees for competitor extraction. A competitor is a rival service
/// business, so the document must be incapable of carrying SaaS-product concepts at all — this is
/// enforced by the type, not by filtering after extraction.
/// </summary>
public sealed class GccV2CompetitorExtractionServiceTests
{
    [Fact]
    public void EmptyDocument_is_empty_and_versioned()
    {
        var doc = GccV2CompetitorExtractionService.EmptyDocument();

        Assert.Equal(GccCompetitorExtractionDocument.CurrentExtractorVersion, doc.ExtractorVersion);
        Assert.Equal("gcc-competitor-extraction.v4", doc.ExtractorVersion);
        Assert.Empty(doc.CoverageMap);
        Assert.Empty(doc.GapMap);
        Assert.Empty(doc.ComparisonAxes);
        Assert.Empty(doc.DeficitRouter);
        Assert.Empty(doc.PublishedPricing);
        Assert.Empty(doc.ClaimRiskFlags);
        Assert.Empty(doc.TypeLabels);
        Assert.Empty(doc.ServiceOfferings);
        Assert.Empty(doc.NamedClients);
        Assert.Empty(doc.PositioningStatements);
    }

    /// <summary>
    /// Regression for the headline defect: competitor was a relabelled copy of partner, which gave a
    /// rival agency seat tiers and billing periods. Those concepts must not exist on the type.
    /// </summary>
    [Fact]
    public void Competitor_document_cannot_carry_saas_product_concepts()
    {
        var properties = typeof(GccCompetitorExtractionDocument)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("PricingCatalog", properties);
        Assert.DoesNotContain("Disqualifiers", properties);
        Assert.DoesNotContain("Integrations", properties);

        var priceFields = typeof(GccCompetitorPublishedPriceAsset)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        // Published day rates and retainers are legitimate; subscription mechanics are not.
        Assert.Contains("PriceText", priceFields);
        Assert.Contains("PricingBasis", priceFields);
        Assert.DoesNotContain("BillingPeriod", priceFields);
        Assert.DoesNotContain("OverageTerms", priceFields);
        Assert.DoesNotContain("TierName", priceFields);
        Assert.DoesNotContain("SeatCap", priceFields);
    }

    /// <summary>
    /// Provenance must not be the partner type — sharing it is what allowed a competitor to hydrate
    /// into a partner shape.
    /// </summary>
    [Fact]
    public void Competitor_assets_use_competitor_owned_provenance()
    {
        var provenance = typeof(GccCompetitorClaimRiskAsset)
            .GetProperty(nameof(GccCompetitorClaimRiskAsset.Provenance))!
            .PropertyType;

        Assert.Equal(typeof(GccCompetitorExtractionProvenance), provenance);
        Assert.NotEqual(typeof(GccPartnerExtractionProvenance), provenance);
    }

    /// <summary>Appendix C offsets are required for citation verify.</summary>
    [Fact]
    public void Competitor_provenance_carries_quote_and_offsets()
    {
        var fields = typeof(GccCompetitorExtractionProvenance)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("Quote", fields);
        Assert.Contains("StartChar", fields);
        Assert.Contains("EndChar", fields);
        Assert.Contains("MarkdownVerified", fields);
        Assert.Contains("SourceRights", fields);
    }
}
