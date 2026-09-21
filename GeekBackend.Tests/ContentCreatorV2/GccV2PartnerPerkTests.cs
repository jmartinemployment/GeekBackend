using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekBackend.Tests.ContentCreatorV2;

/// <summary>
/// Affiliate perks are negotiated with the vendor and published nowhere on their site, so they can
/// never be crawled or quote-verified. They must travel as operator-asserted input and must never be
/// mixed into the library-grounded partner payloads.
/// </summary>
public sealed class GccV2PartnerPerkTests
{
    [Fact]
    public void Perk_is_parsed_from_operator_tools_and_attached_to_the_matched_partner()
    {
        const string brief = """
        {
          "hierarchyPlan": { "recommendedTools": [ { "name": "ApprovalMax", "href": "https://approvalmax.com" } ] },
          "operatorTools": [
            { "name": "ApprovalMax", "url": "https://approvalmax.com", "perk": "20% off first year with GEEK20" }
          ]
        }
        """;

        var rows = GccV2PartnerUrlResearchService.CollectPartnerToolRows(brief);
        var row = Assert.Single(rows, r => r.Name == "ApprovalMax");

        Assert.Equal("20% off first year with GEEK20", row.Perk);
        Assert.Equal("operator", row.Source);
    }

    [Fact]
    public void Partner_without_a_perk_carries_null_rather_than_an_invented_offer()
    {
        const string brief = """
        {
          "hierarchyPlan": { "recommendedTools": [ { "name": "Plooto", "href": "https://plooto.com" } ] },
          "operatorTools": [ { "name": "Plooto", "url": "https://plooto.com" } ]
        }
        """;

        var rows = GccV2PartnerUrlResearchService.CollectPartnerToolRows(brief);
        var row = Assert.Single(rows, r => r.Name == "Plooto");

        Assert.Null(row.Perk);
    }

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
        Assert.DoesNotContain("AffiliatePerks", properties);
    }
}

/// <summary>
/// Affiliate disclosure must bind to a jurisdiction/policy regime when the source states one.
/// The field previously existed with zero consumers anywhere in the codebase.
/// </summary>
public sealed class GccV2AffiliateDisclosureTests
{
    [Fact]
    public void Disclosure_asset_carries_a_jurisdiction_or_policy()
    {
        var fields = typeof(GeekApplication.Models.ContentCreator.GccPartnerAffiliateDisclosureAsset)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("DisclosureText", fields);
        Assert.Contains("JurisdictionOrPolicy", fields);
    }
}
