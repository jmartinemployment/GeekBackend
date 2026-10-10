using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;

namespace GeekAPI.Services.ContentCreator.ContentTypes;

/// <summary>
/// Blog: a deep-dive companion article. Its sections are <b>obligations defined here</b>, as Pillar's and
/// Tool's are -- not headings invented by the metadata call.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this changed, 2026-10-02.</b> This type used to return
/// <c>ctx.BlogMetadata.SectionOutline.Select(SectionSlot.Assigned)</c> -- free strings from the metadata
/// call -- and it was the only live type that let the model choose what its sections <i>were</i>. That is
/// the whole reason a tools section kept appearing: with the section set open, "Best Tools for X" is inside
/// the space of valid answers, and everything downstream could only detect it after the fact. Pillar
/// (<c>PillarPrompts</c>) and Tool (<c>ToolPrompts</c>) already own their plans and have never produced one.
/// </para>
/// <para>
/// Jeff, four times, most recently 2026-10-02: <i>"Pillar &amp; Blog are not to contain a tool section,
/// period"</i> and <i>"Tools have no headings within Pillar &amp; Blogs"</i>. The historical failure was
/// three things, not one -- the tools were <b>omitted</b> from the solution prose, <b>displaced</b> into a
/// listing, and the listing <b>repeated on every run</b>. A prohibition alone fixes only the middle one and
/// leaves a page with no tools at all, which is worse. So the obligations below carry the requirement as
/// well as the ban: the tools are discussed inside the sections that solve the problem.
/// </para>
/// <para>
/// <b>No slot owns a tool.</b> A reviewer proposed mapping the five partners one-per-section; that is a
/// tools section spread across five headings, and Jeff's instruction forbids exactly that shape -- <i>"not
/// a paragraph per tool, not the same sentence shape five times with the names swapped. A reader must not
/// be able to see the template."</i> Each slot names the tools that bear on <i>its own</i> facet, the
/// writer places them where the argument reaches them, and <c>GccRequiredToolMentions</c> enforces that
/// every declared partner appears somewhere in the finished page.
/// </para>
/// <para>
/// Nothing was lost by dropping the planned headings. The SERP and competitor material the planner used
/// reaches the body writer directly through <c>ResearchBriefPhase.BlogSection</c>
/// (<c>ResearchBriefBuilder.cs:90-96</c> -- keyword SERP, competitor gaps, authoritative sources, known
/// tools), so the long-tail signal is in front of the writer regardless. <c>SectionOutline</c> carried
/// heading text, not context, and nothing on the Create path consumed it.
/// </para>
/// </remarks>
public sealed class BlogPrompts(IContentPromptBuilder prompts) : IContentTypePrompts
{
    public string Key => "blog";

