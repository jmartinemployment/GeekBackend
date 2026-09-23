using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>Pillar: an exhaustive macro-level hub. Its lede IS its first H2.</summary>
public sealed class PillarPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "pillar";

    private static readonly string[] Sections =
    [
        "Overview",
        "Why it matters now",
        "How it works",
        "What to evaluate",
        "Implementation path",
        "When it is the right call",
    ];

    public IReadOnlyList<string> OutlineFor(ContentTypePromptContext ctx) => Sections;

    /// <summary>
    /// Returns the lede AND the introduction section -- BuildPillarLedePrompt asks for
    /// LedeAndIntroductionJsonContract, so it must be read with ParseLedeAndIntroduction. Reading
    /// it as a sections array failed every pillar generation until 2026-09-23.
    /// </summary>
    private static ArticleMetadataDraft Meta(ContentTypePromptContext ctx) =>
        ctx.Metadata ?? throw new InvalidOperationException("A pillar page needs ArticleMetadataDraft.");

    public ChatCompletionRequest Lede(ContentTypePromptContext ctx) =>
        prompts.BuildPillarLedePrompt(
            ctx.Context,
            Meta(ctx),
            ledeHeading: Sections[0],
            ledeIndex: 0,
            totalSections: Sections.Length,
            fullOutline: Sections,
            isRegeneration: false);

    /// <summary>Outline minus the lede slot: the lede already wrote Outline[0].</summary>
    public ChatCompletionRequest Body(ContentTypePromptContext ctx) =>
        prompts.BuildArticleSectionBatchPrompt(
            ctx.Context,
            Meta(ctx),
            headings: [.. Sections.Skip(1)],
            fullOutline: Sections,
            isRegeneration: false,
            revisionNotes: null,
            requireHeadingProvenance: true,
            evidenceBlock: ctx.EvidenceBlock);
}
