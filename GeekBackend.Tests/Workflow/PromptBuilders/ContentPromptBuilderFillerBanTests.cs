using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekBackend.Tests.Workflow.PromptBuilders;

/// <summary>
/// Urgent, per the plan: pillar and blog -- the highest-volume outputs -- generated with no
/// appendix, no filler ban, no Brief, on the live Create path. Assert on the rendered prompt, not
/// the article, per the plan's own verification standard.
/// </summary>
public class ContentPromptBuilderFillerBanTests
{
    private static ProjectGenerationContext Context() => new(
        ProjectName: "Acme",
        ProjectUrl: "https://acme.test",
        TargetKeyword: "ai implementation",
        Department: "marketing",
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

    private static string SystemPrompt(ChatCompletionRequest request) =>
        request.Messages.Single(m => m.Role == ChatRole.System).Content;

    [Fact]
    public void Pillar_body_prompt_bans_ai_filler()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview", "Details"]);

        var request = builder.BuildArticleSectionBatchPrompt(
            Context(), metadata,
            slots: [SectionSlot.Assigned("Details")],
            fullOutline: [SectionSlot.Assigned("Overview"), SectionSlot.Assigned("Details")],
            isRegeneration: false);

        Assert.Contains("Ban filler", SystemPrompt(request));
    }

    [Fact]
    public void Blog_body_prompt_bans_ai_filler()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview", "Details"]);

        var request = builder.BuildStandaloneBlogBodyPrompt(Context(), metadata);

        Assert.Contains("Ban filler", SystemPrompt(request));
    }

    [Fact]
    public void Pillar_body_prompt_carries_populated_brief_fields()
    {
        // The Brief reaching generation was the second half of Urgent. On the live Create path it
        // was dumped as raw JSON only inside GenerateStartingContentAsync -- pillar/blog ignored it
        // entirely. BuildBriefBodyGuidance is now wired via the shared builder; assert its actual
        // rendered content, not just that the prompt is non-empty.
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview"]);
        var context = Context() with
        {
            PrimaryIntent = "commercial_investigation",
            WritingNotes = "SMBs looking to implement AI",
        };

        var request = builder.BuildArticleSectionBatchPrompt(
            context, metadata,
            slots: [SectionSlot.Assigned("Overview")],
            fullOutline: [SectionSlot.Assigned("Overview")],
            isRegeneration: false);

        var system = SystemPrompt(request);
        Assert.Contains("=== BRIEF CONTROLS", system);
        Assert.Contains("Primary intent: commercial_investigation", system);
        Assert.Contains("Writing notes: SMBs looking to implement AI", system);
    }

    [Fact]
    public void Pillar_body_prompt_omits_brief_controls_block_when_the_brief_is_empty()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new ArticleMetadataDraft("Title", "Meta", ["ai"], ["Overview"]);

        var request = builder.BuildArticleSectionBatchPrompt(
            Context(), metadata,
            slots: [SectionSlot.Assigned("Overview")],
            fullOutline: [SectionSlot.Assigned("Overview")],
            isRegeneration: false);

        Assert.DoesNotContain("=== BRIEF CONTROLS", SystemPrompt(request));
    }

    [Fact]
    public void Blog_lede_prompt_gets_real_lede_type_guidance_not_a_hardcoded_creative_only_line()
    {
        // Stage 6: pillar's lede got brief-aware 12-type guidance via BuildLedeTypeGuidance; blog's
        // lede kept a hardcoded "prefer a creative opening" line while still demanding a ledeType
        // value from the same 12-way enum, with nothing telling the model how to choose one.
        var builder = new ContentPromptBuilder();
        var metadata = new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]);

        var request = builder.BuildStandaloneBlogLedePrompt(Context(), metadata);
        var system = SystemPrompt(request);

        Assert.Contains("Lede types (pick ONE ledeType", system);
        Assert.DoesNotContain("Prefer a creative (hook/narrative) opening", system);
    }

    [Fact]
    public void Blog_lede_prompt_actually_responds_to_the_briefs_angle_and_intent()
    {
        var builder = new ContentPromptBuilder();
        var metadata = new BlogMetadataDraft("Title", "Meta", ["ai"], ["Overview"]);
        var context = Context() with
        {
            ContentAngle = "comparative",
            PrimaryIntent = "commercial_investigation",
        };

        var request = builder.BuildStandaloneBlogLedePrompt(context, metadata);
        var system = SystemPrompt(request);

        // The angle has to arrive as an instruction, not a token. "Angle: comparative" told the
        // model a value and left it to guess what to do with it, in a prompt where all twelve lede
        // types carry a line explaining what they are -- which is why the angle visibly failed to
        // shape the writing (Jeff, 2026-09-23).
        Assert.Contains("Comparative", system, StringComparison.Ordinal);
        Assert.Contains("the alternatives this reader is actually weighing", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Angle: comparative", system, StringComparison.Ordinal);
        Assert.Contains("Primary intent: commercial_investigation", system);
    }
}
