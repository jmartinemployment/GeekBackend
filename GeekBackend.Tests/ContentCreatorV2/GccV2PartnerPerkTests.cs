using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Partner perks are negotiated with the vendor and published nowhere on their site, so they can
/// never be crawled or quote-verified. They must travel as operator-asserted input and must never be
/// mixed into the library-grounded partner payloads.
/// </summary>
public sealed class GccV2PartnerPerkTests
{
    [Fact]
    public void Perk_is_not_part_of_the_library_grounded_partner_extraction_document()
    {
        // Perks cannot be verified against source corpus text, so they must not appear on the
        // extraction document alongside payloads that can.
        var properties = typeof(GeekApplication.Models.ContentCreator.GccPartnerExtractionDocument)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("Perks", properties);
    }
}

/// <summary>
/// The extraction carries what a partner's product does, not the economics of promoting it, and not a
/// change log nobody reads (Jeff, 2026-10-04, D6: drop both categories outright).
/// </summary>
/// <remarks>
/// The disclosure category was the extraction's half of the partner-program framing -- a partner
/// "promoted for revenue", with a disclosure regime to capture -- and the freshness category was
/// counted by the tool page gate while no prompt read it, so it could help pass a gate without
/// grounding a sentence. Matched by substring so a renamed copy of either fails here too.
/// </remarks>
public sealed class GccV2PartnerExtractionDroppedCategoriesTests
{
    [Theory]
    [InlineData("Disclosure")]
    [InlineData("Freshness")]
    public void The_extraction_has_no_such_category(string category)
    {
        var documentProperties = typeof(GeekApplication.Models.ContentCreator.GccPartnerExtractionDocument)
            .GetProperties()
            .Select(p => p.Name);
        var assetTypes = typeof(GeekApplication.Models.ContentCreator.GccPartnerExtractionDocument).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "GeekApplication.Models.ContentCreator"
                && t.Name.StartsWith("GccPartner", StringComparison.Ordinal))
            .Select(t => t.Name);

        Assert.DoesNotContain(documentProperties, n => n.Contains(category, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(assetTypes, n => n.Contains(category, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_extraction_prompt_asks_for_neither_category()
    {
        var schema = GeekAPI.Services.ContentCreatorV2.Generation.GccV2AdHocJsonSchema
            .For<PartnerPageExtraction>(new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            });

        Assert.DoesNotContain("disclosure", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("freshness", schema, StringComparison.OrdinalIgnoreCase);
    }
}
