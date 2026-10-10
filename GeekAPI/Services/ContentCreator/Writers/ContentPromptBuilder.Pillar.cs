using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

using GeekAPI.Services.ContentCreator.ContentTypes;

using GeekAPI.Services.ContentCreator;

// Physically filed under GeekAPI/Services/ContentCreator/Writers rather than beside
// ContentPromptBuilder.cs, but this partial-class part keeps ContentPromptBuilder's own
// namespace -- C# requires every part of one partial class to share it, and the alternative
// (a namespace matching the folder) would make this a different type, not a part of this one.
namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

// Pillar Write -- single-source-of-responsibility extraction, step 2
// (plans/single-source-of-responsibility.md, section 6). A pure, behaviour-preserving move:
// every method and JSON contract below is physically relocated out of ContentPromptBuilder.cs
// into this file via a partial-class split, with zero change to any body, signature or caller.
// ContentPromptBuilder stays one compiled type -- PillarPrompts, GccGenerateService and every
// existing test that references ContentPromptBuilder see no difference at all.
//
// IMPORTANT cross-pipeline finding (2026-10-09, not in the plan's section 4/5 evidence, which
// predates it by 11 commits on this file): several of these methods are reached by one more
// caller than plans/single-source-of-responsibility.md accounts for --
//   - BuildPillarLedePrompt: also called by ContentGenerationOrchestrator.cs:1458 (the
//     Workflow product's own, separate pillar pipeline).
//   - BuildArticleSectionBatchPrompt: also called by ContentGenerationOrchestrator.cs:1504.
//   - BuildArticleFaqSectionPrompt: also called by ContentGenerationOrchestrator.cs:1597.
//   - BuildArticleMetadataPrompt: also called by ContentGenerationOrchestrator.cs:1333.
//   - BuildIntroductionSectionGuidance, BuildBenefitsSectionGuidance,
//     BuildBestPracticesSectionGuidance, BuildFutureTrendsSectionGuidance,
//     BuildConcretenessRevisionAmplifier and NotesAskForConcreteness: each also called from
//     BuildArticleSectionPrompt (singular, ContentPromptBuilder.cs), which in turn is called
//     only by ContentGenerationOrchestrator.cs:1567 -- i.e. these are shared between Pillar
//     real path and a method that is otherwise Workflow-only.
// None of this makes the move unsafe tonight -- a partial-class split changes where source
// text lives, never which type it compiles into, so ContentGenerationOrchestrator keeps
// resolving these calls exactly as before. It matters for later: this file cannot be cut over
// to a standalone Pillar Write class, and the old GccGenerateService pillar path cannot be
// deleted, without first accounting for that other pipeline --
// see the plan's 2026-10-09 progress note under section 6 step 2.
//
// BuildArticleLedePrompt, BuildArticleMetaRevisionPrompt, BuildArticleSectionPrompt
// (singular), BuildImplementationSectionGuidance and BuildBriefBodyGuidance stayed in
// ContentPromptBuilder.cs on purpose: the first is Tool's lede builder (shared with Blog/Tool,
// not Pillar-only); the rest are either Workflow-only (zero caller on Pillar's real path) or
// shared across Pillar, Blog, Tool and the short-form writers alike -- see the plan's
// progress note for the full list and the evidence for each.
public partial class ContentPromptBuilder
{
    /// <summary>
    /// The introduction has no heading. Unlike the lede, which is this page's first H2, the
    /// introduction is that opening continuing -- not a second section. It used to take the full
    /// section shape, heading included, which is how a pillar could end up with the title, the
    /// lede's heading and then a third headline before any body section.
    /// </summary>
    /// <remarks>
    /// A property, not a <c>static readonly</c> field as it was before the 2026-10-09 partial-class
    /// split: it reads <c>ParagraphJsonShape</c>/<c>SectionJsonContract</c>, which stayed behind in
    /// ContentPromptBuilder.cs. A field initializer runs once, at an order between the two files'
    /// static fields that C# leaves unspecified -- this one ran before the base file's in practice,
    /// reading them as null and silently dropping both from every rendered prompt (caught by
    /// <c>LedeLengthTests.EveryLedePromptAsksForAHeading("pillar")</c>, 2026-10-09). A property has
    /// no initialization order to get wrong: it evaluates on every read, long after both files'
    /// static fields exist.
    /// </remarks>
    private static string IntroductionJsonContract =>
        "{\"paragraphs\": [" + ParagraphJsonShape + ", ...] (continues the lede; no heading), " +
        "\"children\": [" + SectionJsonContract + ", ...] (optional nested h3s)}";

