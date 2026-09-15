using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Competitor;
using GeekAPI.Services.ContentCreatorV2.Plan;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekBackend.Tests.ContentCreatorV2;

public sealed class GccV2CompetitorExtractionCompletionGatesTests
{
    [Fact]
    public void ClaimRiskGate_flags_echoed_superlative()
    {
        var extraction = GccV2CompetitorExtractionService.EmptyDocument() with
        {
            ClaimRiskFlags =
            [
                new GccCompetitorClaimRiskAsset(
                    "we are the #1 marketing suite for large teams",
                    "superlative",
                    "https://rival.example",
                    "do_not_echo_as_fact",
                    new GccCompetitorExtractionProvenance(
                        "https://rival.example",
                        GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                        null, null, null, null, null)),
            ],
        };

        var section = new Section(
            "h2",
            "Market position",
            [new TextParagraph([new Run(
                "Buyers should know we are the #1 marketing suite for large teams according to rivals.")])],
            null,
            []);
        var output = new GccV2WriteOutput
        {
            Title = "t",
            MetaDescription = null,
            Lede = new GccV2WriteSection("lede", "Lede", "problem", section, false),
            Sections = [],
        };

        var gaps = GccV2CompetitorClaimRiskGate.CollectGaps(output, extraction);
        Assert.Contains(gaps, g => g.Contains("claim-risk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TypePlanRouting_filters_content_only_from_product_entities()
    {
        var extraction = GccV2CompetitorExtractionService.EmptyDocument() with
        {
            TypeLabels =
            [
                new GccCompetitorTypeLabelAsset(
                    "content",
                    "editorial",
                    "Forbes Advisor",
                    "https://forbes.example",
                    new GccCompetitorExtractionProvenance(
                        "https://forbes.example",
                        GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                        null, null, null, null, null)),
                new GccCompetitorTypeLabelAsset(
                    "direct",
                    "sells product",
                    "Rival Co",
                    "https://rival.example",
                    new GccCompetitorExtractionProvenance(
                        "https://rival.example",
                        GccCompetitorExtractionDocument.CrawlTypeCompetitor,
                        null, null, null, null, null)),
            ],
        };

        var route = GccV2CompetitorTypePlanRouting.Route(extraction);
        Assert.Contains("Forbes Advisor", route.ContentOnlyRivalNames);
        Assert.Contains("Rival Co", route.ProductRivalNames);
        var filtered = GccV2CompetitorTypePlanRouting.FilterProductEntities(
            ["Forbes Advisor", "Rival Co", "Our Partner"],
            route.ContentOnlyRivalNames);
        Assert.DoesNotContain(filtered, e => e == "Forbes Advisor");
        Assert.Contains(filtered, e => e == "Rival Co");
        Assert.True(GccV2PlanService.IsContentOnlyRivalHeading("Forbes Advisor", route.ContentOnlyRivalNames));
        Assert.False(GccV2PlanService.IsContentOnlyRivalHeading("Rival Co", route.ContentOnlyRivalNames));
    }
}
