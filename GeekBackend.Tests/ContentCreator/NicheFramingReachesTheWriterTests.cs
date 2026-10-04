using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekApplication.Interfaces.ContentWriterV3;
using Xunit;

namespace GeekBackend.Tests.ContentCreator;

/// <summary>
/// The operator's niche framing reaches the tool body prompt — asserted on the prompt the provider was
/// actually handed, not on the reader in isolation.
/// </summary>
/// <remarks>
/// <see cref="GccNicheFramingTests"/> pins that the brief parses. This pins that the parsed value
/// survives six hops to the model: <c>GenerateToolPageAsync</c> resolves it →
/// <c>ContentTypePromptContext.NicheFraming</c> → <c>ToolPrompts.OutlineFor</c> → <c>Outline</c> →
/// <c>Opening</c>'s <c>SectionSlot.Guidance</c> → <c>BuildToolBodyPrompt</c>'s slot renderer. A reader
/// that works and a wiring that does not is indistinguishable from the operator's side: a missing
/// <c>Guidance</c> is null, and null is what it was before.
/// </remarks>
public class NicheFramingReachesTheWriterTests
{
    private const string BriefWithFraming = """
        {
          "primaryIntent": "commercial",
          "buyingStage": "evaluation",
          "audienceSegment": "smb",
          "audienceNotes": "Accounts payable leads at 20-200 person firms.",
          "angle": "problem_solution",
          "ctaType": "demo",
          "toneOfVoice": "plain",
          "eeatSignals": ["practitioner experience"],
          "lengthBand": "long",
          "nicheFraming": {
            "taxonomyPath": "Accounting -> Cash Flow Forecasting -> Accounts Payable",
            "coreProblem": "Invoices are keyed by hand twice and reconciled a third time.",
            "painPoints": "Nobody owns the inbox.\nApprovals stall with one person on holiday.",
            "automationToPitch": "Capture, code and route every invoice on arrival."
          }
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

    private static async Task<string> ToolPromptsAsync(string? briefJson)
    {
        var fixtures = GccToolPageFanOutFixture.Build(
            GroundablePage, ["https://dext.com"], pagesPerPartner: 2,
            draftable: true, briefJson: briefJson);

        await fixtures.Service.GenerateToolPagesPerPartnerAsync(
            fixtures.Create, null, ContentGeneratorProvider.OpenAi, CancellationToken.None);

        Assert.NotEmpty(fixtures.Calls.Prompts);
        return string.Join("\n\n", fixtures.Calls.Prompts);
    }

    [Fact]
    public async Task The_operators_problem_statement_arrives_in_the_tool_body_prompt()
    {
        var prompts = await ToolPromptsAsync(BriefWithFraming);

        Assert.Contains("Invoices are keyed by hand twice", prompts, StringComparison.Ordinal);
        Assert.Contains("Nobody owns the inbox.", prompts, StringComparison.Ordinal);
        Assert.Contains("Capture, code and route every invoice on arrival.", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task It_arrives_labelled_as_framing_rather_than_as_evidence()
    {
        // Without the label the writer cannot tell this from a retrieved quote and may attribute it to a
        // source. That is the one way operator framing becomes a citation defect.
        var prompts = await ToolPromptsAsync(BriefWithFraming);

        Assert.Contains("never cite it", prompts, StringComparison.Ordinal);
        Assert.Contains("not retrieved evidence", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_brief_with_no_framing_leaves_the_prompt_exactly_as_it_was()
    {
        // The additive guarantee: this fills a null field and changes nothing else. A create with no
        // framing must produce the same obligation it always did.
        var prompts = await ToolPromptsAsync(briefJson: null);

        Assert.DoesNotContain("never cite it", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("THE OPERATOR'S OWN FRAMING", prompts, StringComparison.Ordinal);
        // ...and the slot itself is still there, unchanged.
        Assert.Contains("the problem this reader has with", prompts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_partner_program_or_commission_vocabulary_can_reach_a_prompt()
    {
        // Jeff, 2026-10-02: "Partner program text has no place in my output." There is no field for it,
        // so this is a guard against one being added later and wired in by reflex. A brief that tries to
        // smuggle one through must not surface it.
        const string briefWithProgramText = """
            {
              "primaryIntent": "commercial",
              "buyingStage": "evaluation",
              "audienceSegment": "smb",
              "audienceNotes": "AP leads.",
              "angle": "problem_solution",
              "ctaType": "demo",
              "toneOfVoice": "plain",
              "eeatSignals": ["practitioner experience"],
              "lengthBand": "long",
              "nicheFraming": {
                "coreProblem": "Manual keying.",
                "painPoints": "Nobody owns the inbox.",
                "automationToPitch": "Capture on arrival.",
                "partnerProgram": "$500 per referral, capped at $599 annually, paid via PartnerStack"
              }
            }
            """;

        var prompts = await ToolPromptsAsync(briefWithProgramText);

        Assert.Contains("Manual keying.", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("$500 per referral", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("$599", prompts, StringComparison.Ordinal);
        Assert.DoesNotContain("PartnerStack", prompts, StringComparison.Ordinal);
    }
}
