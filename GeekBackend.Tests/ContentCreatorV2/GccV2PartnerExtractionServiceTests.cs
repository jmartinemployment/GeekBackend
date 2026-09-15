using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2PartnerExtractionServiceTests
{
    [Fact]
    public void ExtractFromPages_empty_returns_empty_document()
    {
        var doc = GccV2PartnerExtractionService.ExtractFromPages([]);
        Assert.Equal(GccPartnerExtractionDocument.CurrentExtractorVersion, doc.ExtractorVersion);
        Assert.Empty(doc.Citables);
        Assert.Empty(doc.PricingCatalog);
    }

    [Fact]
    public void ExtractFromPages_grounds_core_and_min_expand_assets()
    {
        var page = new GccQuoteablePage(
            "https://partner.example/pricing",
            "Partner AI Writer Pro",
            [new HeadingDto(2, "How do you support SSO?"), new HeadingDto(2, "Use case: bulk ad hooks")],
            [
                "Stop manually writing cold emails — auto-generate ad hooks in seconds.",
                "Tired of hit-or-miss AI generations? Partner AI Writer Pro reduces video rendering time by 40%.",
                "Start free trial today at https://partner.example/trial",
                "Pro plan is $19.00 monthly and includes API access on Pro+.",
                "14-day free trial. Overage is $0.02 / 1k words.",
                "Built for mid-market finance teams. Not for consumer freelancers.",
                "Integrates with Salesforce and ships a native C# SDK.",
                "SOC 2 Type II certified. Max 5 seats on Starter. US-only availability.",
                "SAML SSO on Enterprise.",
                "Does not include a native mobile SDK — limited to web and API workflows.",
                "Best for bulk operations when you need flat-rate seats.",
                "As of January 2026 pricing updated for Pro.",
                "We may earn an affiliate commission when you purchase through our links.",
            ],
            PageId: "page-1",
            RunId: Guid.NewGuid().ToString("D"),
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk,
            CrawledAtUtc: DateTimeOffset.Parse("2026-09-14T12:00:00Z"));

        var doc = GccV2PartnerExtractionService.ExtractFromPages(
            [page],
            ["Partner AI Writer Pro", "Partner Collab"]);

        Assert.Contains(doc.Citables, c => c.IsolatedClaim.Contains("40%", StringComparison.Ordinal));
        Assert.All(doc.Citables, c =>
        {
            Assert.Equal("partner", c.Provenance.CrawlType);
            Assert.Equal(page.Url, c.OriginProofUrl);
            Assert.True(GccV2PartnerExtractionService.IsGrounded(c.IsolatedClaim, string.Join('\n', page.Paragraphs)));
        });

        Assert.NotEmpty(doc.Advertisements);
        Assert.Contains(doc.Advertisements, a =>
            a.MarketingHook.Contains("cold emails", StringComparison.OrdinalIgnoreCase)
            || a.CtaWrapper.Contains("trial", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(doc.PricingCatalog, p => p.ListPrice == 19.00m && p.PriceCurrency == "USD");
        Assert.Contains(doc.OfferCtas, o => o.OfferType == "trial");
        Assert.Contains(doc.Integrations, i =>
            i.IntegrationName.Contains("Salesforce", StringComparison.OrdinalIgnoreCase)
            || (i.ApiOrSdk?.Contains("SDK", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.Contains(doc.ProofPack, p => p.ProofKind == "certification");
        Assert.Contains(doc.Disqualifiers, d => d.LimitType is "seats" or "region" or "feature");
        Assert.Contains(doc.FaqBank, f => f.Question.Contains("SSO", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(doc.Comparisons, c => c.StandardizedFeatureId is "api_access" or "pricing_model" or "seat_limits");
        Assert.Contains(doc.Alternatives, a => a.RecommendedSwap.Contains("Partner Collab"));
        Assert.Contains(doc.Icp, i => i.ServedSegments.Count > 0 || i.ExcludedSegments.Count > 0);
        Assert.NotEmpty(doc.FreshnessLog);
        Assert.NotEmpty(doc.AffiliateDisclosures);
        Assert.NotEmpty(doc.ComplianceSnippets);
        Assert.NotEmpty(doc.UseCasePlaybooks);
    }

    [Fact]
    public void TryBuild_SoftwareApplication_includes_price_only_with_catalog_evidence()
    {
        var page = new GccQuoteablePage(
            "https://partner.example/pricing",
            "Partner AI Writer Pro",
            [],
            [
                "Stop wasting hours on manual token counting—auto-generate ad hooks in seconds.",
                "Pro is $19.00 monthly. Start free trial at https://partner.example/trial",
                "Overage $0.02 / 1k words. Excellent for bulk operations, though it currently lacks a native mobile SDK.",
            ],
            RunId: "run-1",
            RetrievalMode: GccQuoteablePage.RetrievalModeRagChunk);

        var extraction = GccV2PartnerExtractionService.ExtractFromPages([page]);
        var node = GccV2PartnerSoftwareApplicationJsonLd.TryBuild(extraction, [page]);
        Assert.NotNull(node);
        Assert.Equal("SoftwareApplication", node["@type"]);
        Assert.Equal("Partner AI Writer Pro", node["name"]);

        Assert.True(node.ContainsKey("offers"));
        var offer = Assert.IsType<Dictionary<string, object?>>(node["offers"]);
        Assert.Equal("19.00", offer["price"]);
        Assert.Equal("USD", offer["priceCurrency"]);

        GccV2PartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, extraction);
    }

    [Fact]
    public void EnsureShipReadyOrThrow_rejects_price_without_catalog()
    {
        var empty = GccV2PartnerExtractionService.EmptyDocument();
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = "X",
            ["offers"] = new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["price"] = "19.00",
                ["priceCurrency"] = "USD",
            },
        };

        Assert.Throws<InvalidOperationException>(() =>
            GccV2PartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(node, empty));
    }

    [Fact]
    public void MergePartnerExtractionIntoBriefJson_round_trips()
    {
        var page = new GccQuoteablePage(
            "https://partner.example",
            "Tool",
            [],
            ["Maintains 99.9% API uptime for enterprise customers on Pro."],
            RunId: "run-1");
        var extraction = GccV2PartnerExtractionService.ExtractFromPages([page]);
        var brief = GccV2PartnerUrlResearchService.MergePartnerExtractionIntoBriefJson("{\"title\":\"t\"}", extraction);
        Assert.NotNull(brief);
        var parsed = GccV2PartnerUrlResearchService.ParsePartnerExtraction(brief);
        Assert.NotNull(parsed);
        Assert.Equal(extraction.ExtractorVersion, parsed.ExtractorVersion);
        Assert.NotEmpty(parsed.Citables);
    }

    [Fact]
    public void IsGrounded_rejects_invented_claims()
    {
        Assert.False(GccV2PartnerExtractionService.IsGrounded(
            "Invented claim about teleportation",
            "Partner reduces rendering time by 40%."));
        Assert.True(GccV2PartnerExtractionService.IsGrounded(
            "reduces rendering time by 40%",
            "Partner reduces rendering time by 40%."));
    }
}
