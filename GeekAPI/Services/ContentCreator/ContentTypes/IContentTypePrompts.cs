using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;

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
    /// The type's top-level sections, in order, as obligations rather than titles -- what each
    /// section owes the reader, with the writer naming it. One definition: Blog's was written
    /// inline twice and Tool's existed three times over, as an array here, a literal in
    /// GccGenerateService and hardcoded prose inside its body prompt, carrying a comment that the
    /// copies had to be kept in sync by hand.
    ///
    /// Takes the context because an outline depends on the brief -- the Angle for SEO decides what
    /// the page opens on.
    ///
    /// These were fixed heading strings until 2026-09-23, which meant every pillar and every tool
    /// page shipped with byte-identical H2s. Jeff: "Headings are lame and I would bet repeated on
    /// every single blog post", "The headings reflect why content word count is so drastically
    /// low", and "I really don't want to see Overview again, on any content type. Overview is a
    /// type of Lede." See <see cref="SectionSlot"/> for why the obligation survives and the title
    /// does not.
    /// </summary>
    IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx);

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
    string? ExtractedResearchJson = null,
    /// <summary>
    /// The spans a tool page may quote, which is the same list GccToolQuoteGuard checks the draft
    /// against. Both have to read one list: the writer was shown the extraction JSON and told to
    /// quote "a testimonial or an isolated claim" from it, so a partner whose extraction filed
    /// nothing under those headings left it with nothing verbatim to use -- while the guard held
    /// spans cut from the retrieved pages it had never seen.
    /// </summary>
    IReadOnlyList<GccQuoteCandidate>? QuoteCandidates = null,
    /// <summary>The opening already written for this page. The body prompts could not see it, so a
    /// page that opened as anecdote switched to reference voice at the first H2 (Jeff, 2026-09-23:
    /// "While it starts off nice with a story, it becomes dull and a chore to read
    /// afterward").</summary>
    Section? Lede = null,
    /// <summary>What this pass has to change. Set only when revising: a type's body prompt is the
    /// only correct way to rewrite that type, and revise could not reach one without this -- so it
    /// used the blog prompt for everything and rewrote pillars and tool pages to blog length.</summary>
    string? RevisionNotes = null,
    /// <summary>
    /// The sections this call writes, when the body is being generated in batches. Null means the
    /// whole outline in one call, which is what every type did -- and a single call cannot exceed
    /// the model's output ceiling, so a long piece was capped by arithmetic rather than by what it
    /// had to say.
    /// </summary>
    IReadOnlyList<SectionSlot>? SectionBatch = null,
    /// <summary>
    /// Which batch this is, counting from zero. The page's keyword-bearing H2 belongs to exactly one
    /// call -- the scorer wants at least one and the outline rules cap it at two, so asking every
    /// batch for one puts it in half the headings on the page.
    /// </summary>
    int SectionBatchIndex = 0);
