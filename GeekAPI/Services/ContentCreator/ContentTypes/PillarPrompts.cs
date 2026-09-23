using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>Pillar: an exhaustive macro-level hub. Its lede IS its first H2.</summary>
public sealed class PillarPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "pillar";

    /// <summary>
    /// What a pillar owes its reader, in order. These were the literal H2s until 2026-09-23 --
    /// "Overview / Why it matters now / How it works / What to evaluate / Implementation path /
    /// When it is the right call" -- so every pillar page this tool has ever produced carried the
    /// same six headings regardless of subject, and "Overview" opened all of them.
    ///
    /// The coverage does not change page to page; the headings must. Depth is stated per slot only
    /// where the sections are genuinely uneven, which for a pillar they are not.
    /// </summary>
    private static readonly SectionSlot[] Sections =
    [
        SectionSlot.Cover(
            "the opening: what this reader is dealing with, told concretely, and what this page settles for them"),
        SectionSlot.Cover(
            "what is actually going wrong in this work today and what the status quo costs -- hours, errors, delay, risk, and who absorbs them"),
        SectionSlot.Cover(
            "how the approach works end to end: the mechanics, in the order they happen, specific enough that a reader could describe it back"),
        SectionSlot.Cover(
            "what separates an implementation that holds up from one that stalls -- the decisions that are made early and cannot be unmade"),
        SectionSlot.Cover(
            "what rolling this out actually involves in a real environment: sequence, data, integration, the people whose work changes"),
        SectionSlot.Cover(
            "when this is the right call and when it is not, what the reader should do next, and what they should be able to expect"),
    ];

    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) => Sections;

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
            ledeHeading: Sections[0].Label,
            ledeIndex: 0,
            totalSections: Sections.Length,
            fullOutline: Sections,
            isRegeneration: false);

    /// <summary>Outline minus the lede slot: the lede already wrote Outline[0].</summary>
    public ChatCompletionRequest Body(ContentTypePromptContext ctx) =>
        prompts.BuildArticleSectionBatchPrompt(
            ctx.Context,
            Meta(ctx),
            slots: [.. Sections.Skip(1)],
            fullOutline: Sections,
            isRegeneration: false,
            revisionNotes: null,
            requireHeadingProvenance: true,
            evidenceBlock: ctx.EvidenceBlock,
            lede: ctx.Lede);
}
