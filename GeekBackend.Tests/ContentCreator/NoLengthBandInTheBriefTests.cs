using GeekAPI.Controllers.ContentCreator;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The brief carries no length band (decision J6): no path may require one.
/// </summary>
/// <remarks>
/// 2026-10-05, the first Generate from the project page: every tool page was refused "brief required:
/// missing lengthBand", and the pillar and blog written beside them were discarded with it. The
/// project route had stopped requiring the field and strips it from what the writer is given; the tool
/// page path checks the brief again inside and still demanded it. The route's own test passed, because
/// it never reached that second check.
/// </remarks>
public sealed class NoLengthBandInTheBriefTests
{
    /// <summary>A complete brief as the project page saves it: every required field, no lengthBand.</summary>
    private const string BriefWithoutLengthBand = """
        {
          "primaryIntent": "commercial",
          "buyingStage": "evaluation",
          "audienceSegment": "smb",
          "audienceNotes": "Accounts payable leads at 20-200 person firms.",
          "angle": "problem_solution",
          "ctaType": "demo",
          "toneOfVoice": "plain",
          "eeatSignals": ["practitioner experience"]
        }
        """;

    private static readonly PartnerPageExtraction GroundablePage = new(
        Citables: null, Advertisements: null, Comparisons: null, Alternatives: null, Pricing: null,
        Icp: null,
        Integrations: [new PartnerIntegrationItem("Xero", "accounting", null, null)],
        Faqs: [new PartnerFaqItem("Does it capture line items?", "Yes.", null)],
        CaseStudies: null, Testimonials: null, Awards: null,
        FeatureInventory: [new PartnerFeatureItem("Invoice data capture", "automation", null, null)],
        TechnicalConstraints: null, OfferCtas: null, Disqualifiers: null, UseCasePlaybooks: null,
        Categories: null, BattlecardSlices: null, DemoBeats: null,
        ComplianceSnippets: null);

    [Fact]
    public void The_brief_gate_does_not_ask_for_a_length_band()
    {
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2, draftable: true, briefJson: BriefWithoutLengthBand);

        GccGenerateService.ValidateBriefRequired(fixtures.Create);
    }

    /// <summary>The path that failed: the tool pages, written from the brief the project route hands on.</summary>
    [Fact]
    public async Task A_tool_page_is_written_from_a_brief_with_no_length_band()
    {
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2, draftable: true, briefJson: BriefWithoutLengthBand);
        // What the project route does to the brief before the run: any lengthBand is removed.
        var create = fixtures.Create with { BriefJson = GccProjectsController.WithoutLengthBand(fixtures.Create.BriefJson) };

        var outcomes = await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.All(outcomes, o => Assert.DoesNotContain("lengthBand", o.Refusal ?? string.Empty));
        // It reached the writer: the gate that refused every tool page sits before any prompt is built.
        Assert.NotEmpty(fixtures.Calls.Prompts);
    }
}
