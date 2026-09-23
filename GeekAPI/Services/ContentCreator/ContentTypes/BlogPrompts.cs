using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Blog: a deep-dive companion article. Unlike Pillar and Tool, a blog's sections are planned per
/// post by BuildStandaloneBlogMetadataPrompt rather than fixed here -- so this type's outline is
/// whatever that call returned, and the constant that used to sit in this file
/// ("Overview / Key considerations / Next steps", written inline twice in GccGenerateService and
/// kept identical by hand) named three sections no blog has been written against since the body
/// began using the planned outline.
/// </summary>
public sealed class BlogPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "blog";

    /// <summary>
    /// The outline the metadata call planned for this post, as assigned slots -- they are already
    /// this post's own headings, written against its title and angle, not a reusable skeleton.
    /// Empty before that call has run, which is the honest answer: a blog has no outline until one
    /// is planned for it.
    /// </summary>
    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) =>
        [.. (ctx.BlogMetadata?.SectionOutline ?? []).Select(SectionSlot.Assigned)];

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
            evidenceBlock: ctx.EvidenceBlock,
            lede: ctx.Lede);
}