    /// <summary>Same reason as <see cref="IntroductionJsonContract"/>: a property, reading <c>LedeJsonContract</c> from the base file.</summary>
    private static string LedeAndIntroductionJsonContract =>
        "{\"lede\": " + LedeJsonContract + ", \"introduction\": " + IntroductionJsonContract + "}";

    private const string ArticleMetadataJsonContract =
        "{\"title\": string, \"summary\": string (the standfirst: one or two sentences placed directly under the H1, stating the promise this page makes to the reader in plain language — not the meta description reworded, not a list of what the page covers), \"metaDescription\": string (140-160 characters, must include the target keyword naturally, no hype), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-7 declarative H2 headings, plus final item: \"People Also Ask\")}";

    public ChatCompletionRequest BuildArticleMetadataPrompt(
        ProjectGenerationContext context,
        IReadOnlyList<string>? previousViolations = null)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine(HierarchyPromptGuidance(context, strictChildHeadings: true))
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(ArticleMetadataJsonContract)
            .AppendLine("With the exception of the Lede, article headings are never questions.")
            .AppendLine("GOOD sectionOutline example: [\"Where Enterprise AI Budgets Actually Go\", \"Implementation Framework\", \"Measuring ROI\", \"People Also Ask\"]")
            .AppendLine("BAD sectionOutline example: [\"What is AI?\", \"How does it work?\"] — never use questions as main H2s.")
            .AppendLine("CRITICAL: sectionOutline[0] is the opening H2 — it MUST be a creative hook headline (lede-driven, specific to the keyword), never a generic \"Introduction to...\" / \"Introduction/Overview\" label. The lede's 12-type hook IS this first H2.")
            .AppendLine("No heading anywhere in the outline is \"Overview\" or a variant of it. An overview is a kind of lede — the summary hook — so it belongs in the opening H2 and nowhere else; a later section that sets out to overview the topic is the opening written twice.")
            .AppendLine("BAD first H2: \"Introduction to AI Content Creation Workflow\" — never use a bare Introduction label.")
            .AppendLine("Meta description MUST be 140-160 characters, include the target keyword naturally, and stay factual — no hype words like \"cutting-edge\".")
            .ToString();

        var matchedUseCaseInstruction = context.MatchedUseCase is { } matched
            ? $"This keyword corresponds to \"{matched.Name}\" — an item already named on {context.PublisherName}'s own Home page under \"{matched.Category}\"" +
              (string.IsNullOrWhiteSpace(matched.Description) ? "." : $", described there as: \"{matched.Description}\".") +
              " Build sectionOutline so this pillar is demonstrably the page that Home page item was promising — align with that description rather than treating the keyword generically. "
            : string.Empty;

        var hierarchyOutlineInstruction = HierarchyChildOutlineInstruction(context, forPillarOrBlog: true);

