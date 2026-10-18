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
    /// What a tool page owes a reader weighing this product, in order.
    ///
    /// The opening is named by the brief's Angle for SEO; the remaining five are the type's spine
    /// and do not move -- a tool page still has to cover what the product does, how it works, what
    /// deploying it involves, how to judge it and who it suits, whatever angle opens it.
    ///
    /// What used to be fixed was the wording at the top of each section: "Overview / Key
    /// Capabilities / How It Works / Implementation Considerations / Evaluation Criteria / When to
    /// Use", identical on every tool page, with the same list written out again in
    /// GccGenerateService and a third time as prose inside BuildToolBodyPrompt under a comment
    /// saying the copies had to be kept in sync by hand. The obligation is the thing worth fixing;
    /// the heading is the thing the writer owes this particular product.
    ///
    /// Per-section depth stays here too, because it is genuinely uneven across a tool page -- the
    /// implementation section carries more than the closing one -- and because it belongs beside
    /// the obligation it sizes, not in a second list somewhere else that has to agree with this one.
    /// </summary>
    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx) =>
        Outline(ctx.Context, ctx.App?.Name, ctx.ToolFraming);

    /// <summary>
    /// The same definition, reachable without a prompt-set instance -- the orchestrator's own tool
    /// path (<c>ToolPageGenerator</c>) writes the identical page and must not carry a copy of this
    /// list or, worse, hand the tool body prompt the pillar's planned outline instead.
    /// </summary>
    /// <param name="framing">
    /// The operator's framing for this tool, when the brief carries one: the category's and the
    /// tool's own. Optional so the orchestrator's tool path — which has no brief — keeps calling this
    /// unchanged.
    /// </param>
    public static IReadOnlyList<SectionSlot> Outline(
        ProjectGenerationContext context,
        string? productName,
        GccToolFraming? framing = null)
    {
        var product = productName is { Length: > 0 } n ? n : "this product";
        var publisher = context.PublisherName;
        var keyword = context.TargetKeyword;
        // Ten sections, where there were six until 2026-10-10 (Jeff: "Add more sections"). A body call writes
        // about 650 words whatever it is asked (GccGenerateService.MeasuredWordsPerBodyCall), so six sections
        // were three calls and about 2,000 words against a 3,000-word floor. Each added section is a part of
        // one that was already here, with the part of its guidance that applies, word for word: how it works
        // into mechanics and architecture, deploying into three, judging into two. None is a new topic.
        const string notGeneric = "Not generic industry advice.";
        var (pageFloor, _, _) = GccLongFormTypes.GetSeoLengthRules(GccLongFormTypes.Tool);
        return SectionSlot.WithOwedWords(
        [
            Opening(context.ContentAngle, product, keyword, OpeningGuidance(framing, productName)),
            // The page's keyword heading belongs here: this is the first section about the work once the product
            // does it. The one before it opens on the problem as the reader has it today, by hand, and a
            // heading there cannot carry "Automated ..." without contradicting what the section covers.
            SectionSlot.Cover(
                $"what {product} actually does, stated as what it removes from the reader's week rather than as a feature list",
                "600-850 words",
                $"Every capability lands with its consequence -- hours returned, errors removed, a job that stops needing a person. A capability without one is a spec sheet, and they can already read {product}'s own.")
                with { OwnsKeywordHeading = true },
            SectionSlot.Cover(
                $"how {product} works, its real mechanics step by step",
                "500-700 words",
                $"The platform's own machinery, specific to {product} -- not a restatement of what it does, and not generic SaaS description."),
            SectionSlot.Cover(
                $"how {product} is built, its real architecture, what it connects to and what it holds",
                "500-700 words",
                $"The platform's own machinery, specific to {product} -- not a restatement of what it does, and not generic SaaS description."),
            SectionSlot.Cover(
                $"what going live with {product} involves in a client's existing environment, what shortens it, and the order it happens in",
                "500-700 words",
                $"{notGeneric} Made concrete to {product}: what shortens go-live (pre-built connectors, templated setup, phased rollout)."),
            SectionSlot.Cover(
                $"the data structure and mapping decisions that matter upfront when deploying {product}",
                "500-700 words",
                $"{notGeneric} Made concrete to {product}: what data structure and mapping decisions matter upfront."),
            // Names no feature. It read "approval chains, routing, automation logic", written when every project
            // was accounts payable, and the writer was held to it: on the 13:14 UTC run of 2026-10-11 three cash
            // flow forecasting tools were each given a section headed "Approval Chains, Routing, and Automation
            // Logic". What a product has to configure is in its own pages, and the slot sends the writer there.
            SectionSlot.Cover(
                $"what gets configured in {product}, its own rules and automation logic, and its extension mechanism",
                "500-700 words",
                $"{notGeneric} Made concrete to {product}: which rules and automation logic get configured, named as {product}'s own pages name them and never as another kind of product's; and {product}'s own extension mechanism if it has one (API, scripting, SDK) -- if it is config-only, say so rather than inventing one. This is where the reader's DIY question gets answered: what {publisher} does that makes it work in their environment."),
            SectionSlot.Cover(
                $"how a buyer should judge {product} on fit and on its pricing model",
                "500-700 words",
                "Pricing only where the persisted research carries it; otherwise discuss what to weigh rather than inventing a figure. Never state a specific price, tier or discount that is not in the research."),
            SectionSlot.Cover(
                $"the adjacent approaches a buyer is also weighing, and how {product} sits among them",
                "500-700 words"),
            SectionSlot.Cover(
                $"who {product} suits, who it does not, and what the reader should do next",
                "450-600 words"),
        ],
        pageFloor);
    }

    /// <summary>
    /// The opening section, named by the brief's Angle for SEO. Angle vocabulary is CONTENT_ANGLES
    /// in brief-catalog.ts; an unset or unrecognised angle opens on what the product is for rather
    /// than guessing at an angle.
    ///
    /// There is no "Overview" case any more, for either the recognised angles or the fallback.
    /// Jeff, 2026-09-23: "I really don't want to see Overview again, on any content type. Overview
    /// is a type of Lede." An overview is the summary hook -- it is the opening's job, and the
    /// opening is already written by the shared 12-type lede below.
    /// </summary>
    /// <remarks>
    /// <paramref name="guidance"/> fills this slot's <c>Guidance</c>, which was null until 2026-10-02 —
    /// so the writer invented the problem, its cost and its failure modes on every page. The slot's
    /// three clauses and the operator's three fields are the same three things; this is the operator
    /// answering the question the slot already asked.
    ///
    /// It is attached to the opening on every angle, not only <c>problem_solution</c>. A comparative or
    /// evidence-led page opens differently but is still about the same problem, and the framing says
    /// what that problem is.
    /// </remarks>
    private static SectionSlot Opening(string? angle, string product, string keyword, string? guidance)
    {
        return (angle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "problem_solution" => SectionSlot.Cover(
                $"the problem this reader has with {keyword} today, what it costs them, and where {product} breaks it",
                "500-700 words",
                guidance),
            "comparative" => SectionSlot.Cover(
                $"how {product} stands against the alternatives this reader is actually weighing for {keyword}",
                "500-700 words",
                guidance),
            "case_study_data" => SectionSlot.Cover(
                $"the evidence for {product} on {keyword}, what the partner data actually shows and what it does not",
                "500-700 words",
                guidance),
            _ => SectionSlot.Cover(
                $"what {product} is for, and where it fits in the work this reader is doing around {keyword}",
                "500-700 words",
                guidance),
        };
    }

    /// <summary>
    /// What the opening section is told of the operator's framing: where it is, when the page carries
    /// it. Null when the brief holds nothing for this tool or for the category.
    /// </summary>
    /// <remarks>
    /// The framing itself is in the block every body request carries (<see cref="Body"/>,
    /// <see cref="GccPartnerAnswers.ForToolPage"/>); the opening section is sent to it. Until
    /// 2026-10-12 the framing was printed here, in the opening section's own guidance, which only the
    /// first of the page's five body requests prints: the other eight sections were written with none
    /// of what the operator entered for the tool. The same question decides both, so a section is
    /// never sent to a block its request does not carry.
    /// </remarks>
    private static string? OpeningGuidance(GccToolFraming? framing, string? productName) =>
        GccPartnerAnswers.ForToolPage(framing, productName) is null ? null : GccPartnerAnswers.ToolOpeningGuidance;

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
        prompts.BuildArticleLedePrompt(
            ctx.Context, Meta(ctx), evidenceBlock: ctx.EvidenceBlock,
            operatorsProblem: ctx.ToolFraming?.HookGuidance(ctx.App?.Name));

    public ChatCompletionRequest Body(ContentTypePromptContext ctx)
    {
        var app = ctx.App ?? throw new InvalidOperationException("A tool page needs the product it is about.");
        // What the operator entered for every partner and for this tool, after the evidence, in every
        // body request: each of the page's sections is written within it. Added here and not by the
        // caller, so the request that sends the opening section to it is a request that carries it.
        var evidence = string.Join(
            Environment.NewLine,
            new[] { ctx.EvidenceBlock, GccPartnerAnswers.ForToolPage(ctx.ToolFraming, app.Name) }
                .Where(block => !string.IsNullOrWhiteSpace(block)));
        return prompts.BuildToolBodyPrompt(
            ctx.Context,
            Meta(ctx),
            app,
            ctx.ToolSlug ?? string.Empty,
            outline: ctx.SectionBatch ?? OutlineFor(ctx),
            revisionNotes: null,
            extractedToolResearchJson: ctx.ExtractedResearchJson,
            lede: ctx.Lede,
            fullOutline: OutlineFor(ctx),
            batchIndex: ctx.SectionBatchIndex,
            evidenceBlock: evidence.Length == 0 ? null : evidence,
            quoteCandidates: ctx.QuoteCandidates,
            writtenSoFar: ctx.WrittenSoFar);
    }
}
