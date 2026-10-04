using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;
using Xunit;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// The tool FAQ prompt does not tell the model its answers are verified, because nothing verifies
/// them.
/// </summary>
/// <remarks>
/// The prompt said "Every answer below is already verified against the partner's own site", labelled
/// each one "Verified answer" and headed the block "VERIFIED PARTNER FAQ". The answers are
/// model-extracted: GccV2PartnerExtractionService builds each GccPartnerFaqAsset straight from the
/// extraction's output, and the only verifier, VerifyAgainstLibraryAsync, is called from the dormant
/// v2 resolver and not from the generate path. A promise the code does not keep is removed the day it
/// is found; the word comes back when the verification does.
/// </remarks>
public class ToolFaqPromptMakesNoVerificationClaimTests
{
    private static readonly ContentPromptBuilder Builder = new();

    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "automated approval workflows",
        Department: "accounting",
        SiteName: "Acme",
        DetectedTone: string.Empty,
        DetectedFocus: string.Empty,
        CrawledHeadings: [],
        CrawledParagraphs: [],
        JsonLdStructuredSummary: null,
        KeywordSources: [],
        PeopleAlsoAskQuestions: [],
        PublisherName: "Geek",
        PublisherLogoUrl: "https://geek.test/logo.png",
        AuthorName: "Author",
        ArticleBaseUrl: "https://geek.test/articles",
        BlogBaseUrl: "https://geek.test/blog",
        ToolBaseUrl: "https://geek.test/tools",
        ImplementerPositioning: "an AI implementation partner",
        Provider: LlmProviderType.OpenAi);

    [Fact]
    public void The_faq_prompt_does_not_call_extracted_answers_verified()
    {
        var request = Builder.BuildToolFaqSectionPrompt(
            Context(),
            new ArticleMetadataDraft("Partner Widget", "Meta", ["ai"], []),
            new SoftwareApplicationDescriptor("Partner Widget", "A widget."),
            [new GccPartnerFaqAsset(
                "Is Partner Widget secure?",
                "Yes, SOC 2 Type II certified.",
                "https://partner.test/faq",
                new GccPartnerExtractionProvenance(
                    "https://partner.test/faq", "partner", null, null, null, null, null))]);

        var prompt = string.Join("\n", request.Messages.Select(m => m.Content));

        // The answer still reaches the model -- only the claim about it is gone.
        Assert.Contains("Yes, SOC 2 Type II certified.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("verified", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
