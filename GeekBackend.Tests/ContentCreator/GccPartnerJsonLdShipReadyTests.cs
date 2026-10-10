using GeekAPI.Services.ContentCreator.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// Partner SoftwareApplication JSON-LD ships on live tool pages, so its fail-closed gate must stay
/// fail-closed: a published price must be backed by list_price evidence in the pricing catalog.
/// Coverage was lost when the competitor JSON-LD builder was deleted (4e123bd) and is restored here
/// against the partner builder, which is still live via GccV2ToolPageSchemaBuilder.
/// </summary>
public sealed class GccPartnerJsonLdShipReadyTests
{
    private static GccPartnerExtractionProvenance Prov() =>
        new("https://partner.example", GccPartnerExtractionDocument.CrawlTypePartner,
            RunId: "run-1", PageId: "page-1", SectionTitle: null, SourceDigest: null,
            TemporalAnchorUtc: null);

    private static GccPartnerExtractionDocument WithPricing(decimal? listPrice) =>
        GccPartnerExtractionService.EmptyDocument() with
        {
            PricingCatalog =
            [
                new GccPartnerPricingTierAsset(
                    "Pro", listPrice, listPrice is null ? null : "USD", "monthly",
                    FeatureGates: null, FreeOrTrial: null, OverageTerms: null,
                    UnitCostAmount: null, UnitCostBasis: null,
                    OriginProofUrl: "https://partner.example/pricing", Provenance: Prov()),
            ],
        };

    [Fact]
    public void Price_without_list_price_evidence_is_rejected()
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["offers"] = new Dictionary<string, object?> { ["@type"] = "Offer", ["price"] = "49.00" },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            GccPartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, WithPricing(null)));
        Assert.Contains("list_price", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Price_backed_by_list_price_evidence_is_allowed()
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["offers"] = new Dictionary<string, object?> { ["@type"] = "Offer", ["price"] = "49.00" },
        };

        GccPartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, WithPricing(49.00m));
    }

    [Fact]
    public void Offers_must_be_a_structured_offer_object()
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["offers"] = "49.00",
        };

        Assert.Throws<InvalidOperationException>(() =>
            GccPartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, WithPricing(49.00m)));
    }

    [Fact]
    public void Node_without_offers_is_ship_ready()
    {
        var node = new Dictionary<string, object?> { ["@type"] = "SoftwareApplication" };

        GccPartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, WithPricing(null));
    }
}
