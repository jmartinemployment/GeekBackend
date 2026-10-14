using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// "PDF / LinkedIn document": a deck of slides and the LinkedIn post that carries it
/// (<c>plans/enable-pdf.md</c>). Written from the Brief and the research like the Pillar, never from
/// another page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two requests.</b> <see cref="Lede"/> writes the cover and the post; <see cref="Body"/> writes
/// every slide between the cover and the last. The last slide, what to do next, is built by code from
/// the publisher's booking words and the Brief's questions and appears in no request's assignment.
/// </para>
/// <para>
/// <b>The outline is the partners.</b> A deck has a slide for each partner tool, so
/// <see cref="OutlineFor"/> reads <see cref="ContentTypePromptContext.Partners"/> and the same Brief
/// gives the same slides every time. Whether the project has a number of partners a deck can be
/// written for is asked before any request is built (<see cref="GccLinkedInDocument.PartnerRefusal"/>).
/// </para>
/// <para>
/// <b>What the operator entered reaches both requests.</b> The framing of the problem for every
/// partner is printed in both (<see cref="GccNicheFraming.DeckGuidance"/>), and so is what was entered
/// under each partner (<see cref="ContentTypePromptContext.PartnerAnswers"/>). A slide's entry sends
/// the writer to the one it is built on by that block's own heading, and only when the request
/// carries the block.
/// </para>
/// </remarks>
public sealed class LinkedInDocumentPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => GccChannelTypes.LinkedInDocument;

    /// <summary>
    /// The slides the body request writes, in order: the problem, one for each partner, the approach.
    /// The cover is the opening and is not in this list; the last slide is built by code and is in no
    /// list a writer is assigned from.
    /// </summary>
    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) =>
        Outline(ctx.Partners ?? [], ctx.NicheFraming, PartnerAnswersShown(ctx));

    /// <summary>The same definition, reachable without a prompt-set instance.</summary>
    /// <param name="partners">The project's partner tools, as the writer is told to spell them.</param>
    /// <param name="niche">The operator's framing for a page about every partner, or null.</param>
    /// <param name="partnerAnswersShown">Whether the request carries what the operator entered under
    /// each partner. A slide is sent to that block only when it does.</param>
    public static IReadOnlyList<SectionSlot> Outline(
        IReadOnlyList<string> partners, GccNicheFraming? niche, bool partnerAnswersShown) =>
        [.. GccLinkedInDocument.WrittenAfterTheCover(partners).Select(slide => slide.Kind switch
        {
            GccDeckSlideKind.Problem => SectionSlot.Cover(
                Describe(slide), guidance: ProblemGuidance(niche, partnerAnswersShown))
                with { ResearchLabel = GccResearchLabel.Problem },
            GccDeckSlideKind.Approach => SectionSlot.Cover(
                Describe(slide), guidance: ApproachGuidance(niche))
                with { ResearchLabel = GccResearchLabel.HowItWorks },
            _ => SectionSlot.Cover(
                Describe(slide), guidance: PartnerGuidance(slide.Partner ?? string.Empty, partnerAnswersShown)),
        })];

    /// <summary>
    /// What one slide is for, in a line. The body request's assignment prints it for the slides it
    /// writes, and the cover request lists every slide by it for context.
    /// </summary>
    public static string Describe(GccDeckSlidePlan slide) => slide.Kind switch
    {
        GccDeckSlideKind.Cover => "the cover: the failure this deck is about, as the reader lives it",
        GccDeckSlideKind.Problem =>
            "what goes wrong in this work today and what it costs this reader, before any tool is named",
        GccDeckSlideKind.Partner => $"{slide.Partner}: the failure it answers and what is done about it",
        GccDeckSlideKind.Approach => "how the pieces fit, end to end, in the order they happen",
        _ => "what to do next: the publisher's own booking words and questions (built by code, written by no request)",
    };

    /// <summary>Where the slide on what goes wrong takes its lines from.</summary>
    private static string ProblemGuidance(GccNicheFraming? niche, bool partnerAnswersShown)
    {
        const string noPartner = "It names no partner tool: each has a slide of its own.";
        if (niche is { PainPoints.Count: > 0 })
        {
            return $"Its lines come from the failures listed in {GccNicheFraming.FramingHeading}. {noPartner}";
        }

        if (niche is { FailuresAreUnderThePartners: true } && partnerAnswersShown)
        {
            return $"Its lines come from the \"{GccPartnerAnswers.FailureLabel}\" lines in {GccPartnerAnswers.Heading}: "
                + "the operator entered the failures under the partner that answers each, and failures that say "
                + "the same thing under different partners are one failure. It names no failure the operator did "
                + $"not. {noPartner}";
        }

        return $"Its lines come from what the passages in the research describe going wrong. {noPartner}";
    }

    /// <summary>What one partner's slide is built on.</summary>
    private static string PartnerGuidance(string partner, bool partnerAnswersShown)
    {
        var named = $"Name {partner} in the lines, spelled exactly so, and never in the title.";
        return partnerAnswersShown
            ? $"What the operator entered for {partner} is under its name in {GccPartnerAnswers.Heading}, where "
                + $"anything was entered. The slide is built on a failure listed there, or on {partner}'s own problem "
                + $"where no failure is, and says what {partner} does about it only as {partner}'s own passages in the "
                + $"research show it. With nothing listed under {partner}, the slide is built on what its passages "
                + $"show it doing about this deck's problem. {named}"
            : $"The slide is built on what {partner}'s own passages in the research show it doing about this deck's "
                + $"problem, and claims nothing they do not show. {named}";
    }

    /// <summary>Whose approach the slide on how the pieces fit describes.</summary>
    private static string ApproachGuidance(GccNicheFraming? niche) =>
        string.IsNullOrWhiteSpace(niche?.AutomationToPitch)
            ? GccPublisherPositions.DeckApproachSlotGuidance
            : $"The approach is the operator's, stated as \"{GccNicheFraming.AnswerLabel}\" in "
                + $"{GccNicheFraming.FramingHeading}: not a generic one and not a vendor's. "
                + GccPublisherPositions.DeckApproachSlotGuidance;

    private static bool PartnerAnswersShown(ContentTypePromptContext ctx) =>
        !string.IsNullOrWhiteSpace(ctx.PartnerAnswers);

    /// <summary>
    /// A request's evidence with what the operator entered under each partner after it. Added here and
    /// not by the caller, so a request that sends a slide to that block is a request that carries it.
    /// </summary>
    private static string? EvidenceWithPartnerAnswers(ContentTypePromptContext ctx)
    {
        var blocks = new[] { ctx.EvidenceBlock, ctx.PartnerAnswers }
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();
        return blocks.Count == 0 ? null : string.Join(Environment.NewLine, blocks);
    }

    /// <summary>The cover and the LinkedIn post, one request.</summary>
    public ChatCompletionRequest Lede(ContentTypePromptContext ctx)
    {
        var partners = ctx.Partners ?? [];
        var slides = GccLinkedInDocument.Slides(partners, ctx.DeckLastSlides);
        return prompts.BuildLinkedInDocumentCoverPrompt(
            ctx.Context,
            [.. slides.Select(Describe)],
            partnerSlides: slides.Count(slide => slide.Kind == GccDeckSlideKind.Partner),
            operatorsFraming: ctx.NicheFraming?.DeckGuidance(),
            evidenceBlock: EvidenceWithPartnerAnswers(ctx));
    }

    /// <summary>Every slide between the cover and the last, one request.</summary>
    public ChatCompletionRequest Body(ContentTypePromptContext ctx) =>
        prompts.BuildLinkedInDocumentSlidesPrompt(
            ctx.Context,
            slides: ctx.SectionBatch ?? OutlineFor(ctx),
            slideCount: GccLinkedInDocument.Slides(ctx.Partners ?? [], ctx.DeckLastSlides).Count,
            operatorsFraming: ctx.NicheFraming?.DeckGuidance(),
            evidenceBlock: EvidenceWithPartnerAnswers(ctx),
            cover: GccLinkedInDocument.CoverOf(ctx.Lede));
}
