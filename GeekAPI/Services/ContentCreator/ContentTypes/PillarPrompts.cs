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
    /// The coverage does not change page to page; the headings must.
    ///
    /// Every slot declares the same depth, because a pillar's sections are even. It used to declare
    /// none, on the reasoning that depth is only worth stating where sections differ -- but the slot's
    /// lower figure is also what a batch is held to when it comes back
    /// (<c>GccGenerateService.BatchFloorWords</c>), so a pillar had no floor at all: the 2:32 PM pillar
    /// of 2026-10-05 was 2,006 words against 3,000 with nothing retried and nothing reported, while
    /// the blog and tool pages beside it were each told and each listed.
    /// </summary>
    private static readonly string SectionDepth =
        $"{ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words";

    private static readonly SectionSlot[] Sections =
    [
        SectionSlot.Cover(
            "the opening: what this reader is dealing with, told concretely, and what this page settles for them",
            SectionDepth),
        SectionSlot.Cover(
            "what is actually going wrong in this work today and what the status quo costs -- hours, errors, delay, risk, and who absorbs them",
            SectionDepth),
        SectionSlot.Cover(
            "how the approach works end to end: the mechanics, in the order they happen, specific enough that a reader could describe it back",
            SectionDepth),
        SectionSlot.Cover(
            "what separates an implementation that holds up from one that stalls -- the decisions that are made early and cannot be unmade",
            SectionDepth),
        SectionSlot.Cover(
            "what rolling this out actually involves in a real environment: sequence, data, integration, the people whose work changes",
            SectionDepth),
        SectionSlot.Cover(
            "when this is the right call and when it is not, what the reader should do next, and what they should be able to expect",
            SectionDepth),
    ];

    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) => Outline(ctx.NicheFraming);

    /// <summary>
    /// The six obligations, each guided by the part of the operator's framing that answers it: the
    /// opening by the whole framing, "what is going wrong" by the operator's failures, "how the approach
    /// works" by the operator's automation, and the later sections held to that same approach.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-06 the pillar never received the framing at all -- only the tool page did (its
    /// opening slot, since 2026-10-02) -- so the slots were filled from retrieved vendor prose and
    /// competitor coverage and every pillar argued a methodology of the model's own. Jeff, 2026-10-06:
    /// "It is again inventing its own Methodology versus using mine?" The coverage is unchanged; what
    /// each section argues from is now the operator's. A brief with no framing gets the slots as before.
    /// </remarks>
    public static IReadOnlyList<SectionSlot> Outline(GccNicheFraming? niche)
    {
        if (niche is null || !niche.HasAny) return Sections;

        var approach = niche.ApproachGuidance();
        var pointer = niche.ApproachPointer();
        return
        [
            Sections[0] with { Guidance = niche.ToGuidance() },
            Sections[1] with { Guidance = niche.FailuresGuidance() },
            Sections[2] with { Guidance = approach },
            Sections[3] with { Guidance = pointer },
            Sections[4] with { Guidance = pointer },
            Sections[5] with { Guidance = pointer },
        ];
    }

    /// <summary>
    /// Returns the lede AND the introduction section -- BuildPillarLedePrompt asks for
    /// LedeAndIntroductionJsonContract, so it must be read with ParseLedeAndIntroduction. Reading
    /// it as a sections array failed every pillar generation until 2026-09-23.
    /// </summary>
    private static ArticleMetadataDraft Meta(ContentTypePromptContext ctx) =>
        ctx.Metadata ?? throw new InvalidOperationException("A pillar page needs ArticleMetadataDraft.");

    public ChatCompletionRequest Lede(ContentTypePromptContext ctx)
    {
        var outline = OutlineFor(ctx);
        return prompts.BuildPillarLedePrompt(
            ctx.Context,
            Meta(ctx),
            ledeHeading: outline[0].Label,
            ledeIndex: 0,
            totalSections: outline.Count,
            fullOutline: outline,
            isRegeneration: false,
            evidenceBlock: ctx.EvidenceBlock);
    }

    /// <summary>Outline minus the lede slot: the lede already wrote Outline[0].</summary>
    public ChatCompletionRequest Body(ContentTypePromptContext ctx)
    {
        var outline = OutlineFor(ctx);
        return prompts.BuildArticleSectionBatchPrompt(
            ctx.Context,
            Meta(ctx),
            slots: ctx.SectionBatch ?? [.. outline.Skip(1)],
            fullOutline: outline,
            isRegeneration: ctx.RevisionNotes is { Length: > 0 },
            revisionNotes: ctx.RevisionNotes,
            requireHeadingProvenance: true,
            evidenceBlock: ctx.EvidenceBlock,
            lede: ctx.Lede,
            batchIndex: ctx.SectionBatchIndex);
    }
}
