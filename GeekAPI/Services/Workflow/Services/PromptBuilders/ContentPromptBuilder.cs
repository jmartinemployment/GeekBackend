using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.Workflow.Services.PromptBuilders;

public interface IContentPromptBuilder
{
    ChatCompletionRequest BuildTopicFocusPrompt(string siteName, IReadOnlyList<string> headings, IReadOnlyList<string> paragraphs);

    /// <summary>Extracts named use-case/service items from the Home page's own crawled content (e.g. an
    /// "Our Use Cases" listing), so a project's TargetKeyword can later be matched against a real,
    /// already-published item name.</summary>
    ChatCompletionRequest BuildUseCaseExtractionPrompt(string siteName, IReadOnlyList<string> homeHeadings, IReadOnlyList<string> homeParagraphs);

    /// <param name="previousViolations">
    /// Why the last plan was rejected, when this is a retry. Passed back so the model is told what
    /// it broke instead of being asked the same question again.
    /// </param>
    ChatCompletionRequest BuildArticleMetadataPrompt(
        ProjectGenerationContext context,
        IReadOnlyList<string>? previousViolations = null);

    ChatCompletionRequest BuildArticleLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string? revisionNotes = null,
        string? existingLedeHeading = null);

    /// <summary>Produces the pillar's opening Lede H2 — the hook (12-type, audience×angle→heading/topic, tone) plus its real h3/h4 scoping.
    /// Replaces the tightly-coupled lede+Introduction pair; the lede IS the first H2.</summary>
    ChatCompletionRequest BuildPillarLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string ledeHeading,
        int ledeIndex,
        int totalSections,
        IReadOnlyList<SectionSlot> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null,
        string? existingLedeHeading = null);

    /// <summary>
    /// Small revise pass for meta description (and title only when notes explicitly demand it).
    /// Returns null when <paramref name="revisionNotes"/> do not mention meta/title hygiene.
    /// </summary>
    ChatCompletionRequest? BuildArticleMetaRevisionPrompt(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string revisionNotes);

    /// <summary>Writes several main-body H2 sections in one call (e.g. Benefits + any other
    /// non-Implementation/Introduction sections) — call-count consolidation, same
    /// SectionsArrayJsonContract/ParseSections pattern the blog fix and tool pages already use.</summary>
    /// <param name="slots">One entry per section to write, in order. A slot either carries a
    /// heading a planning call wrote for this page, or states what the section must cover and
    /// leaves the heading to the writer -- see <see cref="SectionSlot"/>.</param>
    /// <param name="lede">The opening already written for this page, so the body continues it
    /// instead of restarting in reference voice at the first H2.</param>
    ChatCompletionRequest BuildArticleSectionBatchPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<SectionSlot> slots,
        IReadOnlyList<SectionSlot> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null,
        bool requireHeadingProvenance = false,
        string? evidenceBlock = null,
        Section? lede = null);

    ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null);

    ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes = null);

    /// <summary>Lightweight first Tools call: ordered list of 4–5 real platform names (no nested section JSON).</summary>
    ChatCompletionRequest BuildToolsPlatformListPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        bool isRegeneration,
        string? revisionNotes = null);

    /// <summary>One platform h3 subtree (overview, capability list, implementer h4) for the Tools section.</summary>
    ChatCompletionRequest BuildToolsPlatformChildPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        string platformName,
        IReadOnlyList<string> allPlatforms,
        int platformIndex,
        int platformCount,
        bool isRegeneration,
        string? revisionNotes = null,
        string? crawlHref = null);

    ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle);

    ChatCompletionRequest BuildBlogBodyPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata, string? revisionNotes = null);

    ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);

    /// <summary>
    /// Standalone blog (no pillar) — Content Creator starting-content flexibility.
    /// Uses research brief + keyword, not pillar-repurpose prompts.
    /// </summary>
    ChatCompletionRequest BuildStandaloneBlogMetadataPrompt(ProjectGenerationContext context);

    ChatCompletionRequest BuildStandaloneBlogLedePrompt(ProjectGenerationContext context, BlogMetadataDraft metadata);

    ChatCompletionRequest BuildStandaloneBlogBodyPrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? revisionNotes = null,
        bool requireHeadingProvenance = false, string? evidenceBlock = null, Section? lede = null);

    ChatCompletionRequest BuildSocialPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string platform, string articleUrl);
    ChatCompletionRequest BuildColdOutreachPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string articleUrl);
    ChatCompletionRequest BuildSectionImagePromptsPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogDraft sourceBlog,
        string articleUrl,
        string blogUrl,
        IReadOnlyList<ImagePromptSectionTarget> sections);

    /// <summary>
    /// Content Creator: standalone image prompt (topic + notes, optional artifact context).
    /// Same visual style contract as section image prompts — prompt text only, not pixels.
    /// </summary>
    ChatCompletionRequest BuildStandaloneImagePrompt(
        string topic,
        string? notes,
        string? artifactContext);

    /// <param name="outline">The page's sections as obligations. Tool's outline used to exist three
    /// times -- an array in its prompt set, a literal in GccGenerateService, and prose inside the
    /// prompt itself carrying a comment that the copies had to be kept in sync by hand.</param>
    ChatCompletionRequest BuildToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string toolSlug,
        IReadOnlyList<SectionSlot> outline,
        string? revisionNotes = null,
        string? extractedToolResearchJson = null,
        Section? lede = null);

    /// <summary>
    /// FAQ section for a tool page, additional to the body word-count target -- not a substitute
    /// for it. Sourced from real, already-verified partner FAQ pairs (never re-derived or invented
    /// the way Pillar's PAA-driven FAQ has to be), so the model formats/paraphrases, it doesn't
    /// answer from scratch.
    /// </summary>
    ChatCompletionRequest BuildToolFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<GccPartnerFaqAsset> faqBank);

    ChatCompletionRequest BuildToolMetadataPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        ContentDocument body);

    /// <summary>Roundup document listing/linking each per-tool page from persisted research.</summary>
    ChatCompletionRequest BuildToolRoundupPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        string roundupTitle,
        string toolsResearchBlock);

    /// <summary>Extract structured tool research from an uploaded HTML tool page (one call per upload).</summary>
    ChatCompletionRequest BuildToolResearchExtractionPrompt(string fileName, string htmlOrText);

    ChatCompletionRequest BuildAdvertisingPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, string articleUrl);

    ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel);

}

public class ContentPromptBuilder : IContentPromptBuilder
{
    /// <summary>
    /// Output budget for one pillar section.
    /// <para>
    /// Sections target 500-700 words (<see cref="ContentLengthTargets.PillarSectionMinWords"/>).
    /// 700 words is roughly 1,000 tokens of prose, and the section is returned as JSON with every
    /// paragraph wrapped in runs objects and escaped, which about doubles it — so a section written
    /// to spec needs ~2,000 tokens. The cap was 2,048, leaving no headroom, and a section that hit
    /// its own target was cut off mid-object: observed as completionTokens=2048 exactly, with the
    /// parser reporting "the response looks truncated".
    /// </para>
    /// </summary>
    private const int PillarSectionMaxOutputTokens = 4096;

    private const string TopicFocusJsonContract =
        "{\"focus\": string[] (4-8 short topic phrases, 1-4 words each, describing the site's real services/subject matter — no generic filler words)}";

    private const string UseCaseExtractionJsonContract =
        "{\"items\": [{\"category\": string, \"name\": string (the exact item name as shown on the page), \"description\": string|null (its own short description text, if present), \"href\": string|null (the exact relative or absolute link this item points to, or null if it has no dedicated link yet)}]}";

    /// <summary>
    /// The structured-output contract every section-body call uses. No tag characters and no
    /// heading/emphasis/list punctuation in the text at all — headings are a plain string field,
    /// emphasis/links are boolean/url fields on a run, lists are their own paragraph variant. This
    /// is what actually eliminates truncated or malformed markup: there is no markup syntax
    /// available for the model to get wrong.
    /// </summary>
    private const string RunJsonShape =
        "{\"text\": string (plain text only — never markup syntax of any kind), \"bold\": boolean?, \"italic\": boolean?, \"href\": string?}";

    private const string ParagraphJsonShape =
        "{\"type\":\"text\",\"runs\":[" + RunJsonShape + ", ...]} " +
        "OR {\"type\":\"list\",\"ordered\":boolean,\"items\":[[" + RunJsonShape + ", ...], ...]} " +
        "OR {\"type\":\"quote\",\"runs\":[" + RunJsonShape + ", ...],\"cite\":string? (source URL)} " +
        "(a real block quotation, for wording worth reproducing verbatim with its source — " +
        "never \"According to X, ...\" written as ordinary prose)";

