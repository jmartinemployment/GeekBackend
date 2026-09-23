using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Everything that makes one content type that content type: its outline, and how it asks for its
/// lede and body.
///
/// Prompts used to live as a flat type-x-role matrix on one 2,228-line ContentPromptBuilder --
/// BuildToolBodyPrompt next to BuildBlogLedePrompt next to BuildStandaloneBlogMetadataPrompt --
/// with nothing grouping a type's decisions together and the choice of which to call made in a
/// switch elsewhere. Two defects on 2026-09-23 came straight out of that: the Tool writer prompt
/// did not know what a partner is while the extraction prompt feeding it did, and Tool's context
/// builder drifted from Pillar's so Angle for SEO never reached the lede. Neither is a prompt
/// written badly; both are decisions with no home.
///
/// Shared rules stay shared and are composed here, never copied per type -- the 12-type lede
/// guidance, the JSON contracts, brand tone, provenance and no-invention rules all still come from
/// IContentPromptBuilder. A type owns what differs, not what is common.
///
/// See content-creator-v2/plans/prompts-per-content-type.md.
/// </summary>
public interface IContentTypePrompts
{
    /// <summary>Normalised key -- letters only, lowercase, matching the dispatch in GccGenerationCoordinator.</summary>
    string Key { get; }

    /// <summary>
    /// The type's top-level sections, in order. One definition: Blog's was written inline twice and
    /// Tool's existed both as an array and as hardcoded prose inside its body prompt, carrying a
    /// comment that the two had to be kept in sync by hand.
    ///
    /// Takes the context because an outline can depend on the brief. "Overview" is an
    /// angle-agnostic placeholder sitting where the Angle for SEO should decide -- Pillar's prompt
    /// already forbids a generic opener outright, while Tool's mandated one by name.
    /// </summary>
    IReadOnlyList<string> OutlineFor(ContentTypePromptContext ctx);

    ChatCompletionRequest Lede(ContentTypePromptContext ctx);

    ChatCompletionRequest Body(ContentTypePromptContext ctx);
}

/// <summary>
/// What a prompt set needs to build its calls. Types differ in which parts they use -- Tool needs
/// the product and its extracted research, Pillar and Blog need an evidence block for heading
/// provenance -- so this carries the union and each set takes what applies to it.
/// </summary>
public sealed record ContentTypePromptContext(
    ProjectGenerationContext Context,
    ArticleMetadataDraft? Metadata = null,
    /// <summary>Blog's prompts take BlogMetadataDraft rather than ArticleMetadataDraft -- a real
    /// per-type difference, carried here rather than papered over with a conversion.</summary>
    BlogMetadataDraft? BlogMetadata = null,
    string? EvidenceBlock = null,
    SoftwareApplicationDescriptor? App = null,
    string? ToolSlug = null,
    string? ExtractedResearchJson = null);
