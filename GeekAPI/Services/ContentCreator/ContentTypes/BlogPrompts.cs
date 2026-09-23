using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Blog: a deep-dive companion article. One outline definition -- this literal was written inline
/// twice in GccGenerateService, in two methods, kept identical by hand.
/// </summary>
public sealed class BlogPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "blog";

    public IReadOnlyList<string> Outline { get; } = ["Overview", "Key considerations", "Next steps"];

    /// <summary>LedeJsonContract -- read with ParseLede, not ParseSections.</summary>
    public ChatCompletionRequest Lede(ContentTypePromptContext ctx) =>
        prompts.BuildStandaloneBlogLedePrompt(ctx.Context, Meta(ctx));

    private static BlogMetadataDraft Meta(ContentTypePromptContext ctx) =>
        ctx.BlogMetadata
        ?? throw new InvalidOperationException("A blog page needs BlogMetadataDraft, not the article shape.");

    public ChatCompletionRequest Body(ContentTypePromptContext ctx) =>
        prompts.BuildStandaloneBlogBodyPrompt(
            ctx.Context,
            Meta(ctx),
            revisionNotes: null,
            requireHeadingProvenance: true,
            evidenceBlock: ctx.EvidenceBlock);
}