    /// <summary>
    /// Five obligations plus an angle-shaped opening -- <c>BlogSectionCountMin</c> is 5 and the target is 6
    /// (<c>ContentLengthTargets:42-44</c>), at 450-600 words each against a 2,000-2,700 word page.
    /// </summary>
    /// <remarks>
    /// Blog-shaped rather than a copy of Pillar's six: this is a deep dive on one question, so it spends its
    /// middle on the mechanics and the evidence rather than on breadth. The heading for each is the writer's
    /// own words; what is fixed is what the section is <i>responsible for</i>.
    /// </remarks>
    public IReadOnlyList<SectionSlot> OutlineFor(ContentTypePromptContext ctx)
    {
        // Already the keyword -- see ProjectGenerationContext.TargetKeyword. Re-splitting here made this
        // a second reader that disagreed with the scorer on a multi-colon topic.
        var keyword = ctx.Context.TargetKeyword;
        var depth = $"{ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words";

        // The operator's framing, where the brief carries it, on the slots that ask what it answers:
        // the problem on the opening, the operator's failures on the cost section, the operator's
        // automation on the mechanics, and the later sections held to that approach. Until 2026-10-06
        // the blog, like the pillar, never received it (Jeff: "inventing its own Methodology versus
        // using mine?"); the slot text below is unchanged, and what each argues from is now the operator's.
        var niche = ctx.NicheFraming is { HasAny: true } framing ? framing : null;
        var pointer = niche?.ApproachPointer();

        return
        [
            Opening(ctx.Context.ContentAngle, keyword, niche),
            SectionSlot.Cover(
                $"what doing {keyword} by hand actually costs this reader -- the hours, the errors, the "
                + "delay, and who absorbs them",
                depth,
                With(
                    "Concrete and attributable, from the evidence in front of you. Not a list of generic pain "
                    + "points, and no tool is named here -- this section is the problem, stated so plainly that "
                    + "the rest of the page has something to solve.",
                    niche?.FailuresGuidance())),
            // The page's keyword heading belongs here: this is the section about the work once it is automated,
            // so the exact phrase reads true in its heading. The two before it are about doing it by hand.
            //
            // It names the tools in the prose and says nothing about linking them. The blog's sentence also said
            // "Link the first substantive mention"; the pillar has no such sentence, and its tools are linked
            // correctly by the tools block every long-form body prompt shares, which gives each tool its path
            // (Jeff, 2026-10-07: copy how the pillar links tools). What follows the naming sentence is the
            // pillar's "how the approach works" guidance, word for word.
            SectionSlot.Cover(
                $"how the work changes once {keyword} is automated -- the mechanics, in the order they happen",
                depth,
                With(
                    With(
                        "Name the partner tools that do this part of the work, in the prose, where the explanation "
                        + "reaches them -- what each one does about THIS step, not what it is in general. Never a "
                        + "heading, never a sub-section, never one paragraph per product.",
                        niche?.ApproachGuidance()),
                    GccPublisherPositions.ApproachSlotGuidance)) with { OwnsKeywordHeading = true },
            SectionSlot.Cover(
                $"what separates an implementation of {keyword} that holds up from one that stalls",
                depth,
                With(
                    "The decisions made early that cannot be unmade -- data, mapping, approval routing, who owns "
                    + "what. Name the tools whose behaviour decides these, where that matters to the point being "
                    + "made.",
                    pointer)),
            SectionSlot.Cover(
                $"what the evidence shows about {keyword} -- measured outcomes, and what they do not prove",
                depth,
                "Only figures the retrieved evidence carries, attributed to the partner that published them. "
                + "Where the evidence is thin, say what is unknown rather than filling it."),
            SectionSlot.Cover(
                $"when automating {keyword} is the right call, when it is not, and what this reader does next",
                depth,
                With(
                    "An honest boundary -- the cases where the manual way is still correct. Then one concrete "
                    + "next step, not a summary of the page.",
                    pointer)),
        ];
    }

    /// <summary>The slot's own guidance, followed by the operator's where there is some.</summary>
    private static string With(string own, string? operators) =>
        string.IsNullOrWhiteSpace(operators) ? own : $"{own}{Environment.NewLine}   {operators}";

    /// <summary>
    /// The opening obligation, shaped by the brief's Angle for SEO.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>ToolPrompts.Opening</c> (<c>:82-96</c>), which is the only angle-aware structure in the
    /// codebase -- reusing its shape rather than inventing a second convention. On
    /// <c>problem_solution</c> the keyword names the <i>solution</i>, so the problem is its manual form:
    /// the page argues that doing it by hand is the problem, and automating it is the answer.
    /// </remarks>
    private static SectionSlot Opening(string? angle, string keyword, GccNicheFraming? niche)
    {
        var depth = $"{ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words";
        // The whole framing on the opening, whatever the angle: a comparative or evidence-led post opens
        // differently but is still about the same problem, and the framing says what that problem is.
        var guidance = niche?.ToGuidance();
        return (angle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "problem_solution" => SectionSlot.Cover(
                $"the moment this reader recognises the problem: doing {keyword} the manual way, and why it "
                + "keeps costing them",
                depth,
                guidance),
            "comparative" => SectionSlot.Cover(
                $"the choice this reader is actually facing about {keyword}, and what the options really differ on",
                depth,
                guidance),
            "case_study_data" => SectionSlot.Cover(
                $"what measurably changed for someone who automated {keyword}, and under what conditions",
                depth,
                guidance),
            "ultimate_guide" => SectionSlot.Cover(
                $"what {keyword} is, what it requires, and what this page settles for the reader",
                depth,
                guidance),
            _ => SectionSlot.Cover(
                $"what this reader is dealing with around {keyword}, and what this page settles for them",
                depth,
                guidance),
        };
    }

    /// <summary>LedeJsonContract -- read with ParseLede, not ParseSections.</summary>
    public ChatCompletionRequest Lede(ContentTypePromptContext ctx) =>
        prompts.BuildStandaloneBlogLedePrompt(ctx.Context, Meta(ctx), ctx.EvidenceBlock);

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
            lede: ctx.Lede,
            sectionBatch: ctx.SectionBatch,
            batchIndex: ctx.SectionBatchIndex,
            // The whole plan as context, so a batch knows what the other calls own.
            fullOutline: OutlineFor(ctx));
}
