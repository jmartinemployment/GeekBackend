using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Tool: a partner-grounded page for one product, showing how it addresses the create's keyword.
/// The revenue-critical type -- equal to Pillar in depth, never a thinner treatment.
/// </summary>
public sealed class ToolPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "tool";

    /// <summary>
    /// Also stated as prose inside BuildToolBodyPrompt ("Required top-level (h2) sections, in
    /// order: ..."), which carries a comment that the two must be kept in sync. This is the
    /// definition; that prose is the one that should eventually read from here.
    /// </summary>
    /// <summary>
    /// The opening section is named by the brief's Angle for SEO, not fixed. "Overview" is an
    /// angle-agnostic placeholder, and a page that opens with one has told the reader nothing --
    /// Pillar's own prompt already forbids a generic opener by name while Tool's mandated one
    /// (Jeff, 2026-09-23: "instead of Overview it should be based on Angle for SEO, which 99% of
    /// the time is going to be Problem-Solution").
    ///
    /// The remaining five sections are the type's spine and do not move: a tool page still has to
    /// cover what it does, how it works, what deploying it involves, how to judge it, and who it
    /// suits, whatever angle opens it.
    /// </summary>
    public IReadOnlyList<string> OutlineFor(ContentTypePromptContext ctx) =>
        [Opening(ctx.Context.ContentAngle), .. Spine];

    private static readonly string[] Spine =
    [
        "Key Capabilities",
        "How It Works",
        "Implementation Considerations",
        "Evaluation Criteria",
        "When to Use",
    ];

    /// <summary>
    /// Angle vocabulary is CONTENT_ANGLES in brief-catalog.ts. An unset or unrecognised angle keeps
    /// the neutral opener rather than guessing at one.
    /// </summary>
    private static string Opening(string? angle) =>
        (angle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "problem_solution" => "The Problem It Solves",
            "comparative" => "How It Compares",
            "case_study_data" => "The Evidence",
            "ultimate_guide" => "Overview",
            _ => "Overview",
        };

    /// <summary>
    /// The shared 12-type hook, chosen against this brief's audience, angle, intent and tone --
    /// the same path every other long-form type uses, not a Tool-specific copy. Tool had no lede
    /// call at all until 2026-09-23: its first body section was promoted into the lede slot, so
    /// every page opened with a section headed "Overview".
    ///
    /// Additive, not carved out of the body: all six outline sections survive, the way the FAQ
    /// section is additional. Tool must equal or exceed Pillar in length.
    /// </summary>
    private static ArticleMetadataDraft Meta(ContentTypePromptContext ctx) =>
        ctx.Metadata ?? throw new InvalidOperationException("A tool page needs ArticleMetadataDraft.");

    public ChatCompletionRequest Lede(ContentTypePromptContext ctx) =>
        prompts.BuildArticleLedePrompt(ctx.Context, Meta(ctx));

    public ChatCompletionRequest Body(ContentTypePromptContext ctx) =>
        prompts.BuildToolBodyPrompt(
            ctx.Context,
            Meta(ctx),
            ctx.App ?? throw new InvalidOperationException("A tool page needs the product it is about."),
            ctx.ToolSlug ?? string.Empty,
            revisionNotes: null,
            extractedToolResearchJson: ctx.ExtractedResearchJson);
}
