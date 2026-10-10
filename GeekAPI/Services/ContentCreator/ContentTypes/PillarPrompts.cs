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
    /// of 2026-10-05 was 2,006 words against 3,000 with nothing reported, while
    /// the blog and tool pages beside it were each told and each listed.
    /// </summary>
    private static readonly string SectionDepth =
        $"{ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words";

    /// <summary>
    /// The opening and ten body sections. There were five body sections until 2026-10-10 (Jeff: "Add more
    /// sections"): a body call writes about 650 words whatever it is asked
    /// (<c>GccGenerateService.MeasuredWordsPerBodyCall</c>), so five sections were three calls and about 2,000
    /// words against a 3,000-word floor. Ten are five calls. Each of the five obligations was split along what
    /// it already said; none is a new topic.
    /// </summary>
    private static readonly SectionSlot[] Sections =
    [
        SectionSlot.Cover(
            "the opening: what this reader is dealing with, told concretely, and what this page settles for them",
            SectionDepth),
        SectionSlot.Cover(
            "what is actually going wrong in this work today",
            SectionDepth),
        SectionSlot.Cover(
            "what the status quo costs -- hours, errors, delay, risk, and who absorbs them",
            SectionDepth),
        // The page's keyword heading belongs here: this is the section about the work once it is automated, so
        // the exact phrase reads true in its heading. The two before it are about the work as it is done today.
        SectionSlot.Cover(
            "how the approach works end to end: the mechanics, in the order they happen, specific enough that a reader could describe it back",
            SectionDepth) with { OwnsKeywordHeading = true },
        SectionSlot.Cover(
            "what separates an implementation that holds up from one that stalls",
            SectionDepth),
        SectionSlot.Cover(
            "the decisions that are made early and cannot be unmade",
            SectionDepth),
        SectionSlot.Cover(
            "what rolling this out actually involves in a real environment: the sequence it happens in",
            SectionDepth),
        SectionSlot.Cover(
            "the data and the integration a real environment has to supply",
            SectionDepth),
        SectionSlot.Cover(
            "the people whose work changes, and what changes for them",
            SectionDepth),
        SectionSlot.Cover(
            "when this is the right call and when it is not",
            SectionDepth),
        SectionSlot.Cover(
            "what the reader should do next, and what they should be able to expect",
            SectionDepth),
    ];

    /// <summary>How many of <see cref="Sections"/> the body calls write: all but the opening, which the lede call writes.</summary>
    internal static int BodySectionCount => Sections.Length - 1;

    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) => Outline(ctx.NicheFraming);

    /// <summary>
    /// The obligations, each guided by the part of the operator's framing that answers it: the opening by the
    /// whole framing, the two "what is going wrong" sections by the operator's failures, "how the approach
    /// works" by the operator's automation, and the later sections held to that same approach.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-06 the pillar never received the framing at all -- only the tool page did (its
    /// opening slot, since 2026-10-02) -- so the slots were filled from retrieved vendor prose and
    /// competitor coverage and every pillar argued a methodology of the model's own. Jeff, 2026-10-06:
    /// "It is again inventing its own Methodology versus using mine?" The coverage is unchanged; what
    /// each section argues from is now the operator's. A brief with no framing gets the slots as before.
    /// <para>
    /// Every body section owes its share of the page's floor (<see cref="SectionSlot.WithOwedWords"/>), so
    /// what the five calls are held to adds up to the floor the page is scored against.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SectionSlot> Outline(GccNicheFraming? niche)
    {
        // The publisher's own method, stated on their site, is what "how the approach works" walks
        // whether or not the brief carries a framing (GccPublisherPositions).
        if (niche is null || !niche.HasAny)
        {
            return WithBodyOwed(
            [
                Sections[0], Sections[1], Sections[2],
                Sections[3] with { Guidance = GccPublisherPositions.ApproachSlotGuidance },
                .. Sections[4..],
            ]);
        }

        var failures = niche.FailuresGuidance();
        var approach = niche.ApproachGuidance();
        var pointer = niche.ApproachPointer();
        return WithBodyOwed(
        [
            Sections[0] with { Guidance = niche.ToGuidance() },
            Sections[1] with { Guidance = failures },
            Sections[2] with { Guidance = failures },
            Sections[3] with { Guidance = $"{approach}{Environment.NewLine}   {GccPublisherPositions.ApproachSlotGuidance}" },
            .. Sections[4..].Select(slot => slot with { Guidance = pointer }),
        ]);
    }

    /// <summary>
    /// The outline with the page's floor divided across its body sections. The opening is written by the lede
    /// call and is not part of a body batch, so it carries no share.
    /// </summary>
    private static IReadOnlyList<SectionSlot> WithBodyOwed(IReadOnlyList<SectionSlot> outline)
    {
        var (pageFloor, _, _) = GccLongFormTypes.GetSeoLengthRules(GccLongFormTypes.Pillar);
        return [outline[0], .. SectionSlot.WithOwedWords([.. outline.Skip(1)], pageFloor)];
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
            isRegeneration: false,
            requireHeadingProvenance: true,
            evidenceBlock: ctx.EvidenceBlock,
            lede: ctx.Lede,
            batchIndex: ctx.SectionBatchIndex,
            writtenSoFar: ctx.WrittenSoFar);
    }
}
