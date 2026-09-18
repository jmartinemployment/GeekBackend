using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Rag;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2PartnerExtractionCompletionTests
{
    [Fact]
    public void FindQuoteOffsets_and_digest_round_trip()
    {
        const string md = "# Partner\n\nReduces video rendering time by 40% for teams.\n";
        var (start, end) = GccV2PartnerExtractionVerify.FindQuoteOffsets(
            md, "Reduces video rendering time by 40%");
        Assert.NotNull(start);
        Assert.NotNull(end);
        Assert.Equal("Reduces video rendering time by 40%", md[start!.Value..end!.Value]);
        Assert.Equal(64, GccV2PartnerExtractionVerify.ComputeSourceDigest(md).Length);
    }

    [Fact]
    public void AlternativesJoin_keeps_competitor_crawlType_on_deficit()
    {
        var extraction = GccV2PartnerExtractionService.EmptyDocument();
        var competitor = new GccQuoteablePage(
            "https://rival.example/pricing",
            "Rival Tool",
            [],
            ["Rival lacks a native C# SDK and seat pricing is restrictive for teams."],
            PageId: "comp-1",
            RunId: Guid.NewGuid().ToString("D"));

        var joined = GccV2PartnerAlternativesJoin.EnrichWithCompetitorDeficits(
            extraction,
            [competitor],
            ["Partner AI Writer Pro"]);

        Assert.Contains(joined.Alternatives, a =>
            a.RecommendedSwap.Contains("Partner AI Writer Pro")
            && string.Equals(
                a.Provenance.CrawlType,
                GccPartnerExtractionDocument.CrawlTypeCompetitors,
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(joined.Alternatives, a =>
            string.Equals(a.Provenance.CrawlType, "partner", StringComparison.OrdinalIgnoreCase)
            && a.TriggerDeficit.Contains("lacks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CitableBridge_attaches_markdown_verified_citable_for_partner_mention()
    {
        var runId = Guid.NewGuid().ToString("D");
        var citable = new GccPartnerCitableAsset(
            "Maintains 99.9% API uptime",
            "https://partner.example/docs",
            new GccPartnerExtractionProvenance(
                "https://partner.example/docs",
                GccPartnerExtractionDocument.CrawlTypePartner,
                runId,
                "page-1",
                "Uptime",
                "digest",
                DateTimeOffset.UtcNow,
                Quote: "Maintains 99.9% API uptime",
                StartChar: 0,
                EndChar: 26,
                SourceRights: GccV2SourceRightsGate.Consented,
                QuoteVerified: true));

        var extraction = GccV2PartnerExtractionService.EmptyDocument() with
        {
            Citables = [citable],
        };

        var section = new Section(
            "h2",
            "Why teams pick Partner AI Writer Pro",
            [new TextParagraph([new Run("Partner AI Writer Pro helps ops teams ship faster.")])],
            null,
            []);
        var output = new GccV2WriteOutput
        {
            Title = "t",
            MetaDescription = null,
            Lede = new GccV2WriteSection("lede", "Lede", "problem", section, false),
            Sections = [],
        };

        var tokens = new[]
        {
            new GccV2PartnerMentionGate.PartnerToken("Partner AI Writer Pro", ["partner ai writer pro"]),
        };
        var bridged = GccV2PartnerCitableBridge.AttachVerifiedCitables(output, extraction, tokens);
        Assert.NotNull(bridged.Lede.Citations);
        Assert.Contains(bridged.Lede.Citations!, c =>
            c.Verified == true
            && c.CrawlType == "partner"
            && c.Quote.Contains("99.9%", StringComparison.Ordinal));
    }

    [Fact]
    public void PartnerMentionGate_passes_when_bridged_citable_present()
    {
        var runId = Guid.NewGuid().ToString("D");
        var citation = new RagCitationDto
        {
            PageId = "page-1",
            RunId = runId,
            Url = "https://partner.example/docs",
            Quote = "Maintains 99.9% API uptime",
            CrawlType = "partner",
            Verified = true,
            SourceRights = GccV2SourceRightsGate.Consented,
            SectionKey = "lede",
        };
        var section = new Section(
            "h2",
            "Why teams pick Partner AI Writer Pro",
            [new TextParagraph([new Run("Partner AI Writer Pro helps ops teams ship faster.")])],
            null,
            []);
        var output = new GccV2WriteOutput
        {
            Title = "t",
            MetaDescription = null,
            Lede = new GccV2WriteSection("lede", "Lede", "problem", section, false, [citation]),
            Sections = [],
        };
        var tokens = new[]
        {
            new GccV2PartnerMentionGate.PartnerToken("Partner AI Writer Pro", ["partner ai writer pro"]),
        };
        Assert.Empty(GccV2PartnerMentionGate.CollectGaps(output, tokens));
    }
}