        var user = ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleMetadata,
            $"Plan a comprehensive pillar TechnicalArticle use case targeting the keyword \"{context.TargetKeyword}\" for {context.PublisherName}. " +
            matchedUseCaseInstruction +
            hierarchyOutlineInstruction +
            "Derive sectionOutline from keyword SERP and local pack headings (declarative topics like \"Benefits of X\", not questions). " +
            "Frame this as a use case showing how AI implementation services solve the client problem — not just generic background. " +
            NoToolsSectionOutlineInstruction + " " +
            SeoOutlineInstruction(context.TargetKeyword) + " " +
            "With the exception of the Lede, article headings are never questions. People Also Ask questions from the brief are not outline headings. " +
            "Title must NOT be a question and must NOT start with \"How\" — use a definitive statement (e.g. \"AI Prospecting and Lead Intelligence: Implementation Guide\"). " +
            $"Meta description: 140-160 characters, include \"{context.TargetKeyword}\" naturally, concise factual summary for B2B readers, no hype. " +
            "End sectionOutline with exactly one FAQ section titled \"People Also Ask\" — PAA questions are answered there in the body step, not as main H2s. " +
            "Return title, metaDescription, keywords, and sectionOutline only (body is written separately).");

        if (previousViolations is { Count: > 0 })
        {
            // The plan is regenerated, never repaired, so a retry has to actually change the
            // model's behaviour — restating the rule it just broke is the only lever available.
            user += Environment.NewLine
                + "The previous attempt was rejected: " + string.Join(" ", previousViolations)
                + " Fix exactly that and keep everything else.";
        }


        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.5,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildPillarLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string ledeHeading,
        int ledeIndex,
        int totalSections,
        IReadOnlyList<SectionSlot> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null,
        string? existingLedeHeading = null,
        string? evidenceBlock = null)
    {
        var outlineContext = RenderOutline(fullOutline);
        var system = SystemPrompt(LedeAndIntroductionJsonContract);

        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine(SeoLedeInstruction(context.TargetKeyword))
            .AppendLine("Tone: audience × angle sets ledeType and voice (audience + angle + topic → 12 types); keep an expert, consultative tone throughout.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine()
            .AppendLine("Produce the pillar's opening — its first H2 and the lead paragraphs under it.")
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("Do NOT start with \"How\" or a question unless ledeType is Question.")
            .AppendLine("PAIN BEFORE SOLUTION (required): the first paragraph must open on the practitioner's pain with the manual / status-quo process ")
            .AppendLine("for the target keyword (cost, delay, error, risk, wasted hours) — before naming AI or an intelligent solution.")
            .AppendLine("Only after that pain is established, introduce how an AI-assisted approach changes the situation.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(LedeAskInstruction)
            .AppendLine()
            .AppendLine("The introduction continues the same opening — it is not a second start:")
            .AppendLine("After the hook, carry straight on into scoping (who this is for, what the article walks through). Never a duplicate hook, and never a heading.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            // Not a fixed quota: this read "2-3 h3s, each MUST nest 1-3 h4s" until 2026-10-09, forcing
            // this call to invent structure alongside its Lede and Introduction word counts in the same
            // budget. BuildArticleSectionBatchPrompt's body call already states the material-driven
            // version below; this call never had it. An h4 invented to fill a required slot has no real
            // subtopic behind it, and is the likeliest way to write a heading GccHeadingProvenanceGuard
            // then refuses as unlicensed.
            .AppendLine("Nest h3 children in \"children\" where the introduction's own material genuinely has distinct parts, with multiple text paragraphs and at least one list paragraph where appropriate; nest h4 under an h3 only when that part itself divides.")
            .AppendLine("Depth where the material has depth, not a fixed lattice on every section. Do not leave an h3 you do write as a bare label with nothing under it -- but an introduction with no h3 at all, because its own material does not divide, is not a gap.")
            .AppendLine("CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. " +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch, but MUST be explicitly labeled hypothetical/illustrative.")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage — vary outcomes and make them operationally specific.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for the Introduction section.")
            .AppendLine(BuildIntroductionSectionGuidance(context))
            .AppendLine()
            .AppendLine("Always include both \"lede\" and \"introduction\" keys. They are one continuous opening: the lede carries the heading, the introduction carries none, and its paragraphs follow the lede's under that same heading.")
            .AppendLine(LedeHeadingInstruction)
            .AppendLine()
            .AppendLine(BuildPublisherSiteBlock(context));

        // The opening is where the page's factual claims are set, so it is written against the same
        // retrieved evidence the body gets -- see LedeEvidenceInstruction for why the framing comes
        // first. Only the research half is passed in: BuildCompetitorHeadingBlock tells the model to
        // tag a heading "competitor:<exact heading text>" and refers it to the provenance rules, and
        // neither exists here -- the lede contract has no provenance field.
        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            user.AppendLine(LedeEvidenceInstruction).AppendLine(evidenceBlock);
        }

        if (isRegeneration)
        {
            user.AppendLine("REGENERATION: use fresh prose and examples.");
        }

        var ledeNotes = ScopeRevisionNotesForLede(revisionNotes, existingLedeHeading, metadata.SectionOutline);
        var ledeRevisionBlock = BuildRevisionNotesBlock(ledeNotes, sectionHeading: existingLedeHeading);
        if (ledeRevisionBlock is not null)
        {
            user.AppendLine("LEDE " + ledeRevisionBlock);
        }

        var introRevisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: ledeHeading);
        if (introRevisionBlock is not null)
        {
            user.AppendLine("INTRODUCTION " + introRevisionBlock);
        }

        user.AppendLine()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.Opening));

        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            .AppendLine($"Write the pillar's Lede (first H2) {ledeIndex + 1} of {totalSections}. It covers: {ledeHeading}. You write its heading.")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .AppendLine()
            .AppendLine("Full article outline (for context only — write ONLY the Lede H2):")
            .AppendLine(outlineContext)
            .AppendLine(AnswerInTheContract);

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 6144);
    }

    public ChatCompletionRequest BuildArticleSectionBatchPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<SectionSlot> slots,
        IReadOnlyList<SectionSlot> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null,
        bool requireHeadingProvenance = false,
        string? evidenceBlock = null,
        Section? lede = null,
        int batchIndex = 0)
    {
        var outlineContext = RenderOutline(fullOutline);
        var namesItsOwn = slots.Any(sl => sl.WritesItsOwnHeading);
        var headingsList = string.Join(Environment.NewLine, slots.Select((sl, i) =>
            sl.WritesItsOwnHeading
                ? $"{i + 1}. Cover: {sl.Covers}"
                    + (sl.Depth is { Length: > 0 } ? $" (roughly {sl.Depth})" : string.Empty)
                    + (sl.Guidance is { Length: > 0 } ? Environment.NewLine + $"   {sl.Guidance}" : string.Empty)
                : $"{i + 1}. \"{sl.Heading}\""));

        var system = SystemPrompt(requireHeadingProvenance ? SectionsArrayJsonContractWithProvenance : SectionsArrayJsonContract);

        // What a pillar is. The same text on every call of the type.
        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine("A schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Each section's own tag is \"h2\". Use nested h3 children where a section genuinely has distinct parts, and h4 under an h3 only when that part itself divides — depth where the material has depth, not a fixed lattice on every section.")
            .AppendLine(NoToolsSectionInstruction)
            .AppendLine(ToolsAsSolutionInstruction)
            .AppendLine("Open each section where its own material starts. Somewhere early in the page the practitioner's cost — the delay, the error rate, the wasted hours of the status quo — has to be concrete, but it is one page making one argument: do not restate the pain at the top of every section, and never open with \"AI enables…\", \"Intelligent X is…\", a capability list, or a definition of the technology.")
            .AppendLine("Do not write these as neutral textbook explainers — every subsection should be framed through what an AI implementation " +
                $"consultancy like {context.PublisherName} ({context.ImplementerPositioning}) actually does about the problem being discussed, not just background education on it.")
            .AppendLine("Do NOT repeat the same point, example, or framing across sections in this batch — each must cover genuinely distinct ground.")
            .AppendLine("If a hypothetical scenario is used, keep it to 1-2 sentences woven naturally into the surrounding paragraph.")
            .AppendLine("CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. " +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch, but MUST be explicitly labeled hypothetical/illustrative. ")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage across sections — vary outcomes and make them operationally specific.")
            .AppendLine("With the exception of the Lede, article headings are never questions.")
            .AppendLine("Tools listed in the research brief must be woven into sentences where they are relevant to this section — never as a Tools heading or catalog.")
            .AppendLine();

        // Who it is for and what the publisher already says about itself.
        user.AppendLine(BuildBriefBodyGuidance(context));
        user.AppendLine(BuildPublisherSiteBlock(context));
        user.AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection));
        user.AppendLine();

        if (requireHeadingProvenance)
        {
            if (!string.IsNullOrWhiteSpace(evidenceBlock))
            {
                user.AppendLine(evidenceBlock);
            }

            user.AppendLine(HeadingProvenanceInstruction +
                " Each top-level section here fulfils one of the numbered sections you were assigned" +
                " below — tag its own provenance \"plan\", whether the heading was given to you or you" +
                " wrote it yourself. Every h3/h4 child nested under it is yours to invent, and each of" +
                " those needs a real tag from the rules above.");
        }

        var continuity = BuildLedeContinuityBlock(lede);
        if (continuity is not null)
        {
            user.AppendLine(continuity);
        }

        // Per-heading guidance — these blocks are pure functions of context (not the loop index),
        // so appending each one that applies across the whole batch is safe even combined into a
        // single call, as long as they're clearly scoped to the heading they apply to.
        foreach (var heading in slots.Select(sl => sl.Label))
        {
            if (PillarSectionClassifier.IsBenefitsSection(heading))
            {
                user.AppendLine($"For \"{heading}\":").AppendLine(BuildBenefitsSectionGuidance(context));
            }
            if (PillarSectionClassifier.IsBestPracticesSection(heading))
            {
                user.AppendLine($"For \"{heading}\":").AppendLine(BuildBestPracticesSectionGuidance(context));
            }
            if (PillarSectionClassifier.IsFutureTrendsSection(heading))
            {
                user.AppendLine($"For \"{heading}\":").AppendLine(BuildFutureTrendsSectionGuidance(context));
            }
        }

        if (isRegeneration)
        {
            user.AppendLine("REGENERATION: use fresh prose and examples.");
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            user.AppendLine(revisionBlock);
            if (slots.Any(sl => PillarSectionClassifier.IsBenefitsSection(sl.Label)) || NotesAskForConcreteness(revisionNotes, string.Empty))
            {
                user.AppendLine(BuildConcretenessRevisionAmplifier());
            }
        }

        // What this call writes. The part that differs from one batch to the next, last.
        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            .AppendLine($"Write {slots.Count} sections of this pillar in one response.")
            .AppendLine(SeoBodyInstruction(
                context.TargetKeyword, GccLongFormTypes.Pillar,
                // The lede wrote fullOutline[0], so the body's own sections are what remains.
                slots.Count, Math.Max(slots.Count, fullOutline.Count - 1),
                SectionSlot.BatchOwnsKeywordHeading(slots, fullOutline, batchIndex)))
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for EACH section.")
            .AppendLine(namesItsOwn
                ? "Write these sections, in this order. Each numbered entry says what the section must cover; you write its heading:"
                : "Write these sections, in this order:")
            .AppendLine(headingsList)
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine()
            .AppendLine("Full article outline (for context only — write ONLY the sections listed above):")
            .AppendLine(outlineContext)
            .AppendLine()
            .AppendLine(BatchClosingInstruction(
                context, [.. slots.Select(sl => sl.Label)], [.. fullOutline.Select(sl => sl.Label)]))
            .AppendLine(AnswerInTheContract);

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: LongFormBodyMaxOutputTokens));
    }

    public ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes = null) =>
        FaqSectionPrompt(
            context, metadata.Title, faqQuestions,
            pageKind: "TechnicalArticle pillar", heading: "People Also Ask", isRegeneration, revisionNotes);

    private static string BuildIntroductionSectionGuidance(ProjectGenerationContext context)
    {
        var sb = new StringBuilder()
            .AppendLine("INTRODUCTION / OVERVIEW SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Frame this section around the problems the approach solves for practitioners — not a textbook definition of the technology.")
            .AppendLine("Open with the costs of the status-quo process (errors, delays, manual effort, compliance risk), then explain what intelligent / AI-assisted ")
            .AppendLine($"compliance or automation changes. Tie the framing to what {context.PublisherName} helps clients address, without hard-selling.")
            .AppendLine("Ban openings that define \"what AI is\" or tour features before naming a concrete business pain.");

        if (context.MatchedUseCase is { } matched)
        {
            sb.AppendLine($"This article corresponds to \"{matched.Name}\", already named on {context.PublisherName}'s own Home page under \"{matched.Category}\"" +
                (string.IsNullOrWhiteSpace(matched.Description) ? "." : $", described there as: \"{matched.Description}\".") +
                " Make sure this introduction's framing is demonstrably continuous with that — a reader who saw it on the Home page should recognize this as the page it pointed to, not a different treatment of the same keyword.");
        }

        if (context.DesiredHeadings is { Count: > 0 } desiredHeadings)
        {
            sb.AppendLine("REQUIRED SUBTOPICS: in addition to your own introduction content above, include one h3 child (with 1-3 h4 children each) for each of these " +
                "client-requested subtopics — write each with the same depth/quality as any other h3, introducing what the reader will learn, since each will eventually " +
                $"get its own dedicated article: {string.Join(", ", desiredHeadings.Select(h => $"\"{h}\""))}.")
                .AppendLine("This section may run longer than the standard section word target to properly cover both your own introduction content and every " +
                "required subtopic in full — do not shorten or drop either to fit the usual length.");
        }

        return sb.ToString();
    }

    private static string BuildBenefitsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("BENEFITS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("This section fails if it reads as polished marketing claims (\"streamlined\", \"enhanced efficiency\", \"competitive edge\") without ")
            .AppendLine("naming what changes in day-to-day work. For EACH benefit, state a concrete before→after operational change: who does what differently, ")
            .AppendLine("what error/delay/handoff disappears, or what decision becomes possible that was not before.")
            .AppendLine($"Tie at least half of the benefits to what {context.PublisherName} actually configures or designs (data model, workflow, integration, ")
            .AppendLine("change management) — not to abstract \"AI capabilities\".")
            .AppendLine("Use at most ONE labeled hypothetical in the whole section. Make it operationally specific and unique to this section — ")
            .AppendLine("never recycle a stock \"40% reduction for a mid-sized retailer/manufacturer\" line used elsewhere in the article.")
            .AppendLine("List bullets must be operational outcomes (e.g. \"exemption certificates validated before filing, not after notice\") — not slogan phrases.")
            .AppendLine("Ban filler: cutting-edge, paradigm shift, transformative potential, seamless transition, maximize ROI, unlock value.")
            .ToString();
    }

    private static string BuildConcretenessRevisionAmplifier() =>
        """
        CONCRETENESS REVISION (mandatory for this pass):
        The reviewer flagged generic claims. Do NOT rephrase the same claims with nicer adjectives.
        Replace each generic benefit with a specific operational example: role or team, task that changes, and the failure mode avoided.
        Prefer distinct before→after details over percentages. If you use one quantified hypothetical, label it clearly and do not reuse a stock 40% line.
        """;

    /// <summary>
    /// True when revision notes that apply to <paramref name="sectionHeading"/> ask for concrete /
    /// specific examples or call out generic claims.
    /// </summary>
    internal static bool NotesAskForConcreteness(string? revisionNotes, string sectionHeading)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return false;
        }

        var scoped = revisionNotes;
        if (revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            var matching = new List<string>();
            foreach (var item in SplitNumberedNoteItems(revisionNotes))
            {
                var match = SectionTagRegex.Match(item);
                if (match.Success
                    && match.Groups["heading"].Value.Trim().Equals(sectionHeading, StringComparison.OrdinalIgnoreCase))
                {
                    matching.Add(item);
                }
            }

            if (matching.Count == 0)
            {
                return false;
            }

            scoped = string.Join('\n', matching);
        }

        ReadOnlySpan<string> markers =
        [
            "concrete", "specific", "generic", "example", "examples", "specificity", "vague", "measurable"
        ];
        foreach (var marker in markers)
        {
            if (scoped.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildBestPracticesSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("BEST PRACTICES SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Do not write generic industry advice divorced from the publisher. For each practice, tie it explicitly to how ")
            .AppendLine($"{context.PublisherName} solves that problem for clients — name the concrete mechanism: accelerated deployment timelines, ")
            .AppendLine("data model design, workflow/process configuration, integration setup, or change management (training, adoption, rollout).")
            .AppendLine("Example pattern: state the practice, then 1-2 sentences on what goes wrong without it, then how an experienced implementer ")
            .AppendLine("closes that gap (e.g. \"Without a documented data model, teams re-map fields after go-live; an implementer front-loads this ")
            .AppendLine("during discovery so config work doesn't get redone.\").")
            .AppendLine("Any tool or platform named here must be real and verifiable — never invent a feature or product to illustrate a practice.")
            .AppendLine("CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. " +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();
    }

    private static string BuildFutureTrendsSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("FUTURE TRENDS SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Do not end this section as neutral industry commentary. For each trend, add 1-2 sentences on how ")
            .AppendLine($"{context.PublisherName} is positioned to help clients act on it now — e.g. evaluating/piloting the trend, adapting existing ")
            .AppendLine("data models or workflows to it, or guiding change management as teams adopt it. Keep it consultative, not a sales pitch.")
            .AppendLine("Only cite real, verifiable tools, vendors, or capabilities when discussing a trend — never invent one to make the trend concrete.")
            .AppendLine("CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. " +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();
    }
}
