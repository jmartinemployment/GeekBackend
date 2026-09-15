using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Shape guarantees for partner extraction after the regex extractor was replaced with
/// schema-constrained extraction.
/// </summary>
public sealed class GccV2PartnerExtractionServiceTests
{
    [Fact]
    public void EmptyDocument_is_empty_and_versioned()
    {
        var doc = GccV2PartnerExtractionService.EmptyDocument();

        Assert.Equal(GccPartnerExtractionDocument.CurrentExtractorVersion, doc.ExtractorVersion);
        Assert.Empty(doc.Citables);
        Assert.Empty(doc.CaseStudies);
        Assert.Empty(doc.Testimonials);
        Assert.Empty(doc.Awards);
        Assert.Empty(doc.FeatureInventory);
        Assert.Empty(doc.TechnicalConstraints);
        Assert.Empty(doc.BattlecardSlices);
        Assert.Empty(doc.AffiliateDisclosures);
    }

    /// <summary>
    /// Proof used to be one bucket (ProofKind + ProofClaim free text), so a case study, a testimonial
    /// and a G2 badge were indistinguishable and could not be injected where each is persuasive.
    /// </summary>
    [Fact]
    public void Proof_is_three_addressable_types_not_one_bucket()
    {
        var properties = typeof(GccPartnerExtractionDocument)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("ProofPack", properties);
        Assert.Contains("CaseStudies", properties);
        Assert.Contains("Testimonials", properties);
        Assert.Contains("Awards", properties);
    }

    [Fact]
    public void Case_study_carries_a_named_client_and_an_uncomputed_metric()
    {
        var fields = typeof(GccPartnerCaseStudyAsset).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("ClientName", fields);
        Assert.Contains("MetricName", fields);
        Assert.Contains("MetricValue", fields);
    }

    [Fact]
    public void Award_source_is_normalized_rather_than_free_text()
    {
        var fields = typeof(GccPartnerAwardAsset).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("Source", fields);
    }

    /// <summary>The definitive capability list, distinct from the comparison vector.</summary>
    [Fact]
    public void Feature_inventory_is_distinct_from_the_comparison_vector()
    {
        var feature = typeof(GccPartnerFeatureAsset).GetProperties().Select(p => p.Name).ToList();
        var comparison = typeof(GccPartnerComparisonAsset).GetProperties().Select(p => p.Name).ToList();

        Assert.Contains("FeatureName", feature);
        Assert.Contains("GatedToTier", feature);
        Assert.Contains("StandardizedFeatureId", comparison);
        Assert.DoesNotContain("FeatureName", comparison);
    }

    [Fact]
    public void Technical_constraints_and_unit_cost_are_structured_not_prose()
    {
        var constraint = typeof(GccPartnerTechnicalConstraintAsset).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("ConstraintKind", constraint);
        Assert.Contains("LimitValue", constraint);
        Assert.Contains("LimitUnit", constraint);

        var pricing = typeof(GccPartnerPricingTierAsset).GetProperties().Select(p => p.Name).ToList();
        Assert.Contains("UnitCostAmount", pricing);
        Assert.Contains("UnitCostBasis", pricing);
    }

    [Theory]
    [InlineData("Plooto automates approvals", "Plooto automates approvals for teams", true)]
    [InlineData("Plooto   automates\napprovals", "Plooto automates approvals for teams", true)]
    [InlineData("Plooto cures cancer", "Plooto automates approvals for teams", false)]
    [InlineData("", "anything", false)]
    public void IsGrounded_requires_the_claim_to_appear_in_source(string claim, string source, bool expected)
    {
        Assert.Equal(expected, GccV2PartnerExtractionService.IsGrounded(claim, source));
    }
}