    private const string SectionJsonContract =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], \"href\": null, " +
        "\"children\": [Section, ...] (nested subsections, same shape, one level deeper tag)}";

    private const string SectionsArrayJsonContract =
        "{\"sections\": [" + SectionJsonContract + ", ...] (top-level h2 sections, in order)}";

    /// <summary>
    /// Stage 2 (heading provenance). Every section a model invents beyond the assigned outline --
    /// pillar's required h3/h4 children chief among them -- must be licensed by real material, not
    /// invented from nothing. A section carrying this field is checked by
    /// <c>GccHeadingProvenanceGuard.FindUnlicensedHeadings</c> after parsing; an unresolvable tag
    /// fails the whole generation. Binary, queryable, no similarity matching.
    /// </summary>
    private const string ProvenanceFieldShape =
        "\"provenance\": string (required on every section, top-level and nested) -- exactly one of: " +
        "\"plan\" (only for a heading that matches one you were explicitly assigned to write), " +
        "\"brief:<fieldName>\" (the brief field above it is drawn from), " +
        "\"paa:<question text>\" (the exact People Also Ask question above it answers), or " +
        "\"competitor:<heading text>\" (the exact competitor heading above it fills a gap on)";

    private const string SectionJsonContractWithProvenance =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], \"href\": null, " +
        "\"children\": [<same shape, one level deeper tag>, ...], " + ProvenanceFieldShape + "}";

    private const string SectionsArrayJsonContractWithProvenance =
        "{\"sections\": [" + SectionJsonContractWithProvenance + ", ...] (top-level h2 sections, in order)}";

    private const string HeadingProvenanceInstruction =
        "Every section you write, at every level including nested children, must be licensed by real " +
        "material above -- never invented from nothing. Tag each one with the \"provenance\" field the " +
        "JSON shape requires, using the exact URL, brief field name, PAA question, or competitor heading " +
        "it is drawn from. If a subsection cannot honestly be tagged this way, do not write it. " +
        "A \"competitor:\" tag names a gap that heading revealed, never a heading you may reuse: " +
        "writing the cited text as your own heading is rejected outright. Their outline tells you " +
        "what a reader expects to find covered; it does not tell you what to call it, and " +
        "reproducing the headings every page in this niche already carries is how a page ends up " +
        "reading like all of them.";

    /// <summary>
    /// The AI-filler ban every body-generating prompt needs. Historically this existed only on
    /// techArticle/social/ads/imagePrompt/aiTool via the consultant appendix -- pillar and blog,
    /// the highest-volume outputs, had no equivalent. Same banned phrase list as
    /// BuildSummaryVariantsPrompt, kept in one place rather than retyped per call site.
    /// </summary>
    /// <summary>
    /// How the prose reads, as opposed to what it says.
    ///
    /// <para>
    /// Jeff, 2026-09-23: "Even with RAG it still does not act like human, so it is very easy to spot
    /// as AI written?" Grounding changes what a page says and leaves its register untouched -- a
    /// perfectly evidenced page written in this rhythm still reads as machine-written, and every
    /// other rule added today is about structure or facts.
    /// </para>
    ///
    /// <para>
    /// The tells are mechanical, so the instructions are too. The one passage he liked all day --
    /// a named person at her desk at month-end -- broke most of them, which is the register this
    /// asks for everywhere rather than only in the opening.
    /// </para>
    /// </summary>
    /// <summary>
    /// The publisher's own site, when it has been crawled -- their framework, their proof points,
    /// their offer, in their words.
    ///
    /// <para>
    /// Jeff, 2026-09-23: "You spell out your own methodology instead of enforcing the one on page
    /// 1/home?", then "it's common knowledge to reference existing items on the Home page, which
    /// this has electronically", then "Instead its making shit/good shit up."
    /// </para>
    ///
    /// <para>
    /// Inventing well is still inventing. A page that recommends buying criteria the publisher's own
    /// methodology contradicts -- "choose a provider offering comprehensive onboarding" against
    /// "choose ease of use platforms that require minimal training" -- does not read as generic, it
    /// reads as written by someone who does not work there.
    /// </para>
    /// </summary>
    private static string? BuildPublisherSiteBlock(ProjectGenerationContext context)
    {
        var headings = (context.CrawledHeadings ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).ToList();
        var paragraphs = (context.CrawledParagraphs ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (headings.Count == 0 && paragraphs.Count == 0)
        {
            return null;
        }

        var block = new StringBuilder()
            .AppendLine($"=== {context.PublisherName}'S OWN SITE -- USE THIS, DO NOT INVENT AROUND IT ===");
        foreach (var heading in headings) block.AppendLine($"  # {heading}");
        foreach (var paragraph in paragraphs) block.AppendLine($"  {paragraph}");
        block.AppendLine(
            "This is what the publisher already says about themselves, published and live. Where they " +
            "have a named framework, phases, figures, service area or offer, use theirs -- their " +
            "wording, their order, their numbers. Do not write a competing version of something they " +
            "have already published, and do not recommend criteria their own stated approach " +
            "contradicts.");
        block.AppendLine(
            "Reference their existing pages the way any writer references their own publication: name " +
            "the framework when the section is about how work gets done, use their published figures " +
            "rather than inventing equivalents, and close on the offer they actually make rather than " +
            "a generic suggestion to consider one.");
        block.AppendLine(
            "Paraphrase it. Use their framework, their phases, their figures and their offer -- in " +
            "your own sentences, written for this page. Never reprint the home page: a section that " +
            "quotes their site back at them adds nothing a reader could not get by clicking Home, " +
            "and a page assembled out of lifted blocks is not a piece of writing.");
        block.AppendLine(
            "Where their site is silent, write from the evidence -- but never fill their silence with " +
            "a plausible-sounding invention about them.");
        return block.ToString();
    }

    private const string HumanRegisterInstruction =
        "HOW THIS READS: the giveaway is rhythm, not vocabulary. " +
        "Vary sentence length on purpose. A paragraph whose sentences are all fifteen to twenty-five " +
        "words reads as machine-written however good each one is. Use short sentences -- three to " +
        "eight words -- and let some run long. " +
        "Vary paragraph length too: some are one sentence, some are six. Never a page of even blocks. " +
        "Drop the scaffolding: no Moreover, Furthermore, Additionally, In conclusion, It is worth " +
        "noting, It is important to note. Start the sentence. " +
        "Break the symmetry: no \"not just X, but Y\", no three-item lists used for cadence rather " +
        "than because there are exactly three things, no paired clauses balanced against each other " +
        "line after line. " +
        "Never restate. A paragraph that summarises the paragraph above it is filler with good " +
        "manners, and a closing recap of points already made is the same thing at the end. " +
        "Commit. Say which option is worse and why, say what you would not do, leave something out " +
        "because it does not matter. Covering every angle evenly is how a page says nothing. " +
        "Be specific in a way a generic page could not be: \"a twelve-person AP team\" rather than " +
        "\"businesses\", the actual figure from the evidence rather than \"significant savings\", the " +
        "named product rather than \"leading platforms\". One concrete detail per section that could " +
        "not have been written about any other subject.";

    private const string FillerBanInstruction =
        "Ban filler: cutting-edge, paradigm shift, transformative potential, seamless transition, " +
        "maximize ROI, unlock value. Write specific, verifiable claims instead of hype adjectives.";

    /// <summary>
    /// Headings, for every type that lets the writer name its own sections.
    ///
    /// <para>
    /// Jeff, 2026-09-23: "Headings are lame and I would bet repeated on every single blog post",
    /// immediately followed by "The headings reflect why content word count is so drastically low."
    /// The bet was safe -- Pillar and Tool shipped compile-time heading lists, so they repeated by
    /// construction, and Blog's were model-written with nothing telling the model that a reusable
    /// skeleton was the wrong answer. The second sentence is the part that matters here: this is
    /// not a polish rule. A category label is a section with nothing in particular to say, so it
    /// gets a little of everything and stops early. Naming the claim is what gives the section
    /// somewhere to go, which is why the specificity test below is stated as a test and not as a
    /// preference.
    /// </para>
    /// </summary>
    private const string HeadingCraftInstruction =
        "HEADINGS: write them for this page and no other. The test is concrete -- if a heading " +
        "would sit unchanged on a page about a different product, industry or keyword, it is the " +
        "wrong heading; rewrite it so it states this section's own specific claim. " +
        "\"Overview\" is never a section heading at all: an overview is a kind of lede -- the summary " +
        "hook -- so it belongs in this page's opening and nowhere after it, and this page already " +
        "has an opening. A later section that overviews the subject is the opening written twice. " +
        "The other stock openers -- Introduction, Understanding X, What Is X, Why It Matters, How " +
        "It Works, Key Benefits, Key Capabilities, Key Considerations, Key Takeaways, Common " +
        "Challenges, Best Practices, Getting Started, Next Steps, The Future of X, Final Thoughts, " +
        "Conclusion -- are not forbidden, they are unfinished. Each is fine once it carries this " +
        "page's own subject: \"How It Works\" is a label, \"How Invoice Capture Actually Works\" is a " +
        "heading. Never leave one bare. " +
        "A reader who scans nothing but your headings should come away with the argument. And a " +
        "bare category label is also a section with nothing in particular to say, which is why " +
        "generic headings come back thin -- name the claim and the section has somewhere to go.";

    /// <summary>
    /// Structural monotony, which is a different defect from a bad heading and was producing the
    /// same symptom. Jeff, 2026-09-23: "While it starts off nice with a story, it becomes dull and
    /// a chore to read afterward." The lede lands because it is chosen from twelve types against
    /// audience and angle; the body then ran one formula over every section -- open on the
    /// practitioner problem, nest two to three h3s, nest one to three h4s under each, 500-700 words
    /// -- so all six sections had the same silhouette. That reads as a form someone filled in, and
    /// no amount of per-sentence quality fixes it.
    /// </summary>
    private const string SectionVarietyInstruction =
        "VARY THE SECTIONS: they are parts of one piece of writing, not repetitions of a template. " +
        "Do not open every section the same way, do not give every section the same internal shape, " +
        "and do not close every section on the same note. Some sections carry one example at " +
        "length; some are mostly argument; some earn a list and most do not; some need " +
        "subsections and some are stronger as continuous prose. A page where every section opens " +
        "on a problem statement and resolves into three subheadings is a chore to read by the " +
        "third one, however good the sentences are.";

    /// <summary>
    /// What the opening already did, handed to the call that writes the body.
    ///
    /// <para>
    /// The body prompts could not see the lede, so the page changed voice at the first H2: a hook
    /// written as anecdote or scene-setting, then neutral reference prose that reintroduces the
    /// topic to a reader who is already three paragraphs in. Nothing in either prompt was wrong on
    /// its own; they were simply two documents. Returns null when there is no lede to continue, so
    /// callers can append unconditionally.
    /// </para>
    /// </summary>
    private static string? BuildLedeContinuityBlock(Section? lede, string? ledeType = null)
    {
        if (lede is null || string.IsNullOrWhiteSpace(lede.Heading))
        {
            return null;
        }

        var opening = ContentDocumentText.Flatten(new ContentDocument(lede, [])).Trim();
        var block = new StringBuilder()
            .AppendLine("=== THE OPENING THIS PAGE ALREADY HAS (continue it -- do not restate it) ===")
            .AppendLine($"Opening heading: {lede.Heading}");
        if (!string.IsNullOrWhiteSpace(ledeType))
        {
            block.AppendLine($"Hook type: {ledeType}");
        }

        if (opening.Length > 0)
        {
            block.AppendLine(opening.Length > 1_800 ? opening[..1_800] : opening);
        }

        block.AppendLine(
            "Carry the story forward. If the opening put someone in a situation, they come back: the " +
            "same team, the same invoice, the same Friday afternoon, further along. Two or three " +
            "times across the piece is enough -- a concrete return to the people in the opening, " +
            "where the material naturally allows it. A story used once as a hook and then dropped " +
            "for explanation is the shape that reads well for three paragraphs and becomes a chore.");
        block.AppendLine(
            "The page has started and the reader is inside that thread. The sections below are the " +
            "same piece of writing continuing, not a reference document appended to a story. Keep " +
            "the register the opening set; do not hook the reader a second time, do not " +
            "reintroduce the topic, and do not drop into neutral textbook voice at the first " +
            "heading. Where the opening raised something specific -- a person, a moment, a cost, a " +
            "question -- pay it off later rather than leaving it behind.");
        return block.ToString();
    }

    /// <summary>
    /// Renders the outline for a prompt. Assigned slots list their planned heading; coverage slots
    /// list what they owe the reader and say, once, that the writer names them.
    /// </summary>
    private static string RenderOutline(IReadOnlyList<SectionSlot> outline) =>
        string.Join(Environment.NewLine, outline.Select((s, i) => $"{i + 1}. {s.Label}"));

    /// <summary>
    /// How long the opening actually runs.
    ///
    /// <para>
    /// Every lede prompt said "2-3 paragraphs" and nothing else. That is a count, and three
    /// one-sentence paragraphs satisfies it exactly -- so the one paragraph that decides whether
    /// anybody reads the rest was the only part of the pipeline with no size on it, while body
    /// sections carried 500-700. Jeff, twice: "Lede paragraph way to short", then "Lede paragraph
    /// still ridiculously short! Should be 3 x that length it is LAME."
    /// </para>
    ///
    /// <para>
    /// A per-paragraph floor comes with the total, because a total alone is satisfiable by one long
    /// paragraph and two stubs.
    /// </para>
    /// </summary>
    /// <summary>
    /// How a piece ends.
    ///
    /// <para>
    /// "End with a clear next-step CTA for the reader" was the whole instruction, and it produced
    /// the same non-ending every time -- Jeff, 2026-09-23, quoting a finished post: "If you're
    /// considering automation, it may be beneficial to explore similar success stories in your
    /// industry to understand the potential impact further." That asks the reader to go and think
    /// about it. It names no action, no actor and no next step, and "every sample has lacked" a
    /// real one.
    /// </para>
    ///
    /// <para>
    /// The brief already carries the ask -- ctaType and ctaLabel -- and the closing simply has to
    /// make it. Where the brief names none, the piece still ends on something the reader does, not
    /// on a suggestion that they reflect.
    /// </para>
    /// </summary>
    private static string ClosingCallToActionInstruction(ProjectGenerationContext context)
    {
        var ask = string.IsNullOrWhiteSpace(context.CtaType)
            ? "the one action this reader should take next"
            : context.CtaType
              + (string.IsNullOrWhiteSpace(context.CtaLabel) ? string.Empty : $", worded as \"{context.CtaLabel}\"");

        return "CLOSING: the last section ends by asking for " + ask + ". One ask, stated plainly, "
            + "addressed to the reader, naming who does what next. "
            + "Do NOT end on a reflection -- \"it may be beneficial to explore\", \"consider how this "
            + "could apply\", \"these examples provide insight\", \"to understand the potential impact "
            + "further\". Those name no action and no actor; they are a piece trailing off, and they "
            + "are what every draft has closed on so far. If the reader finishes and does not know "
            + "what they are being asked to do, the ending has failed.";
    }

    private static readonly string LedeLengthInstruction =
        $"LENGTH: the opening runs {ContentLengthTargets.LedeRangeLabel} words across 3-4 paragraphs, " +
        $"and no paragraph in it is shorter than {ContentLengthTargets.LedeParagraphMinWords} words. " +
        "This is the paragraph that decides whether the rest gets read, so give it room: the hook, " +
        "the turn that names what is at stake, and the line that says who this is for and what they " +
        "get. A three-sentence opening is not a short opening, it is an opening that has not started.";

    /// <summary>
    /// The lede is the lead paragraph. It sits directly under the headline and has no headline of
    /// its own -- which is why there is no "heading" here.
    ///
    /// <para>
    /// It asked for one until 2026-09-23, and the lede was stored as a Section and rendered through
    /// the same path as a body section, so every page carried two headlines stacked: the title, then
    /// the lede's. That is the redundancy Jeff reported as "How Automated Data Entry &amp; Processing
    /// Can Transform Your Business then Transform Your Business with Automated Data Entry &amp;
    /// Processing seem redundant" -- which I treated as a wording problem and answered with an
    /// instruction not to restate the title, when the lede should never have had a heading at all.
    /// The twelve types are the tell: summary, anecdotal, narrative, question, startling statement
    /// are kinds of opening <i>paragraph</i>. Nobody picks "anecdotal" for a section heading.
    /// </para>
    /// </summary>
    private const string LedeJsonContract =
        "{\"ledeType\": \"summary\"|\"immediateIdentification\"|\"delayedIdentification\"|\"singleItem\"|\"anecdotal\"|\"narrative\"|\"sceneSetting\"|\"startlingStatement\"|\"directAddress\"|\"question\"|\"quote\"|\"wordplay\", " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...] (the opening itself -- no heading: it runs directly under the page title)" +
        "}";

    /// <summary>
    /// The introduction has no heading either. It is the opening continuing, not a first section --
    /// it used to take the full section shape, heading included, which is how a pillar could end up
    /// with the title, a lede headline and then a third headline before any body section.
    /// </summary>
    private const string IntroductionJsonContract =
        "{\"paragraphs\": [" + ParagraphJsonShape + ", ...] (continues the lede; no heading), " +
        "\"children\": [" + SectionJsonContract + ", ...] (optional nested h3s)}";

    private const string LedeAndIntroductionJsonContract =
        "{\"lede\": " + LedeJsonContract + ", \"introduction\": " + IntroductionJsonContract + "}";

    /// <summary>
    /// The angle as an instruction, not a token. This printed the raw enum -- "Angle:
    /// problem_solution" -- in a prompt where all twelve lede types carry a line explaining what
    /// they are, so the one control the operator uses to shape the piece was the only one the model
    /// had to guess at (Jeff, 2026-09-23: "Doesn't seem to be using Angle for Seo?").
    ///
    /// Vocabulary is CONTENT_ANGLES in brief-catalog.ts. An unrecognised value is passed through
    /// rather than dropped, so a new angle still reaches the model while it waits for a line here.
    /// </summary>
    private static string DescribeAngle(string angle) =>
        angle.Trim().ToLowerInvariant() switch
        {
            "problem_solution" =>
                "Angle -- Problem-Solution: open on the reader's problem and what it is costing them, "
                + "then show how this resolves it. The problem is the hook, not a preamble; earn the "
                + "solution by making the cost concrete first.",
            "comparative" =>
                "Angle -- Comparative (\"versus\"): frame against the alternatives this reader is "
                + "actually weighing. The value is in the contrast and the trade-offs, never a feature "
                + "list that ignores what else they could do.",
            "case_study_data" =>
                "Angle -- Case Study / Data-Driven: lead with evidence -- a number, an outcome, a "
                + "documented result -- and let the argument follow from it. Never invent a figure to "
                + "carry this angle; if the evidence is not in what you were given, argue from what is.",
            "ultimate_guide" =>
                "Angle -- Comprehensive \"Ultimate Guide\": the promise is completeness. Breadth and "
                + "structure carry it: cover the whole territory in an order a reader can follow.",
            _ => $"Angle: {angle}",
        };

    private static string BuildLedeTypeGuidance(ProjectGenerationContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Lede types (pick ONE ledeType that best fits this audience + angle + topic):");
        sb.AppendLine("- summary: direct thesis-first overview (what/why).");
        sb.AppendLine("- immediateIdentification: lead names the who/what up front.");
        sb.AppendLine("- delayedIdentification: hold identity for reveal after hook.");
        sb.AppendLine("- singleItem: spotlight one striking example/data point.");
        sb.AppendLine("- anecdotal: brief human story or vignette.");
        sb.AppendLine("- narrative: chronological arc or journey.");
        sb.AppendLine("- sceneSetting: vivid place/time establishing context.");
        sb.AppendLine("- startlingStatement: bold, counterintuitive claim.");
        sb.AppendLine("- directAddress: speak directly to reader (you/your).");
        sb.AppendLine("- question: open with a compelling question.");
        sb.AppendLine("- quote: open with a relevant quotation.");
        sb.AppendLine("- wordplay: clever phrasing or pun (use sparingly, only if topic allows).");
        // "summary" is the one a model reaches for unprompted on technical B2B material, and it is
        // listed first here, which makes it the anchor. Jeff, 2026-09-23: "Overview is a boring
        // type of lede, that does not pique interest." It is still correct sometimes -- a reader
        // with transactional or navigational intent wants the answer in the first line, not a
        // scene -- so this biases against it rather than banning it.
        sb.AppendLine();
        // The three lines that stood here told the model not to let the lede's heading restate the
        // page title. The lede has no heading any more -- it is the lead paragraph, under the title
        // -- so that was instruction about a slot that no longer exists, spending prompt space and
        // describing a shape the contract contradicts.

        sb.AppendLine("Choosing: \"summary\" is the weakest hook and the one most often reached for by default.");
        sb.AppendLine("Use it only when the brief's intent is transactional or navigational, or the reader");
        sb.AppendLine("genuinely needs the answer in the opening line.");
        sb.AppendLine("Otherwise open with a story. Prefer anecdotal, narrative or sceneSetting: put a");
        sb.AppendLine("person in a situation the reader recognises and let the problem show up in what");
        sb.AppendLine("happens to them, before any explanation of it. \"Picture your accounts team");
        sb.AppendLine("struggling through stacks of invoices, each one a potential error waiting to");
        sb.AppendLine("happen\" is the shape -- concrete, peopled, in motion.");
        sb.AppendLine("directAddress, question, startlingStatement and delayedIdentification are the");
        sb.AppendLine("fallbacks when the material genuinely has no scene in it -- not the default. They");
        sb.AppendLine("are safer to write and that is exactly why they keep getting chosen: a page that");
        sb.AppendLine("opens by addressing the reader in the abstract has stated a topic, not started a");
        sb.AppendLine("piece of writing.");
        // Worked examples, supplied by Jeff 2026-09-23. A one-line definition tells the model what
        // a type is called; it does not show the craft -- the specificity, the concrete detail, the
        // withheld name, the second person. These demonstrate the technique.
        //
        // The subject matter of the examples is deliberately irrelevant and the instruction says so
        // twice: few-shot examples are copied as readily as they are learned from, and a lede that
        // borrows "2 a.m." or a server room from here would be worse than no example at all.
        sb.AppendLine();
        sb.AppendLine("Examples of the craft each type calls for. TWO per type, from two unrelated");
        sb.AppendLine("subjects on purpose: match the TECHNIQUE they share, never their wording, imagery");
        sb.AppendLine("or subject matter. Note the shape -- a hook sentence, then a second sentence that");
        sb.AppendLine("turns it into what the page is about.");
        sb.AppendLine("- anecdotal/narrative:");
        sb.AppendLine("  (a) \"Sarah Jenkins stared at her computer screen at 2 a.m., watching a lines-of-code "
            + "algorithm generate a flawless, professional marketing strategy in under four seconds -- a task that "
            + "normally took her entire team a full workweek to complete.\"");
        sb.AppendLine("  (b) \"Dr. Aris Thorne spent three grueling years reviewing thousands of anonymous patient "
            + "lung scans, searching for microscopic anomalies that the human eye routinely misses. Yesterday, he "
            + "loaded those same images into a new neural network, which flagged every single early-stage tumor in "
            + "less time than it took him to pour a cup of coffee.\"");
        sb.AppendLine("- sceneSetting:");
        sb.AppendLine("  (a) \"Inside the climate-controlled server room, the air hums with a low, collective roar "
            + "as thousands of blinking green lights flicker in the dark, processing billions of data points every "
            + "second to rewrite the future of human labor.\"");
        sb.AppendLine("  (b) \"The oncology ward at St. Jude's is uncharacteristically quiet, save for the soft "
            + "rhythmic beeping of vitals monitors and the faint clicking of a nearby keyboard. On that screen, a "
            + "newly deployed diagnostic algorithm is quietly solving a catastrophic medical bottleneck.\"");
        sb.AppendLine("- delayedIdentification:");
        sb.AppendLine("  (a) \"A quiet, invisible companion now sits at the desk of nearly every modern white-collar "
            + "professional, drafting their emails, analyzing their financial spreadsheets, and silently transforming "
            + "the workforce without ever collecting a paycheck.\"");
        sb.AppendLine("  (b) \"A silent diagnostic partner is entering rural medical clinics across the country, "
            + "reviewing patient records at lightning speed to catch deadly medical oversights before they happen. "
            + "This new automated software is solving America's critical radiologist shortage.\"");
        sb.AppendLine("- startlingStatement:");
        sb.AppendLine("  (a) \"By the time you finish reading this sentence, an automated program will have generated "
            + "enough text online to fill an entire library encyclopedia, fundamentally altering how humanity creates "
            + "and consumes information.\"");
        sb.AppendLine("  (b) \"Half of all malignant lung tumors are caught too late for effective treatment, a tragic "
            + "reality driven by a global shortage of expert medical eyes. But a radical shift in computer vision is "
            + "quietly wiping this problem away.\"");
        sb.AppendLine("- directAddress:");
        sb.AppendLine("  (a) \"Think about the last time you asked an online customer service agent a question, "
            + "received a perfect response in seconds, and closed the window -- unknowingly interacting with a system "
            + "that possesses more collective data than any human mind in history.\"");
        sb.AppendLine("  (b) \"Imagine waiting weeks for a critical medical scan, knowing that a single missed pixel "
            + "on your X-ray could mean the difference between life and death. Now imagine a system that scans your "
            + "files instantly and spots anomalies your doctor might miss.\"");
        sb.AppendLine();
        sb.AppendLine("A third set, in the back-office automation space. These show the level of CONCRETE");
        sb.AppendLine("DETAIL a good lede carries -- a named tool, a real number, a specific task -- not the");
        sb.AppendLine("vague abstraction most drafts open with. Because these are close to the subject matter");
        sb.AppendLine("you may be writing about, the reuse rule is absolute: never repeat their names");
        sb.AppendLine("(Marcus Vance, QuickBooks), their figures (fifty invoices, eighty percent), their");
        sb.AppendLine("businesses or their scenes. Take the register and the specificity; invent your own");
        sb.AppendLine("particulars from the brief and the evidence you were given.");
        sb.AppendLine("- anecdotal/narrative: \"Marcus Vance spent every Sunday afternoon buried under a mountain "
            + "of physical invoices, manually matching line items to receipts for his local hardware store. Last "
            + "week, he finally deployed a custom AI agentic workflow that parsed, verified, and logged fifty "
            + "invoices into QuickBooks in the time it took him to open his laptop.\"");
        sb.AppendLine("- sceneSetting: \"The main office of the local distribution center is dead quiet at midnight, "
            + "save for the hum of a single desktop computer and the stack of unentered billing receipts waiting for "
            + "morning. But behind the screen, an automated data pipeline is silently running.\"");
        sb.AppendLine("- delayedIdentification: \"A tireless new worker has quietly joined the administrative teams "
            + "of several local businesses, managing complex data entries and accounts payable around the clock. "
            + "This custom automation software is permanently solving the manual bottlenecks that stall small "
            + "business growth.\"");
        sb.AppendLine("- startlingStatement: \"Nearly eighty percent of small business owners report that "
            + "administrative tasks like manual data entry and billing reconciliation are the leading barriers to "
            + "their company's growth. A radical shift in automated accounting workflows is now erasing this "
            + "problem.\"");
        sb.AppendLine("- directAddress: \"Imagine spending your Sunday evenings manually typing invoice numbers into "
            + "a spreadsheet instead of being with your family, knowing a single typo could derail your monthly "
            + "financial reports. Now imagine a custom AI pipeline that handles that entire workload for you "
            + "instantly.\"");
        sb.AppendLine();
        sb.AppendLine("Soft/indirect ledes need that turn -- a nutgraf immediately after the hook, carrying the "
            + "reader from the opening image to what this page is actually about.");
        var hasBrief = !string.IsNullOrWhiteSpace(context.AudienceSegment) || !string.IsNullOrWhiteSpace(context.AudienceNotes) || !string.IsNullOrWhiteSpace(context.ContentAngle)
            || !string.IsNullOrWhiteSpace(context.PrimaryIntent) || !string.IsNullOrWhiteSpace(context.BuyingStage) || !string.IsNullOrWhiteSpace(context.ToneOfVoice)
            || !string.IsNullOrWhiteSpace(context.CtaType) || !string.IsNullOrWhiteSpace(context.LengthBand) || !string.IsNullOrWhiteSpace(context.WritingNotes)
            || context.EeatSignals is { Count: > 0 };
        if (hasBrief)
        {
            if (!string.IsNullOrWhiteSpace(context.PrimaryIntent))
                sb.AppendLine($"Primary intent: {context.PrimaryIntent}" + (string.IsNullOrWhiteSpace(context.SecondaryIntent) ? "" : $" (secondary: {context.SecondaryIntent})"));
            if (!string.IsNullOrWhiteSpace(context.BuyingStage))
                sb.AppendLine($"Buying stage: {context.BuyingStage}");
            if (!string.IsNullOrWhiteSpace(context.AudienceSegment))
                sb.AppendLine($"Audience: {context.AudienceSegment}" + (context.AudienceDetails is { Count: > 0 } d ? $" — details: {string.Join(", ", d)}" : "") + (string.IsNullOrWhiteSpace(context.AudienceNotes) ? "" : $" — notes: {context.AudienceNotes}"));
            else if (!string.IsNullOrWhiteSpace(context.AudienceNotes))
                sb.AppendLine($"Audience notes: {context.AudienceNotes}");
            if (context.AudienceDetails is { Count: > 0 } details && string.IsNullOrWhiteSpace(context.AudienceSegment))
                sb.AppendLine($"Audience details: {string.Join(", ", details)}");
            if (!string.IsNullOrWhiteSpace(context.ContentAngle))
                sb.AppendLine(DescribeAngle(context.ContentAngle));
            if (!string.IsNullOrWhiteSpace(context.ToneOfVoice))
                sb.AppendLine($"Tone of voice: {context.ToneOfVoice}" + (context.EeatSignals is { Count: > 0 } ee ? $" — E-E-A-T: {string.Join(", ", ee)}" : ""));
            else if (context.EeatSignals is { Count: > 0 } eeOnly)
                sb.AppendLine($"E-E-A-T: {string.Join(", ", eeOnly)}");
            if (!string.IsNullOrWhiteSpace(context.CtaType))
                sb.AppendLine($"CTA: {context.CtaType}" + (string.IsNullOrWhiteSpace(context.CtaLabel) ? "" : $" — label: {context.CtaLabel}"));
            if (!string.IsNullOrWhiteSpace(context.LengthBand))
                sb.AppendLine($"Length band: {context.LengthBand}");
            if (!string.IsNullOrWhiteSpace(context.WritingNotes))
                sb.AppendLine($"Writing notes: {context.WritingNotes}");
            sb.AppendLine("Lede guidance by angle:");
            sb.AppendLine("  comparative → prefers question, startlingStatement, singleItem (stakes/contrast)");
            sb.AppendLine("  problem_solution → prefers anecdotal, sceneSetting, directAddress, question (pain-first)");
            sb.AppendLine("  case_study_data → prefers immediateIdentification, singleItem, quote, startlingStatement (evidence-first)");
            sb.AppendLine("  ultimate_guide → prefers summary, delayedIdentification, directAddress (comprehensive framing)");
            sb.AppendLine("Lede guidance by audience:");
            sb.AppendLine("  affinity/in_market → more narrative/anecdotal room");
            sb.AppendLine("  detailed_demographics/your_data → more directAddress/question");
            sb.AppendLine("Lede guidance by intent/funnel:");
            sb.AppendLine("  informational → summary/narrative/sceneSetting; transactional/commercial_investigation → directAddress/question/singleItem; navigational → immediateIdentification");
            sb.AppendLine("  awareness → anecdotal/narrative/sceneSetting; consideration → comparative/question; action → directAddress/singleItem");
            sb.AppendLine("If audience notes conflict with segment, follow notes. Tone and E-E-A-T must be honored in lede voice.");
        }
        sb.Append("Pick ONE ledeType from the 12 that best fits this brief (audience + angle + intent/funnel/tone) + heading/topic.");
        return sb.ToString();
    }

    private static string BuildBriefBodyGuidance(ProjectGenerationContext context)
    {
        var sb = new StringBuilder();
        var hasAny = !string.IsNullOrWhiteSpace(context.PrimaryIntent) || !string.IsNullOrWhiteSpace(context.BuyingStage) || !string.IsNullOrWhiteSpace(context.ToneOfVoice)
            || !string.IsNullOrWhiteSpace(context.CtaType) || !string.IsNullOrWhiteSpace(context.LengthBand) || !string.IsNullOrWhiteSpace(context.WritingNotes)
            || context.EeatSignals is { Count: > 0 };
        if (!hasAny) return string.Empty;
        sb.AppendLine("=== BRIEF CONTROLS (honor in body) ===");
        if (!string.IsNullOrWhiteSpace(context.PrimaryIntent))
            sb.AppendLine($"Primary intent: {context.PrimaryIntent}" + (string.IsNullOrWhiteSpace(context.SecondaryIntent) ? "" : $" + {context.SecondaryIntent}"));
        if (!string.IsNullOrWhiteSpace(context.BuyingStage))
            sb.AppendLine($"Buying stage: {context.BuyingStage} — align examples/CTAs to funnel (awareness=educate, consideration=compare, action=convert).");
        if (!string.IsNullOrWhiteSpace(context.ToneOfVoice))
            sb.AppendLine($"Tone of voice: {context.ToneOfVoice} — hold this voice throughout (consultant_professional=objective authority, informational_instructional=clear stepwise, commercial_balanced=balanced benefits/tradeoffs).");
        if (context.EeatSignals is { Count: > 0 } ee2)
            sb.AppendLine($"E-E-A-T signals to demonstrate: {string.Join(", ", ee2)}.");
        if (!string.IsNullOrWhiteSpace(context.CtaType))
            sb.AppendLine($"CTA: {context.CtaType}" + (string.IsNullOrWhiteSpace(context.CtaLabel) ? "" : $" ({context.CtaLabel})") + " — weave naturally into closing, not forced.");
        if (!string.IsNullOrWhiteSpace(context.LengthBand))
            sb.AppendLine($"Length band: {context.LengthBand} — respect target length.");
        if (!string.IsNullOrWhiteSpace(context.WritingNotes))
            sb.AppendLine($"Writing notes: {context.WritingNotes}");
        return sb.ToString();
    }

    private static ChatCompletionRequest WithSectionSchema(ChatCompletionRequest request) =>
        request with { JsonSchemaName = "section", JsonSchema = ContentSectionJsonSchema.SectionSchema };

    private static ChatCompletionRequest WithSectionsArraySchema(ChatCompletionRequest request) =>
        request with { JsonSchemaName = "sections", JsonSchema = ContentSectionJsonSchema.SectionsArraySchema };

    public ChatCompletionRequest BuildTopicFocusPrompt(string siteName, IReadOnlyList<string> headings, IReadOnlyList<string> paragraphs)
    {
        var headingBlock = string.Join("\n", headings.Take(60).Select(h => $"- {h}"));
        var paragraphBlock = string.Join("\n\n", paragraphs.Take(30).Select(p => p.Length > 300 ? p[..300] + "…" : p));

        var system = new StringBuilder()
            .AppendLine("You extract the real topical focus of a business website from its crawled headings and body text.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(TopicFocusJsonContract)
            .AppendLine("Each phrase must name a real service, product, industry, or subject the site actually covers (e.g. \"managed IT services\", ")
            .AppendLine("\"AI implementation\", \"Salesforce consulting\") — never a generic word like \"business\", \"solutions\", \"help\", \"choose\", or \"build\" on its own.")
            .AppendLine("Prefer multi-word phrases over single words. If the site is thin/generic, return fewer, more honest phrases rather than padding with filler.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Site name: {siteName}")
            .AppendLine()
            .AppendLine("Headings crawled from the site:")
            .AppendLine(headingBlock)
            .AppendLine()
            .AppendLine("Body text excerpts crawled from the site:")
            .AppendLine(paragraphBlock)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.2,
            MaxOutputTokens: 512);
    }

    public ChatCompletionRequest BuildUseCaseExtractionPrompt(string siteName, IReadOnlyList<string> homeHeadings, IReadOnlyList<string> homeParagraphs)
    {
        var headingBlock = string.Join("\n", homeHeadings.Take(60).Select(h => $"- {h}"));
        var paragraphBlock = string.Join("\n\n", homeParagraphs.Take(40).Select(p => p.Length > 300 ? p[..300] + "…" : p));

        var system = new StringBuilder()
            .AppendLine("You extract named use-case / service listing items from a business's Home page (crawled headings and body text only — no HTML markup or links are visible to you).")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(UseCaseExtractionJsonContract)
            .AppendLine("Only extract items from a genuine listing/showcase section (e.g. \"Our Use Cases\", \"Services\", \"What We Do\") where each item has its own distinct name — ")
            .AppendLine("never invent items, and never extract generic nav/footer links or one-off mentions in prose.")
            .AppendLine("Group items under the category heading they're actually listed under on the page (e.g. \"Accounting\", \"Customer Service\") — use the item's own immediate parent heading, not the page title.")
            .AppendLine("You cannot see real links from crawled text alone — always return href as null.")
            .AppendLine("Return an empty items array if the page has no such listing section.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Site name: {siteName}")
            .AppendLine()
            .AppendLine("Headings crawled from the Home page:")
            .AppendLine(headingBlock)
            .AppendLine()
            .AppendLine("Body text excerpts crawled from the Home page:")
            .AppendLine(paragraphBlock)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.2,
            MaxOutputTokens: 1024);
    }

    private const string ArticleMetadataJsonContract =
        "{\"title\": string, \"summary\": string (the standfirst: one or two sentences placed directly under the H1, stating the promise this page makes to the reader in plain language — not the meta description reworded, not a list of what the page covers), \"metaDescription\": string (140-160 characters, must include the target keyword naturally, no hype), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-7 declarative H2 headings, plus final item: \"People Also Ask\")}";

    private const string SocialJsonContract =
        "{\"text\": string}";

    private const string ColdOutreachJsonContract =
        "{\"subject\": string, \"bodyText\": string (50-125 words), \"ctaLabel\": string}";

    private const string ImagePromptSectionItemJsonContract =
        "{\"sourceType\": \"pillar-hero|blog-hero|pillar|blog\", \"heading\": string (exact H2 text, or the exact title for a -hero item), \"order\": number, \"prompt\": string (40-400 words), \"width\": number, \"height\": number, \"imageModel\": string, \"stylePreset\": string, \"alchemy\": boolean, \"photoReal\": boolean, \"notes\": string|null}";

    private const string ImagePromptSectionsJsonContract =
        "{\"sections\": [" + ImagePromptSectionItemJsonContract + ", ...]}";

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
            "Do not include a Tools H2. Tool names from the crawl belong in body sentences later, not as outline headings. " +
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

    public ChatCompletionRequest BuildArticleLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string? revisionNotes = null,
        string? existingLedeHeading = null)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write the opening lede for a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("Do NOT start with \"How\" or a question.")
            .AppendLine("PAIN BEFORE SOLUTION (required): the first paragraph must open on the practitioner's pain with the manual / status-quo process ")
            .AppendLine("for the target keyword (cost, delay, error, risk, wasted hours) — before naming AI or an intelligent solution.")
            .AppendLine("Only after that pain is established, introduce how an AI-assisted approach changes the situation.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

        var ledeNotes = ScopeRevisionNotesForLede(revisionNotes, existingLedeHeading, metadata.SectionOutline);
        var revisionBlock = BuildRevisionNotesBlock(ledeNotes, sectionHeading: existingLedeHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.65,
            MaxOutputTokens: 2048);
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
        string? existingLedeHeading = null)
    {
        var outlineContext = RenderOutline(fullOutline);

        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Tone: {context.ImplementerPositioning} — audience×angle sets ledeType and voice (audience + angle + topic → 12 types); keep expert, consultative tone throughout.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine()
            .AppendLine("Produce the pillar's opening — the lead paragraphs that run directly under the page title. No heading of any kind: the title is the page's only headline.")
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("Do NOT start with \"How\" or a question unless ledeType is Question.")
            .AppendLine("PAIN BEFORE SOLUTION (required): the first paragraph must open on the practitioner's pain with the manual / status-quo process ")
            .AppendLine("for the target keyword (cost, delay, error, risk, wasted hours) — before naming AI or an intelligent solution.")
            .AppendLine("Only after that pain is established, introduce how an AI-assisted approach changes the situation.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine()
            .AppendLine("The introduction continues the same opening — it is not a second start:")
            .AppendLine("After the hook, carry straight on into scoping (who this is for, what the article walks through). Never a duplicate hook, and never a heading.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Include 2-3 h3 subsections nested in \"children\" with multiple text paragraphs, and at least one list paragraph where appropriate.")
            .AppendLine("Each h3 is a keyword-level topic and MUST itself nest 1-3 h4 children covering concrete subtopics of that h3.")
            .AppendLine("Do not leave an h3 as a leaf with only paragraphs — every h3 needs at least one substantive h4 child.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch, but MUST be explicitly labeled hypothetical/illustrative.")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage — vary outcomes and make them operationally specific.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for the Introduction section.")
            .AppendLine(BuildIntroductionSectionGuidance(context))
            .AppendLine()
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine("Always include both \"lede\" and \"introduction\" keys. Neither carries a heading — they are one continuous opening, and the introduction's paragraphs follow the lede's.")
            .AppendLine(LedeAndIntroductionJsonContract)
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh prose and examples.";
        }

        var ledeNotes = ScopeRevisionNotesForLede(revisionNotes, existingLedeHeading, metadata.SectionOutline);
        var ledeRevisionBlock = BuildRevisionNotesBlock(ledeNotes, sectionHeading: existingLedeHeading);
        if (ledeRevisionBlock is not null)
        {
            system += Environment.NewLine + "LEDE " + ledeRevisionBlock;
        }

        var introRevisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: ledeHeading);
        if (introRevisionBlock is not null)
        {
            system += Environment.NewLine + "INTRODUCTION " + introRevisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
            .AppendLine($"Write the pillar's Lede (first H2) {ledeIndex + 1} of {totalSections}. It covers: {ledeHeading}. You write its heading.")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .AppendLine()
            .AppendLine("Full article outline (for context only — write ONLY the Lede H2):")
            .AppendLine(outlineContext)
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 6144);
    }

    public ChatCompletionRequest? BuildArticleMetaRevisionPrompt(
        ProjectGenerationContext context,
        string title,
        string metaDescription,
        string revisionNotes)
    {
        var metaNotes = ScopeRevisionNotesForMeta(revisionNotes);
        if (string.IsNullOrWhiteSpace(metaNotes))
        {
            return null;
        }

        var system = new StringBuilder()
            .AppendLine("You revise SEO title and meta description for a TechnicalArticle pillar.")
            .AppendLine("Apply ONLY the reviewer's meta/title notes below. Keep the plan/outline unchanged.")
            .AppendLine("Meta description must be 140-160 characters, factual, no hype words like \"cutting-edge\".")
            .AppendLine("Change the title only when a note explicitly targets Title; otherwise return the current title unchanged.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine("{\"title\": string, \"metaDescription\": string}")
            .AppendLine()
            .AppendLine("REVISION REQUIRED — address each of the following:")
            .AppendLine(metaNotes.Trim())
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Current title: {title}")
            .AppendLine($"Current meta description ({metaDescription.Length} chars): {metaDescription}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.3,
            MaxOutputTokens: 512);
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
        Section? lede = null)
    {
        var outlineContext = RenderOutline(fullOutline);
        var namesItsOwn = slots.Any(sl => sl.WritesItsOwnHeading);
        var headingsList = string.Join(Environment.NewLine, slots.Select((sl, i) =>
            sl.WritesItsOwnHeading
                ? $"{i + 1}. Cover: {sl.Covers}"
                    + (sl.Depth is { Length: > 0 } ? $" (roughly {sl.Depth})" : string.Empty)
                    + (sl.Guidance is { Length: > 0 } ? Environment.NewLine + $"   {sl.Guidance}" : string.Empty)
                : $"{i + 1}. \"{sl.Heading}\""));

        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(briefBody)
            .AppendLine(FillerBanInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine($"Write {slots.Count} sections of a schema.org TechnicalArticle pillar in one response — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Respond with ONLY the sections array, one entry per section listed below, in the same order — no code fences, no commentary:")
            .AppendLine(requireHeadingProvenance ? SectionsArrayJsonContractWithProvenance : SectionsArrayJsonContract)
            .AppendLine("Each section's own tag is \"h2\". Use nested h3 children where a section genuinely has distinct parts, and h4 under an h3 only when that part itself divides — depth where the material has depth, not a fixed lattice on every section.")
            .AppendLine(SectionVarietyInstruction)
            .AppendLine("Open each section where its own material starts. Somewhere early in the page the practitioner's cost — the delay, the error rate, the wasted hours of the status quo — has to be concrete, but it is one page making one argument: do not restate the pain at the top of every section, and never open with \"AI enables…\", \"Intelligent X is…\", a capability list, or a definition of the technology.")
            .AppendLine("Do not write these as neutral textbook explainers — every subsection should be framed through what an AI implementation " +
                $"consultancy like {context.PublisherName} ({context.ImplementerPositioning}) actually does about the problem being discussed, not just background education on it.")
            .AppendLine("Do NOT repeat the same point, example, or framing across sections in this batch — each must cover genuinely distinct ground.")
            .AppendLine("If a hypothetical scenario is used, keep it to 1-2 sentences woven naturally into the surrounding paragraph.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch, but MUST be explicitly labeled hypothetical/illustrative. ")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage across sections — vary outcomes and make them operationally specific.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for EACH section.")
            .AppendLine("With the exception of the Lede, article headings are never questions.")
            .AppendLine("Tools listed in the research brief must be woven into sentences where they are relevant to this section — never as a Tools heading or catalog.")
            .AppendLine(ClosingCallToActionInstruction(context))
            .ToString();

        if (namesItsOwn)
        {
            system += Environment.NewLine + HeadingCraftInstruction;
        }

        var continuity = BuildLedeContinuityBlock(lede);
        if (continuity is not null)
        {
            system += Environment.NewLine + continuity;
        }

        if (requireHeadingProvenance)
        {
            if (!string.IsNullOrWhiteSpace(evidenceBlock))
            {
                system += Environment.NewLine + evidenceBlock;
            }

            system += Environment.NewLine + HeadingProvenanceInstruction +
                " Each top-level section here fulfils one of the numbered sections you were assigned" +
                " above — tag its own provenance \"plan\", whether the heading was given to you or you" +
                " wrote it yourself. Every h3/h4 child nested under it is yours to invent, and each of" +
                " those needs a real tag from the rules above.";
        }

        // Per-heading guidance — these blocks are pure functions of context (not the loop index),
        // so appending each one that applies across the whole batch is safe even combined into a
        // single call, as long as they're clearly scoped to the heading they apply to.
        foreach (var heading in slots.Select(sl => sl.Label))
        {
            if (PillarSectionClassifier.IsBenefitsSection(heading))
            {
                system += Environment.NewLine + $"For \"{heading}\":" + Environment.NewLine + BuildBenefitsSectionGuidance(context);
            }
            if (PillarSectionClassifier.IsBestPracticesSection(heading))
            {
                system += Environment.NewLine + $"For \"{heading}\":" + Environment.NewLine + BuildBestPracticesSectionGuidance(context);
            }
            if (PillarSectionClassifier.IsFutureTrendsSection(heading))
            {
                system += Environment.NewLine + $"For \"{heading}\":" + Environment.NewLine + BuildFutureTrendsSectionGuidance(context);
            }
        }

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh prose and examples.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
            if (slots.Any(sl => PillarSectionClassifier.IsBenefitsSection(sl.Label)) || NotesAskForConcreteness(revisionNotes, string.Empty))
            {
                system += Environment.NewLine + BuildConcretenessRevisionAmplifier();
            }
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
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
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 16384));
    }

    public ChatCompletionRequest BuildArticleSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string sectionHeading,
        int sectionIndex,
        int totalSections,
        IReadOnlyList<string> fullOutline,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var outlineContext = string.Join("\n", fullOutline.Select((h, i) => $"{i + 1}. {h}"));
        var isBestPractices = PillarSectionClassifier.IsBestPracticesSection(sectionHeading);
        var isBenefits = PillarSectionClassifier.IsBenefitsSection(sectionHeading);
        var isIntroduction = PillarSectionClassifier.IsIntroductionSection(sectionHeading);
        var isImplementation = PillarSectionClassifier.IsImplementationSection(sectionHeading);
        var isFutureTrends = PillarSectionClassifier.IsFutureTrendsSection(sectionHeading);

        // Fully static — identical across every pillar-section call in a run, so it forms a stable
        // prefix OpenAI's automatic prompt caching can discount. Anything that varies per call
        // (section-type guidance, the assigned heading, revision notes) lives in the user message
        // instead, after the static research brief — see ResearchBriefBuilder.Build's no-instructions
        // overload below.
        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(briefBody)
            .AppendLine("Write ONE section of a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Pillar standard ({ContentLengthTargets.PillarRangeLabel} words): {ContentLengthTargets.PillarEditorialDefinition}")
            .AppendLine("Respond with ONLY a single valid JSON Section object for this section — no code fences, no commentary, no other sections.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h2\". Do NOT write introductory paragraphs before it — the opening lede is generated separately.")
            .AppendLine("Include 2-3 h3 subsections nested in \"children\" with multiple text paragraphs, and at least one list paragraph where appropriate.")
            .AppendLine("Each h3 is a keyword-level topic and MUST itself nest 1-3 h4 children covering concrete subtopics of that h3 ")
            .AppendLine("(e.g. h3 \"AI Marketing Workflow\" → h4 \"AI Content Creation and Repurposing for Small Businesses\"). ")
            .AppendLine("Do not leave an h3 as a leaf with only paragraphs — every h3 needs at least one substantive h4 child.")
            .AppendLine("PROBLEM-FIRST OPENING (required): the first paragraph under this H2 must open on the practitioner problem this section addresses ")
            .AppendLine("(cost, delay, error, risk, wasted effort). Technology and capability come after that pain is clear.")
            .AppendLine("Do NOT open with \"AI enables…\", \"Intelligent X is…\", a product capability list, or a definition of the technology.")
            .AppendLine("Do not write this as a neutral textbook explainer of the general subject — every subsection should be framed through what an AI implementation " +
                $"consultancy like {context.PublisherName} ({context.ImplementerPositioning}) actually does about the problem being discussed, not just background education on it. " +
                "A reader should finish the section understanding a consultancy's specific angle on it, not just the general concept.")
            .AppendLine("If a hypothetical scenario is used, keep it to 1-2 sentences woven naturally into the surrounding paragraph — not a bolt-on closing paragraph that repeats what was already said.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch (e.g. \"month-end close compressed from two weeks to three days\"), ")
            .AppendLine("but it MUST be explicitly labeled hypothetical/illustrative — e.g. \"a hypothetical mid-sized manufacturer\" or ")
            .AppendLine("\"in a representative scenario\". Never phrase it as something that already happened to a real client. ")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage across sections — vary outcomes and make them operationally specific.")
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for this section. Do not write other sections.")
            .AppendLine("With the exception of the Lede, article headings are never questions.")
            .AppendLine("Tools listed in the research brief must be woven into sentences where they are relevant to this section — never as a Tools heading or catalog.")
            .AppendLine(ClosingCallToActionInstruction(context))
            .ToString();

        var perCall = new StringBuilder();

        if (isIntroduction)
        {
            perCall.AppendLine(BuildIntroductionSectionGuidance(context));
        }

        if (isBenefits)
        {
            perCall.AppendLine(BuildBenefitsSectionGuidance(context));
        }

        if (isImplementation)
        {
            perCall.AppendLine(BuildImplementationSectionGuidance(context));
        }

        if (isBestPractices)
        {
            perCall.AppendLine(BuildBestPracticesSectionGuidance(context));
        }

        if (isFutureTrends)
        {
            perCall.AppendLine(BuildFutureTrendsSectionGuidance(context));
        }

        if (isRegeneration)
        {
            perCall.AppendLine("REGENERATION: use fresh prose and examples.");
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: sectionHeading);
        if (revisionBlock is not null)
        {
            perCall.AppendLine(revisionBlock);
            if (isBenefits || NotesAskForConcreteness(revisionNotes, sectionHeading))
            {
                perCall.AppendLine(BuildConcretenessRevisionAmplifier());
            }
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
            .Append(perCall)
            .AppendLine($"Write section {sectionIndex + 1} of {totalSections}: \"{sectionHeading}\".")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Section to write: {sectionHeading}")
            .AppendLine()
            .AppendLine("Full article outline (for context only — write ONLY the assigned section):")
            .AppendLine(outlineContext)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: PillarSectionMaxOutputTokens));
    }

    public ChatCompletionRequest BuildToolsPlatformListPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(briefBody)
            .AppendLine("Choose 4-5 major platforms or tools for the Tools H2 of a TechnicalArticle pillar.")
            .AppendLine("Only real, verifiable, well-known products relevant to the target keyword. Never invent a tool name or vendor.")
            .AppendLine("Prefer depth on 4 platforms over shallow coverage of 6.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine("{\"platforms\": string[] (4-5 product names, display order)}")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: pick a fresh but still accurate platform set when the notes call for it; otherwise keep strong existing choices.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: toolsSectionHeading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection,
                $"List platforms for Tools section \"{toolsSectionHeading}\"."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: isRegeneration ? 0.55 : 0.4,
            MaxOutputTokens: 512);
    }

    public ChatCompletionRequest BuildToolsPlatformChildPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string toolsSectionHeading,
        string platformName,
        IReadOnlyList<string> allPlatforms,
        int platformIndex,
        int platformCount,
        bool isRegeneration,
        string? revisionNotes = null,
        string? crawlHref = null)
    {
        var perPlatformTarget =
            $"{ContentLengthTargets.PillarToolsSectionMinWords / Math.Max(platformCount, 1)}" +
            $"-{ContentLengthTargets.PillarToolsSectionTargetMaxWords / Math.Max(platformCount, 1)}";

        var briefBody = BuildBriefBodyGuidance(context);
        // Fully static across every platform call in a run — see BuildArticleSectionPrompt's
        // identical rationale for why this needs to be a stable prefix for prompt caching.
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(briefBody)
            .AppendLine("Write ONE platform subsection for the Tools H2 of a TechnicalArticle pillar — third person, expert, consultative.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary, no other platforms.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's own tag is \"h3\". Heading must be exactly \"<a href=\"/tools/{department}/{slug}\">PlatformName</a>\" — enclose the platform name in an anchor tag linking to /tools/{department}/{slugified-platform-name} (e.g. <a href=\"/tools/marketing/tipalti\">Tipalti</a>), using the department from the project context and the slugified tool name.")
            .AppendLine("Include: a brief overview paragraph of what the platform does for this use case, then a list paragraph with 2-4 factual capability bullets.")
            .AppendLine("Then one child Section (tag h4, heading \"How an AI implementer helps with {Platform}\").")
            .AppendLine($"Target ~{perPlatformTarget} words for this platform subtree so the full Tools section lands near {ContentLengthTargets.PillarToolsSectionMinWords}-{ContentLengthTargets.PillarToolsSectionTargetMaxWords} words.")
            .AppendLine("Never invent a feature or capability; if unsure a feature exists, describe it generically instead of naming it.")
            .AppendLine("CRITICAL: there is no real case-study data available, so never present a named client, company, or engagement as if it were real. ")
            .AppendLine("A quantified outcome is fine only if explicitly labeled hypothetical/illustrative.")
            .ToString();

        var perCall = new StringBuilder();
        perCall.AppendLine(BuildToolsPlatformChildGuidance(context, platformName));

        if (isRegeneration)
        {
            perCall.AppendLine("REGENERATION: use fresh prose and examples.");
        }

        // Scope by platform name so Tool:/section notes about other platforms do not leak in.
        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: platformName);
        if (revisionBlock is not null)
        {
            perCall.AppendLine(revisionBlock);
        }

        var platformList = string.Join(", ", allPlatforms.Select((p, i) => i == platformIndex ? $"[{p}]" : p));
        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleSection))
            .AppendLine()
            .Append(perCall)
            .AppendLine($"Write Tools platform {platformIndex + 1} of {platformCount}: \"{platformName}\".")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Tools section heading: {toolsSectionHeading}")
            .AppendLine($"Platforms in this Tools section (write ONLY the bracketed one): {platformList}")
            .AppendLine($"Platform to write: {platformName}");
        if (!string.IsNullOrWhiteSpace(crawlHref))
        {
            user.AppendLine($"This platform was linked from the crawl at: {crawlHref}");
        }

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: isRegeneration ? 0.72 : 0.65,
            MaxOutputTokens: 2048));
    }

    public ChatCompletionRequest BuildArticleFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions,
        bool isRegeneration,
        string? revisionNotes = null)
    {
        var paaBlock = string.Join("\n", faqQuestions.Select((q, i) => $"  - Q{i + 1}: {q}"));

        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(briefBody)
            .AppendLine("Write ONLY the \"People Also Ask\" FAQ section of a TechnicalArticle pillar.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\" and heading is exactly \"People Also Ask\". Each question is a child Section: tag \"h3\", heading is the question verbatim, paragraphs holds a 2-4 sentence answer.")
            .AppendLine("Direct, factual answers. Third person.")
            .AppendLine($"Answers must sound like {context.PublisherName} ({context.ImplementerPositioning}), not a generic textbook FAQ — reflect the same consultative brand voice as the rest of the article, not interchangeable boilerplate.")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh phrasing.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: "People Also Ask");
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleFaq,
                "Write the People Also Ask FAQ section."))
            .AppendLine()
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine()
            .AppendLine("Questions to answer:")
            .AppendLine(paaBlock)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: isRegeneration ? 0.7 : 0.6,
            MaxOutputTokens: 3072));
    }

    private const string BlogMetadataJsonContract =
        "{\"title\": string, \"summary\": string (the standfirst: one or two sentences placed directly under the H1, stating the promise this page makes to the reader in plain language — not the meta description reworded, not a list of what the page covers), \"metaDescription\": string (max 160 chars), \"keywords\": string[] (5-10 items), \"sectionOutline\": string[] (5-6 conversational H2 headings — hooks, numbered angles, or how-to framing; do NOT copy pillar H2s verbatim; " +
        "each states that section's own specific claim about this subject, never a reusable label — no Overview, Introduction, Key Takeaways, Common Challenges, Best Practices or Final Thoughts)}";

    public ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(BlogMetadataJsonContract)
            .AppendLine("The blog title MUST be different from the pillar title — use a conversational hook, question, or numbered angle (e.g. \"3 Ways...\", \"Why...\"). Never copy the pillar title verbatim.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar article title (do NOT reuse): {sourceArticle.Title}")
            .AppendLine($"Pillar summary: {sourceArticle.MetaDescription}")
            .AppendLine()
            .AppendLine($"Plan a deep-dive companion blog ({ContentLengthTargets.BlogRangeLabel} words) with a distinct title, angle, and {ContentLengthTargets.BlogSectionCountMin}-{ContentLengthTargets.BlogSectionCountTarget} fresh H2 section headings.")
            .AppendLine($"Editorial standard: {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine(HierarchyChildOutlineInstruction(context, forPillarOrBlog: true))
            .AppendLine("Each section must support substantive depth — data points, examples, and implementation context, not surface summaries.")
            .AppendLine("Return title, summary, metaDescription, keywords, and sectionOutline only (body is written separately).")
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.6,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("The opening is the hook, then the turn that names what is at stake, then who this is for.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar title (reference only): {sourceArticle.Title}")
            .AppendLine($"Blog title: {metadata.Title}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.7,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildBlogBodyPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata, string? revisionNotes = null)
    {
        var pillarText = ContentDocumentText.Flatten(sourceArticle.Body);

        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("You are given the full text of an already-published pillar article. Repurpose it into a companion deep-dive blog post — ")
            .AppendLine("reframe, condense, and re-angle the pillar's own substance rather than inventing new research. ")
            .AppendLine("Do NOT duplicate the pillar's structure or reuse its H2 headings verbatim — use fresh headings (5-6 top-level sections) ")
            .AppendLine("that pick out a distinct angle or subset of the pillar's material (duplicate structure/headings across the two published pages hurts SEO).")
            .AppendLine("Substantive paragraphs with examples, drawn from what the pillar actually says; first/second person allowed.")
            .AppendLine("Weave the pillar's takeaways into this blog's own paragraphs. Name listed platforms in prose where they help the angle — a closing CTA is not weaving.")
            .AppendLine($"Target at least {ContentLengthTargets.BlogMinWords:N0} words (aim for {ContentLengthTargets.BlogRangeLabel}). Do not stop early.")
            .AppendLine("Respond with ONLY the sections array — no code fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .ToString();

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine("Full pillar article text (this is your source material — repurpose it, don't re-research from scratch):")
            .AppendLine(pillarText)
            .AppendLine()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar title (link target — do not reuse as blog title): {sourceArticle.Title}")
            .AppendLine()
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Blog meta description: {metadata.MetaDescription}")
            .AppendLine()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection))
            .AppendLine()
            .AppendLine("Write the blog body sections. Re-angle the pillar's substance in this blog's own paragraphs — takeaways must appear in the body, not only as a closing CTA.")
            .AppendLine("Name the platforms in the research brief in running prose where they help this angle. A closing CTA is not the only connection to the pillar or the tools.")
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.7,
            MaxOutputTokens: 6144));
    }

    public ChatCompletionRequest BuildStandaloneBlogMetadataPrompt(ProjectGenerationContext context)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(BlogMetadataJsonContract)
            .AppendLine("This is a standalone deep-dive blog — there is no companion pillar article. Title should be a conversational hook, question, or numbered angle.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection,
                $"Plan a standalone deep-dive blog ({ContentLengthTargets.BlogRangeLabel} words) with a distinct title, angle, and {ContentLengthTargets.BlogSectionCountMin}-{ContentLengthTargets.BlogSectionCountTarget} H2 section headings."))
            .AppendLine($"Editorial standard: {ContentLengthTargets.BlogEditorialDefinition}")
            .AppendLine("Each section must support substantive depth — data points, examples, and implementation context.")
            .AppendLine("Return title, summary, metaDescription, keywords, and sectionOutline only (body is written separately).")
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.6,
            MaxOutputTokens: 1536);
    }

    public ChatCompletionRequest BuildStandaloneBlogLedePrompt(ProjectGenerationContext context, BlogMetadataDraft metadata)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            // Stage 6: this used to hardcode "prefer a creative opening" with no way to choose
            // among the 12 lede types the JSON contract below already demands a value for --
            // pillar's lede got real brief-aware guidance; blog never did. Same guidance now.
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("The opening is the hook, then the turn that names what is at stake, then who this is for.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine(LedeJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.7,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildStandaloneBlogBodyPrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? revisionNotes = null,
        bool requireHeadingProvenance = false, string? evidenceBlock = null, Section? lede = null)
    {
        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Write a standalone deep-dive blog post from the research brief and keyword — there is no pillar article to repurpose.")
            .AppendLine("Substantive paragraphs with examples and implementation context; first/second person allowed.")
            .AppendLine($"Target at least {ContentLengthTargets.BlogMinWords:N0} words (aim for {ContentLengthTargets.BlogRangeLabel}). Do not stop early.")
            // A whole-document target is a number the model cannot act on while writing section
            // three of six. Pillar has carried a per-section range all along and lands in its band;
            // blog carried only the total and came back at 791 words against 1,800-2,500 (Jeff,
            // 2026-09-23). Both constants already existed and nothing on this path used them.
            .AppendLine($"Each section runs {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words. " +
                $"That is what {ContentLengthTargets.BlogSectionCountMin}-{ContentLengthTargets.BlogSectionCountTarget} sections of real depth adds up to -- " +
                "a section coming in at half of it has not finished making its point, it has not been written concisely.")
            .AppendLine(HeadingCraftInstruction)
            .AppendLine(SectionVarietyInstruction)
            .AppendLine(FillerBanInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine(briefBody)
            .AppendLine("Respond with ONLY the sections array — no code fences, no commentary:")
            .AppendLine(requireHeadingProvenance ? SectionsArrayJsonContractWithProvenance : SectionsArrayJsonContract)
            .ToString();

        if (requireHeadingProvenance)
        {
            if (!string.IsNullOrWhiteSpace(evidenceBlock))
            {
                system += Environment.NewLine + evidenceBlock;
            }

            system += Environment.NewLine + HeadingProvenanceInstruction +
                " A section whose heading matches one of the advisory H2s below may tag its own" +
                " provenance \"plan\". Any heading you refine, replace, or add beyond those — at any" +
                " level, including nested children — needs a real tag from the rules above.";
        }

        var blogContinuity = BuildLedeContinuityBlock(lede);
        if (blogContinuity is not null)
        {
            system += Environment.NewLine + blogContinuity;
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection,
                "Write the blog body sections from this research. Ground claims in the brief; do not invent statistics."))
            .AppendLine()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Blog meta description: {metadata.MetaDescription}")
            .AppendLine()
            .AppendLine("Advisory section outline (prefer these H2s when they still fit, but refine any that reads as a reusable label rather than this page's own claim):")
            .AppendLine(string.Join(Environment.NewLine, (metadata.SectionOutline ?? []).Select(h => $"- {h}")))
            .AppendLine()
            .AppendLine("Write the blog body sections. Name platforms from the research brief in running prose where they fit.")
            .AppendLine(ClosingCallToActionInstruction(context))
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.7,
            MaxOutputTokens: 6144));
    }

    public ChatCompletionRequest BuildSocialPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, string platform, string articleUrl)
    {
        var (styleGuidance, lengthGuidance, maxTokens) = platform switch
        {
            "Facebook" => (
                "Casual B2B link-share post: 30-50 words (~40-250 characters). Put the hook in the first line before \"See more\" truncates (~200 chars). 1 emoji max. End with the URL and a light CTA.",
                "Keep under 250 characters total when possible.",
                512),
            "LinkedIn" => (
                "Professional thought-leadership post: 200-300 words. Structure: (1) hook in first 30 words — mobile \"see more\" folds at ~210 chars, (2) context/problem, (3) 1-2 insights from the article, (4) CTA + URL. No emojis or at most one.",
                "Aim for 1,300-1,900 characters. Maximum 3,000 characters.",
                2048),
            _ => ("Professional tone, concise, end with the link.", "Keep concise.", 1024)
        };

        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine($"You write {platform} posts for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForSocialPlatform(platform))
            .AppendLine(briefBody)
            .AppendLine(styleGuidance)
            .AppendLine(lengthGuidance)
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine(SocialJsonContract)
            .AppendLine("JSON rules: one string value for text. Use \\n for line breaks. Plain URL only — no [text](url) link syntax.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Article title: {sourceArticle.Title}")
            .AppendLine($"Article summary: {sourceArticle.MetaDescription}")
            .AppendLine($"Link to include verbatim: {articleUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine(HierarchyPromptGuidance(context, strictChildHeadings: false))
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.65,
            MaxOutputTokens: maxTokens);
    }

    public ChatCompletionRequest BuildColdOutreachPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        string articleUrl)
    {
        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You write cold outreach / sales emails for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(briefBody)
            .AppendLine(ContentLengthTargets.EmailColdOutreachEditorialDefinition)
            .AppendLine($"Body must be {ContentLengthTargets.EmailColdOutreachMinWords}-{ContentLengthTargets.EmailColdOutreachMaxWords} words.")
            .AppendLine("Pitch ONE clear idea. No HTML. No inline link syntax. Do not invent URLs.")
            .AppendLine("ctaLabel is short button/link text (e.g. \"Read the full guide\"). The destination URL is injected by the app.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine(ColdOutreachJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Article title: {sourceArticle.Title}")
            .AppendLine($"Article summary: {sourceArticle.MetaDescription}")
            .AppendLine($"Pillar URL (for context only — do not put in JSON): {articleUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine(HierarchyPromptGuidance(context, strictChildHeadings: false))
            .AppendLine(BrandTones.ForEmail())
            .ToString();

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user) },
            Temperature: 0.65,
            MaxOutputTokens: 1024);
    }

    public ChatCompletionRequest BuildSectionImagePromptsPrompt(
        ProjectGenerationContext context,
        ArticleDraft sourceArticle,
        BlogDraft sourceBlog,
        string articleUrl,
        string blogUrl,
        IReadOnlyList<ImagePromptSectionTarget> sections)
    {
        var system = new StringBuilder()
            .AppendLine("You write AI image-generation prompts for B2B article figures.")
            .AppendLine("CRITICAL: Return EXACTLY ONE prompt for EACH listed section, in the exact order listed.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional fintech or B2B tech aesthetic.")
            .AppendLine($"- Default size: {ImagePromptDefaults.PillarWidth}x{ImagePromptDefaults.PillarHeight}. Style: {ImagePromptDefaults.PillarStylePreset}.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine("- pillar-hero / blog-hero (ALWAYS required): the H1/title hero banner image — represents the entire piece at a glance. Wider establishing-shot composition (not a diagram), evokes the title's theme and stakes. blog-hero warmer/more approachable than pillar-hero.")
            .AppendLine("- Pillar H2 sections: teaching diagram, slightly more technical.")
            .AppendLine("- Blog sections: warmer step-by-step feel, still no readable text.")
            .AppendLine("- People Also Ask: abstract Q&A bubbles/shapes without words.")
            .AppendLine("- Tools sections: generic software tiles/icons — no brand names.")
            .AppendLine()
            .AppendLine("IMAGE SETTINGS (include in JSON for each section):")
            .AppendLine($"- imageModel: \"{ImagePromptDefaults.DefaultImageModel}\"")
            .AppendLine("- stylePreset: Illustration")
            .AppendLine("- alchemy: true, photoReal: false")
            .AppendLine("- notes: one short image-gen tip (negative prompt, no text, etc.)")
            .AppendLine()
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no preamble, no trailing text:")
            .AppendLine(ImagePromptSectionsJsonContract)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Pillar title: {sourceArticle.Title}")
            .AppendLine($"Pillar URL: {articleUrl}")
            .AppendLine($"Blog title: {sourceBlog.Title}")
            .AppendLine($"Blog URL: {blogUrl}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine()
            .AppendLine("Sections requiring image prompts (DO NOT SKIP ANY):");

        foreach (var section in sections)
        {
            user.AppendLine($"- sourceType: {section.SourceType}, order: {section.Order}, heading: {section.Heading}");
        }

        user.AppendLine()
            .AppendLine("REQUIRED: All sections above must have a prompt in the JSON response.");

        return new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user.ToString()) },
            Temperature: 0.7,
            MaxOutputTokens: 8192,
            // Utility: image prompts -- structured, short, no prose a reader reads.
            TaskClass: LlmTaskClass.Utility);
    }

    public ChatCompletionRequest BuildStandaloneImagePrompt(
        string topic,
        string? notes,
        string? artifactContext)
    {
        var system = new StringBuilder()
            .AppendLine("You write ONE AI image-generation prompt for a B2B content figure.")
            .AppendLine("Prompt text only — not pixels.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional fintech or B2B tech aesthetic.")
            .AppendLine($"- Default size: {ImagePromptDefaults.PillarWidth}x{ImagePromptDefaults.PillarHeight}. Style: {ImagePromptDefaults.PillarStylePreset}.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine("- Prefer a wider establishing-shot hero composition that evokes the topic's theme and stakes.")
            .AppendLine()
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine(
                "{\"prompt\": string, \"style\": string, \"negativePrompt\": string, \"aspectRatio\": string, \"imageModel\": string, \"stylePreset\": string, \"notes\": string}")
            .AppendLine($"Use imageModel \"{ImagePromptDefaults.DefaultImageModel}\" and stylePreset \"Illustration\" unless the brief clearly requires otherwise.")
            .AppendLine("aspectRatio should be \"16:9\" for hero figures unless notes specify otherwise.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Topic/title: {topic.Trim()}")
            .AppendLine(BrandTones.ForWebpages());
        if (!string.IsNullOrWhiteSpace(notes))
            user.AppendLine($"Notes / brief: {notes.Trim()}");
        if (!string.IsNullOrWhiteSpace(artifactContext))
        {
            var clipped = artifactContext.Length > 6000 ? artifactContext[..6000] : artifactContext;
            user.AppendLine("Artifact context (ground the visual in this draft):");
            user.AppendLine(clipped);
        }

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.7,
            MaxOutputTokens: 1024,
            // Utility: a standalone image prompt -- structured, short, no prose a reader reads.
            TaskClass: LlmTaskClass.Utility);
    }

    private const string ToolMetadataJsonContract =
        "{\"departmentListExcerpt\": string (1-2 sentences for tools hub cards), \"summary\": string (1-2 sentences, general-purpose blurb used on listings), \"mainSummary\": string (1-2 sentences, main-page summary), \"heroSummary\": string (1-2 sentences, blurb under tool page H1), \"homeSummary\": string (1-2 sentences, home-page feature card copy), \"blogSummary\": string (1-2 sentences, blog-listing teaser), \"toolPageExcerpt\": string (1-2 sentences for newspaper tool content column), \"advertisingSummary\": string (2-4 sentences, longer sponsored promotional copy — not an excerpt), \"metaDescription\": string (max 160 chars, SEO only, distinct from the other eight)}";

    public ChatCompletionRequest BuildToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string toolSlug,
        IReadOnlyList<SectionSlot> outline,
        string? revisionNotes = null,
        string? extractedToolResearchJson = null,
        Section? lede = null)
    {
        // One rendering of the outline, from the one definition. This block used to be three hand-
        // written prose lists inside this prompt -- the required section names, the per-section word
        // budget, and three paragraphs of per-section instruction addressing sections by name --
        // beside a fourth copy in ToolPrompts and a fifth in GccGenerateService.
        var sectionBlock = new StringBuilder();
        for (var i = 0; i < outline.Count; i++)
        {
            var slot = outline[i];
            sectionBlock.AppendLine(slot.WritesItsOwnHeading
                ? $"{i + 1}. Cover: {slot.Covers}"
                : $"{i + 1}. \"{slot.Heading}\"");
            if (slot.Depth is { Length: > 0 })
            {
                sectionBlock.AppendLine($"   Roughly {slot.Depth} -- for proportion between sections, not a quota.");
            }
            if (slot.Guidance is { Length: > 0 })
            {
                sectionBlock.AppendLine($"   {slot.Guidance}");
            }
        }

        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            // Pillar and Blog have banned this vocabulary for weeks; Tool never got it, which is the
            // wrong way round -- a page about a partner's product is where "transformative
            // potential" and "unlock value" are most likely to turn up, and where they do the most
            // damage to a claim that has to be true.
            .AppendLine(FillerBanInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine("Respond with ONLY the sections array for this tool overview page — no code fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine("This page is published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            // What this page IS. The prompt previously described a consulting firm writing about a
            // tool and never said the word partner -- while the extraction prompt that feeds it
            // defines one precisely. The two halves of the same pipeline did not share a
            // definition, so the writer had to infer the relationship it was writing about
            // (Jeff, 2026-09-23: "this doesn't appear to be a prompt that is specifically written
            // for Partners/Tools?"). Kept deliberately consistent with
            // GccV2PartnerExtractionService's wording, since both run over the same material.
            .AppendLine($"WHAT THIS PAGE IS: {app.Name} is a PARTNER — a third-party SaaS product that " +
                $"{context.PublisherName} promotes and implements for clients. This page exists to show a reader " +
                $"facing \"{context.TargetKeyword}\" how {app.Name} specifically addresses that problem. It is one " +
                "product's page, not a category explainer and not a roundup.")
            .AppendLine($"Keep the two roles distinct and never blur them: {app.Name} is the software; " +
                $"{context.PublisherName} ({context.ImplementerPositioning}) is the implementer who deploys and " +
                $"configures it. Never describe {app.Name} as if it delivered human consulting or agency services, " +
                $"and never claim {context.PublisherName} builds the product's own features.")
            .AppendLine($"Name {app.Name} throughout, in every section. A sentence that would read identically " +
                "about a competing product is a sentence that has not done its job.")
            .AppendLine("No introductory paragraphs before the first section.")
            .AppendLine($"Write {outline.Count} top-level (h2) sections, in this order. Each entry says what that " +
                "section is responsible for; you write its heading:")
            .AppendLine(sectionBlock.ToString().TrimEnd())
            .AppendLine(HeadingCraftInstruction)
            .AppendLine(SectionVarietyInstruction)
            // Length is guidance for long form, never a quota. "Target at least N words, do not stop
            // early" is padding pressure: on thin partner data the only way to satisfy it is filler,
            // and filler on a partner page is worse than a short honest one. Jeff, 2026-09-23:
            // "Quality is more important than an arbitrary word count" and "quality of the prose is
            // more important than word count for each and every content type. Short forms rein word
            // count" -- so the range informs depth here, while short-form types keep hard limits
            // because brevity is the point of them.
            .AppendLine($"Length guidance, not a quota: {ContentLengthTargets.ToolTargetMinWords:N0}-{ContentLengthTargets.ToolTargetMaxWords:N0} words is what full coverage of this product usually takes, and {ContentLengthTargets.ToolHardMaxWords:N0} is the ceiling. " +
                "Treat it as a signal about depth, never a target to reach.")
            .AppendLine("Write to the material you were given and stop when the section is genuinely covered. " +
                "Never pad, never restate a point in new words to add length, never invent detail to fill a section. " +
                "A shorter section that is entirely supported beats a longer one that is padded -- if the partner data " +
                "does not support a section, say less.")
            .AppendLine($"Equal to a Pillar page in ambition, not a thinner treatment -- {outline.Count} substantial sections, not four.")
            .AppendLine($"This word target is for the {outline.Count} sections above only -- a separate FAQ section, when the tool has " +
                "verified partner FAQ data, is generated afterward and is additional, not part of this budget.")
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent a feature, integration, or claim to fill space.")
            .AppendLine($"When persisted tool research is provided, treat it as the authoritative source — do not re-extract or contradict it.")
            .AppendLine($"Frame the implementation material as {context.PublisherName} ({context.ImplementerPositioning}) closing the gap for a client — consultative, not a sales pitch.")
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .AppendLine($"Tie the opening and closing sections to this project's use-case ({context.TargetKeyword}). Name sibling platforms from the research brief only when a real contrast helps — this page is about {app.Name}, not a roundup.")
            .ToString();

        var toolContinuity = BuildLedeContinuityBlock(lede);
        if (toolContinuity is not null)
        {
            system += Environment.NewLine + toolContinuity;
        }


        // Who the page is for, and what it has to do for them. Drawn from Jeff's own partner-page
        // template (2026-09-23), supplied "to facilitate, not dictate" -- so the six-section
        // outline stays and its conversion intent is folded in as instruction. Until now the tool
        // body prompt named no audience and had no call to action at all, while the brief has
        // collected both for months and ResearchBriefPhase.ToolBody emits neither.
        var audience = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context.AudienceSegment) || !string.IsNullOrWhiteSpace(context.AudienceNotes))
        {
            audience.AppendLine($"WHO THIS IS FOR: {context.AudienceSegment}"
                + (string.IsNullOrWhiteSpace(context.AudienceNotes) ? "" : $" — {context.AudienceNotes}"));
        }
        if (!string.IsNullOrWhiteSpace(context.BuyingStage))
            audience.AppendLine($"Buying stage: {context.BuyingStage} — pitch the page at where they already are.");
        audience.AppendLine($"They are weighing {app.Name} and want three questions answered: is it right for a business my size, "
            + $"what does it fix for my team specifically, and why hire {context.PublisherName} to set it up instead of doing it myself.");
        audience.AppendLine("Translate capability into consequence. Every feature you state must land with what it means for "
            + "that reader — hours returned, errors removed, a job that stops needing a person. A capability listed without "
            + "its consequence is a spec sheet, and they can already read the vendor's own.");
        audience.AppendLine("Lead with outcomes, not mechanism. Plain language over jargon, concrete over abstract.");
        audience.AppendLine($"The implementation section is where you answer the DIY question: what {context.PublisherName} "
            + $"({context.ImplementerPositioning}) does that makes {app.Name} work in their environment — configuration, data "
            + "mapping, integration with what they already run, training. Earn the claim, never assert it.");
        audience.AppendLine(ClosingCallToActionInstruction(context));
        audience.AppendLine("Place it after the reader has reason to act — never a banner, never repeated per section.");

        system += Environment.NewLine + audience.ToString();

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, toolSlug: toolSlug);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ToolBody, $"Write the tool overview page for {app.Name}."))
            .AppendLine()
            .AppendLine($"Target keyword context: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Tool summary: {app.Description ?? "N/A"}")
            .AppendLine($"Public path: /tools/{toolSlug}");
        if (!string.IsNullOrWhiteSpace(context.PillarBodyExcerpt))
        {
            user.AppendLine("=== PILLAR USE-CASE EXCERPT (ground the opening and closing sections here; do not reprint the pillar) ===");
            user.AppendLine(context.PillarBodyExcerpt);
        }
        if (!string.IsNullOrWhiteSpace(extractedToolResearchJson))
        {
            // This is the substance of the page, not background reading. A tool page paraphrases
            // the partner's own data -- capabilities, pricing, who it is for, integrations, limits,
            // proof -- into our words. Labelling it "authoritative" and then asking for prose about
            // the subject produced pages that named no partner and restated nothing from it
            // (Jeff, 2026-09-23: "Tools should be paraphrasing Partner data").
            user.AppendLine("=== PARTNER DATA -- THE SUBSTANCE OF THIS PAGE (authoritative) ===");
            user.AppendLine(extractedToolResearchJson);
            user.AppendLine();
            user.AppendLine(
                "Write this page as a paraphrase of the partner data above. Every factual statement " +
                "-- capabilities, pricing, integrations, who it is for, limitations, evidence -- must " +
                "restate something actually present in that data, in your own words.");
            user.AppendLine(
                "Do not reproduce it verbatim, and do not add capabilities, figures, customers, " +
                "integrations or claims that are not in it. Where the data is silent on something a " +
                "section would normally cover, write less rather than inventing it -- an unsupported " +
                "claim on a partner page is worse than a shorter section.");
            user.AppendLine(
                "Name the product and its specifics concretely. A page that could be about any tool " +
                "in this category has not used the data.");
        }

        user.AppendLine($"Write expert third-person technical prose focused on {app.Name}, grounded in this use-case.");

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.5,
            // 16384 to match BuildArticleSectionBatchPrompt (Pillar's own body-batch call) now that
            // Tool targets the same 3,000-5,000 word range across six JSON-structured sections --
            // 8192 was sized for the old four-section, ~1,500-2,000 word target.
            MaxOutputTokens: 16384));
    }

    /// <summary>See <see cref="IContentPromptBuilder.BuildToolFaqSectionPrompt"/>.</summary>
    public ChatCompletionRequest BuildToolFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<GccPartnerFaqAsset> faqBank)
    {
        var faqBlock = string.Join(
            "\n\n",
            faqBank.Select((f, i) =>
                $"  Q{i + 1}: {f.Question}\n  Verified answer: {f.VerifiedAnswer}\n  Source: {f.OriginProofUrl}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Write ONLY the FAQ section of the tool overview page for {app.Name}.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\" and heading is exactly \"Frequently Asked Questions\". Each " +
                "question is a child Section: tag \"h3\", heading is the question (verbatim or lightly tightened for " +
                "clarity), paragraphs holds the answer.")
            .AppendLine("Every answer below is already verified against the partner's own site -- paraphrase and " +
                $"tighten it into {context.PublisherName}'s ({context.ImplementerPositioning}) voice, but never change " +
                "its factual content, add a claim not in the verified answer, or drop the substance to shorten it.")
            .AppendLine("Use every question provided, in the order given, none invented and none skipped.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine("=== VERIFIED PARTNER FAQ (authoritative — paraphrase, do not re-derive) ===")
            .AppendLine(faqBlock)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.3,
            MaxOutputTokens: 4096,
            // Utility: FAQ formatting from already-verified partner answers -- structured, short, no prose a reader reads.
            TaskClass: LlmTaskClass.Utility));
    }

    public ChatCompletionRequest BuildToolRoundupPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        string roundupTitle,
        string toolsResearchBlock)
    {
        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY a sections array — no code fences, no commentary:")
            .AppendLine(SectionsArrayJsonContract)
            .AppendLine($"Write a hub page titled \"{roundupTitle}\" that lists each tool and links to its dedicated page URL.")
            .AppendLine("This page belongs to Generate Tools — it is not a pillar Write Body section.")
            .AppendLine("Use the persisted research for each tool — do not invent capabilities.")
            .AppendLine("Structure: opening H2 with the exact title, then one H3 per tool. Each H3 heading is the tool name; the first mention in that subsection must link to the provided dedicated-page URL.")
            .AppendLine("Each tool gets a short substantive blurb (what it does for this use case), not a one-line roll-call and not a copy of the dedicated tool page.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine("=== TOOLS + RESEARCH + URLS ===")
            .AppendLine(toolsResearchBlock)
            .ToString();

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.4,
            MaxOutputTokens: 4096));
    }

    public ChatCompletionRequest BuildToolResearchExtractionPrompt(string fileName, string htmlOrText)
    {
        var clipped = htmlOrText.Length > 40_000 ? htmlOrText[..40_000] + "…" : htmlOrText;
        var system =
            "Extract structured research about a single software tool from the uploaded page. " +
            "Respond with ONLY JSON (no fences): " +
            "{\"name\": string, \"summary\": string, \"whatItDoes\": string, \"features\": string[], " +
            "\"useCases\": string[], \"positioning\": string, \"pricing\": string}. " +
            "Use empty string/array when unknown — never invent pricing or features not supported by the page.";
        var user = $"File: {fileName}\n\n---\n{clipped}";
        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.1,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildAdvertisingPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, string articleUrl)
    {
        var system = new StringBuilder()
            .AppendLine("You write schema.org AdvertiserContentArticle body copy for B2B IT consulting.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY JSON: {\"title\": string, \"bodyText\": string, \"metaDescription\": string}.")
            .AppendLine("bodyText should be 180-320 words of sponsored/advertiser article prose grounded in the source — not a keyword-stuffed ad.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Source title: {sourceArticle.Title}")
            .AppendLine($"Source URL: {articleUrl}")
            .AppendLine($"Source meta: {sourceArticle.MetaDescription}")
            .AppendLine("Source excerpt:")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(sourceArticle.Body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.5,
            MaxOutputTokens: 2048);
    }

    public ChatCompletionRequest BuildToolMetadataPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        ContentDocument body)
    {
        var system = new StringBuilder()
            .AppendLine("You write presentation metadata for a B2B tool overview page (schema.org SoftwareApplication).")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine(ToolMetadataJsonContract)
            .AppendLine("departmentListExcerpt, summary, mainSummary, heroSummary, homeSummary, blogSummary, toolPageExcerpt, advertisingSummary, and metaDescription must each use different wording — no field may restate another's sentence structure or lede.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine()
            .AppendLine("Tool page body (for context):")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    private const string SummaryVariantsJsonContract =
        "{\"summary\": string (1-2 sentences, general-purpose blurb used on listings), \"mainSummary\": string (1-2 sentences, main-page summary), \"heroSummary\": string (1-2 sentences, blurb under the page H1), \"homeSummary\": string (1-2 sentences, home-page feature card copy), \"blogSummary\": string (1-2 sentences, blog-listing teaser), \"advertisingSummary\": string (2-4 sentences, longer sponsored promotional copy — not an excerpt)}";

    public ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel)
    {
        var system = new StringBuilder()
            .AppendLine($"You write presentation summary copy for a {contentTypeLabel} page.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine(SummaryVariantsJsonContract)
            .AppendLine("summary, mainSummary, heroSummary, homeSummary, blogSummary, and advertisingSummary must each use different wording from each other and from the meta description provided below — no field may restate another's sentence structure or lede.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Title: {title}")
            .AppendLine($"Meta description (do not repeat this wording): {metaDescription ?? "N/A"}")
            .AppendLine()
            .AppendLine("Page body (for context):")
            .AppendLine(TruncateExcerpt(ContentDocumentText.Flatten(body), 2000))
            .ToString();

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.55,
            MaxOutputTokens: 1024);
    }

    private static readonly Regex SectionTagRegex = new(
        @"\[Section:\s*""(?<heading>[^""]+)""\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Lede-scoped notes: tagged with the current lede heading, or a section title that is not in
    /// the body outline (lede-only titles). Meta/title and FAQ notes are excluded — those have
    /// their own revise paths.
    /// </summary>
    internal static string? ScopeRevisionNotesForLede(
        string? revisionNotes,
        string? existingLedeHeading,
        IReadOnlyList<string> sectionOutline)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        if (!revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            return revisionNotes;
        }

        var ownedElsewhere = new HashSet<string>(sectionOutline, StringComparer.OrdinalIgnoreCase)
        {
            "People Also Ask",
            "Meta description",
            "Meta Description",
            "Title",
        };

        var kept = new List<string>();
        foreach (var item in SplitNumberedNoteItems(revisionNotes))
        {
            var match = SectionTagRegex.Match(item);
            if (!match.Success)
            {
                kept.Add(item);
                continue;
            }

            var heading = match.Groups["heading"].Value.Trim();
            var isCurrentLede = !string.IsNullOrWhiteSpace(existingLedeHeading)
                && heading.Equals(existingLedeHeading, StringComparison.OrdinalIgnoreCase);
            var isLedeOnlyTitle = !ownedElsewhere.Contains(heading);

            if (isCurrentLede || isLedeOnlyTitle)
            {
                kept.Add(item);
            }
        }

        return kept.Count == 0 ? null : string.Join('\n', kept);
    }

    /// <summary>Notes whose [Section: ...] heading targets meta description or title hygiene.</summary>
    internal static string? ScopeRevisionNotesForMeta(string? revisionNotes)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        if (!revisionNotes.Contains("[Section:", StringComparison.Ordinal))
        {
            // Unstructured feedback: only treat as meta work when it clearly mentions meta/title hygiene.
            return MentionsMetaOrTitleHygiene(revisionNotes) ? revisionNotes : null;
        }

        var kept = new List<string>();
        foreach (var item in SplitNumberedNoteItems(revisionNotes))
        {
            var match = SectionTagRegex.Match(item);
            if (!match.Success)
            {
                continue;
            }

            var heading = match.Groups["heading"].Value.Trim();
            if (heading.Equals("Meta description", StringComparison.OrdinalIgnoreCase)
                || heading.Equals("Meta Description", StringComparison.OrdinalIgnoreCase)
                || heading.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                kept.Add(item);
            }
        }

        return kept.Count == 0 ? null : string.Join('\n', kept);
    }

    private static bool MentionsMetaOrTitleHygiene(string notes) =>
        notes.Contains("meta description", StringComparison.OrdinalIgnoreCase)
        || notes.Contains("metaDescription", StringComparison.OrdinalIgnoreCase)
        || (notes.Contains("title", StringComparison.OrdinalIgnoreCase)
            && (notes.Contains("140", StringComparison.Ordinal)
                || notes.Contains("160", StringComparison.Ordinal)
                || notes.Contains("cutting-edge", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Splits reviewer notes into numbered items (lines starting with "N. ") preserving multi-line items.</summary>
    private static IEnumerable<string> SplitNumberedNoteItems(string notes)
    {
        var lines = notes.Replace("\r\n", "\n").Split('\n');
        var current = new StringBuilder();
        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\s*\d+\.\s+") && current.Length > 0)
            {
                yield return current.ToString().TrimEnd();
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.AppendLine();
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// Renders the "REVISION REQUIRED" system-prompt addendum from a reviewer's revise notes, or
    /// null if there's nothing to append. When <paramref name="toolSlug"/> is supplied (tool-batch
    /// regeneration), scopes the notes down to that tool's "Tool: {slug}" block first — notes
    /// tagged for a different tool must never leak into this tool's regeneration prompt. When
    /// <paramref name="sectionHeading"/> is supplied (per-section builders), adds a self-filter
    /// instruction so a section not referenced by any note is left untouched, but only when the
    /// (possibly tool-scoped) notes actually contain "[Section:" markers to match against —
    /// otherwise every generation call would silently discard unstructured feedback.
    /// </summary>
    private static string? BuildRevisionNotesBlock(string? revisionNotes, string? sectionHeading = null, string? toolSlug = null)
    {
        if (string.IsNullOrWhiteSpace(revisionNotes))
        {
            return null;
        }

        var scoped = revisionNotes;
        if (toolSlug is not null)
        {
            var toolBlock = ExtractToolNotesBlock(revisionNotes, toolSlug);
            // "Tool:" tags present but none match this slug -> nothing applies to this tool.
            // No "Tool:" tags at all (e.g. single-tool regeneration test) -> nothing to scope
            // down from, so the whole text is in scope.
            scoped = toolBlock ?? (revisionNotes.Contains("Tool:", StringComparison.Ordinal) ? null! : revisionNotes);
            if (scoped is null)
            {
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(scoped))
        {
            return null;
        }

        var hasSectionTags = scoped.Contains("[Section:", StringComparison.Ordinal);
        if (!hasSectionTags)
        {
            // Format guard: the reviewer didn't produce the structured [Section: "..."] format —
            // fall back to a generic, unfiltered instruction rather than injecting raw prose as if
            // it were structured, and apply no section/tool self-filter since there's no tag to
            // match against.
            return $"REVISION REQUIRED — address the reviewer's feedback: {scoped.Trim()}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("REVISION REQUIRED — address each of the following before returning your section. Only rewrite the");
        sb.AppendLine("section(s) referenced below; leave everything else in your usual writing process unaffected.");
        sb.AppendLine();
        sb.AppendLine(scoped.Trim());

        if (sectionHeading is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"If none of the notes above reference this section (\"{sectionHeading}\"), ignore them and write normally.");
        }

        return sb.ToString();
    }

    /// <summary>Extracts the slice of tagged revision notes (see ReviewLoopService) belonging to one tool, by "Tool: {slug}" marker.</summary>
    private static string? ExtractToolNotesBlock(string taggedNotes, string toolSlug)
    {
        var lines = taggedNotes.Split('\n');
        var marker = $"Tool: {toolSlug}";
        var start = Array.FindIndex(lines, l => l.Trim().Equals(marker, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return null;
        }

        var end = start + 1;
        while (end < lines.Length && !lines[end].TrimStart().StartsWith("Tool: ", StringComparison.OrdinalIgnoreCase))
        {
            end++;
        }

        return string.Join('\n', lines[(start + 1)..end]).Trim();
    }

    private static string TruncateExcerpt(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[..maxChars].TrimEnd() + "…";
    }

    private static string BuildToolsPlatformChildGuidance(ProjectGenerationContext context, string platformName)
    {
        return new StringBuilder()
            .AppendLine("PLATFORM SUBSECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine($"Platform: {platformName}")
            .AppendLine("In the h4 child's paragraphs, cover all four of these mechanisms, each made concrete to THIS specific platform (not generic filler):")
            .AppendLine($"  1. Accelerated deployment — what specifically shortens go-live for {platformName}.")
            .AppendLine($"  2. Data model design — what {platformName}-specific data structure/mapping decisions an implementer gets right upfront.")
            .AppendLine($"  3. Workflow/process configuration — what {platformName}-specific approval chains, routing rules, or automation logic get configured.")
            .AppendLine($"  4. Custom code/development — what {platformName}-specific extension mechanism exists if the platform supports one; if it genuinely has no such layer, say so plainly instead of inventing one.")
            .AppendLine("Keep each of the 4 points to ONE tight sentence — flowing prose in a single text paragraph, not a numbered list.")
            .AppendLine("Tie these to outcomes: reduced time-to-value, fewer failed pilots, production-ready automation.")
            .AppendLine($"Write from the perspective of {context.PublisherName} as the implementer where natural — without hard-selling.")
            .AppendLine("This h3 child should describe a real software product suitable for schema.org SoftwareApplication JSON+LD.")
            .ToString();
    }

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

    private static string BuildImplementationSectionGuidance(ProjectGenerationContext context)
    {
        return new StringBuilder()
            .AppendLine("IMPLEMENTATION SECTION REQUIREMENTS:")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine("Lead with the pain of DIY rollout, failed pilots, opaque vendor setup, or stalled go-lives — not a generic numbered \"steps to implement AI\" list.")
            .AppendLine($"Emphasize the concrete work {context.PublisherName} does for clients on this topic, covering at least:")
            .AppendLine("  - Data model design — what must be mapped correctly upfront for this use case")
            .AppendLine("  - Workflow / process configuration — approval chains, routing, automation logic")
            .AppendLine("  - Integration and change management — connecting systems and getting teams to adopt the new process")
            .AppendLine("Make each point specific to the target keyword / article topic — not interchangeable implementer filler.")
            .ToString();
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
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
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
            .AppendLine("There is no real case-study data available — never present a named client, company, or engagement as if it were real. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .ToString();
    }

    private static string HierarchyPromptGuidance(ProjectGenerationContext context, bool strictChildHeadings)
    {
        var children = context.HierarchyChildHeadings ?? Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(context.HierarchyPath) && children.Count == 0)
            return "No Site Analyzer hierarchy context for this keyword.";

        var sb = new StringBuilder();
        sb.Append("Site Analyzer hierarchy grounding");
        if (!string.IsNullOrWhiteSpace(context.HierarchyPath))
            sb.Append($": path \"{context.HierarchyPath}\"");
        sb.Append('.');
        if (!string.IsNullOrWhiteSpace(context.HierarchySourcePageUrl))
            sb.Append($" Source page: {context.HierarchySourcePageUrl}.");
        if (children.Count == 0)
            return sb.ToString();

        sb.Append(" Child topics: ");
        sb.Append(string.Join("; ", children));
        sb.Append('.');
        if (strictChildHeadings)
        {
            sb.Append(" Each child topic MUST appear as a child heading (H2/H3) in the outline and body — not only as prose mentions.");
        }
        else
        {
            sb.Append(" Use these as soft topical guidance scaled to length; do not exhaust every child as a heading.");
        }

        return sb.ToString();
    }

    private static string HierarchyChildOutlineInstruction(ProjectGenerationContext context, bool forPillarOrBlog)
    {
        var children = context.HierarchyChildHeadings ?? Array.Empty<string>();
        if (!forPillarOrBlog || children.Count == 0)
            return string.Empty;

        return "REQUIRED: include each of these Site Analyzer child topics as its own sectionOutline heading (child headings in the article structure): "
            + string.Join(", ", children.Select(c => $"\"{c}\""))
            + ". ";
    }
}
