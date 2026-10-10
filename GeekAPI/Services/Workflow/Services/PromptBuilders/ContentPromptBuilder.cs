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
        string? existingLedeHeading = null,
        string? evidenceBlock = null);

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
        string? existingLedeHeading = null,
        string? evidenceBlock = null);

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
        Section? lede = null,
        int batchIndex = 0,
        IReadOnlyList<Section>? writtenSoFar = null);

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

    /// <summary>The blog's FAQ from the brief's own questions -- the pillar's People Also Ask shape,
    /// headed "Frequently Asked Questions" on a BlogPosting.</summary>
    ChatCompletionRequest BuildBlogFaqSectionPrompt(
        ProjectGenerationContext context,
        BlogMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions);

    /// <summary>The tool page's FAQ from the operator's questions, answered only from the partner's
    /// retrieved pages in <paramref name="evidenceBlock"/>; a question no passage answers is left out
    /// (the caller reports it), never answered from general knowledge.</summary>
    ChatCompletionRequest BuildToolFaqFromQuestionsPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<string> questions,
        string evidenceBlock);



    ChatCompletionRequest BuildBlogMetadataPrompt(ProjectGenerationContext context, ArticleDraft sourceArticle);

    ChatCompletionRequest BuildBlogBodyPrompt(
        ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata, string? revisionNotes = null);

    ChatCompletionRequest BuildBlogLedePrompt(ProjectGenerationContext context, ArticleDraft sourceArticle, BlogMetadataDraft metadata);

    /// <summary>
    /// Standalone blog (no pillar) — Content Creator starting-content flexibility.
    /// Uses research brief + keyword, not pillar-repurpose prompts.
    /// </summary>
    ChatCompletionRequest BuildStandaloneBlogMetadataPrompt(ProjectGenerationContext context);

    ChatCompletionRequest BuildStandaloneBlogLedePrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? evidenceBlock = null);

    /// <param name="sectionBatch">The sections this call owns, when the body is written in
    /// batches. Null writes the whole planned outline in one response, which is what a blog short
    /// enough to fit does.</param>
    ChatCompletionRequest BuildStandaloneBlogBodyPrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? revisionNotes = null,
        bool requireHeadingProvenance = false, string? evidenceBlock = null, Section? lede = null,
        IReadOnlyList<SectionSlot>? sectionBatch = null, int batchIndex = 0,
        IReadOnlyList<SectionSlot>? fullOutline = null);

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
        Section? lede = null,
        IReadOnlyList<SectionSlot>? fullOutline = null,
        int batchIndex = 0,
        string? evidenceBlock = null,
        IReadOnlyList<GccQuoteCandidate>? quoteCandidates = null,
        IReadOnlyList<Section>? writtenSoFar = null);

    /// <summary>
    /// FAQ section for a tool page, additional to the body word-count target -- not a substitute
    /// for it. Sourced from the partner FAQ pairs the extraction read off the partner's pages (never
    /// re-derived or invented the way Pillar's PAA-driven FAQ has to be), so the model
    /// formats/paraphrases, it doesn't answer from scratch. The pairs are model-extracted and are
    /// not checked against the page text before they reach this prompt.
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

    ChatCompletionRequest BuildSummaryVariantsPrompt(
        ProjectGenerationContext context,
        string title,
        ContentDocument body,
        string? metaDescription,
        string contentTypeLabel);

}

// Partial: the pillar-only prompt builders and JSON contracts live in
// Writers/ContentPromptBuilder.Pillar.cs (plans/single-source-of-responsibility.md, section 6
// step 2, 2026-10-09 -- a pure, behaviour-preserving physical move; see that file's header for
// which methods moved, which stayed, and why).
public partial class ContentPromptBuilder : IContentPromptBuilder
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

    /// <summary>
    /// What a long-form body needs to be able to say its own word floor.
    ///
    /// <para>
    /// Prose in this contract is not prose. Every run carries all four fields the schema marks
    /// required -- <c>{"text":"...","href":null}</c> (it was four, with bold and italic, until 2026-10-07) -- and every
    /// section carries its tag, heading, href, children and provenance. The scaffolding roughly
    /// doubles the token cost of the words, so a budget set by eye against a word count lands at
    /// about half of what those words need.
    /// </para>
    ///
    /// <para>
    /// The blog body had 6,144 against an 1,800-word floor: 3.4 tokens a word, where the pillar
    /// runs at 5.5 and works. It could not reach its floor, stopped near 1,200 words, and failed
    /// density as a consequence -- two mentions in 1,199 words is 0.17%. Three prompt rewrites went
    /// into a cause that was never in the prompt (Jeff, 2026-09-28: scores 0, 40, 60).
    /// </para>
    /// </summary>
    private const int LongFormBodyMaxOutputTokens = 16384;

    private const string TopicFocusJsonContract =
        "{\"focus\": string[] (4-8 short topic phrases, 1-4 words each, describing the site's real services/subject matter — no generic filler words)}";

    private const string UseCaseExtractionJsonContract =
        "{\"items\": [{\"category\": string, \"name\": string (the exact item name as shown on the page), \"description\": string|null (its own short description text, if present), \"href\": string|null (the exact relative or absolute link this item points to, or null if it has no dedicated link yet)}]}";

    /// <summary>
    /// The structured-output contract every section-body call uses. No tag characters and no
    /// heading/emphasis/list punctuation in the text at all — headings are a plain string field,
    /// lists are their own paragraph variant, and the writer has no emphasis and no link to set
    /// (bold and italic are not offered; see LlmResponseJsonParser.NormalizeRun; a partner tool's
    /// page is put on its name by GccToolLinker after the reply is read, Jeff 2026-10-10). This is
    /// what actually eliminates truncated or malformed markup: there is no markup syntax available
    /// for the model to get wrong.
    /// </summary>
    private const string RunJsonShape =
        "{\"text\": string (plain text only — never markup syntax of any kind, never a URL)}";

    private static readonly string ParagraphJsonShape =
        "{\"type\":\"text\",\"runs\":[" + RunJsonShape + ", ...]} " +
        "OR {\"type\":\"list\",\"ordered\":boolean,\"items\":[[" + RunJsonShape + ", ...], ...]} " +
        "OR {\"type\":\"quote\",\"candidate\":integer? (the number of a listed quotable span), \"runs\":[" + RunJsonShape + ", ...],\"cite\":string? (source URL)} " +
        "(a real block quotation, for wording worth reproducing verbatim with its source — " +
        "never \"According to X, ...\" written as ordinary prose. Where quotable spans are listed, " +
        "set \"candidate\" to the span's number, leave \"runs\" empty and \"cite\" null: the words and " +
        "the source are taken from the list by that number, never from your reply)";

    private static readonly string SectionJsonContract =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], \"href\": null, " +
        "\"children\": [Section, ...] (nested subsections, same shape, one level deeper tag)}";

    private static readonly string SectionsArrayJsonContract =
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
        "\"paa:<question text>\" (the exact People Also Ask question above it answers), " +
        "\"competitor:<heading text>\" (the exact competitor heading above it fills a gap on), " +
        // The must-mention block calls its subtopics compulsory and, until 2026-09-28, licensed
        // none of them -- so a heading written to obey it had no honest tag, and the nearest
        // available one ("brief:Subtopics the site already treats under it, ...") failed the guard
        // and refused the draft.
        "\"site:<subtopic text>\" (the exact subtopic from THIS SITE ALREADY COVERS THIS TOPIC that " +
        "the section covers), or " +
        // Added 2026-09-29. QUOTEABLE RESEARCH was in the prompt with nothing to license a heading
        // built on it: a blog draft carrying one heading per partner tool -- exactly what the
        // required-mentions block asks for -- was discarded on 2026-09-28 because the only tag it
        // could reach ("site:") did not resolve. The model was being shown evidence and refused for
        // structuring the page around it.
        "\"evidence:<identifier>\" (a passage in QUOTEABLE RESEARCH that section is built on -- the " +
        "identifier is that passage's \"Target Entity Match\" partner name, or its \"Section:\" " +
        "title, or its page title, or its host; use the partner name where the passage carries one, " +
        "because that is the spelling the rest of this prompt asks for)";

    private static readonly string SectionJsonContractWithProvenance =
        "{\"tag\": \"h2\"|\"h3\"|\"h4\"|\"h5\"|\"h6\", \"heading\": string (plain text, no markup), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...], \"href\": null, " +
        "\"children\": [<same shape, one level deeper tag>, ...], " + ProvenanceFieldShape + "}";

    private static readonly string SectionsArrayJsonContractWithProvenance =
        "{\"sections\": [" + SectionJsonContractWithProvenance + ", ...] (top-level h2 sections, in order)}";

    /// <summary>
    /// Where the keyword has to appear, which the SEO report has always scored and no prompt ever
    /// asked for. GcwSeoAnalyzer checks keyword-in-lede and keyword-in-heading; the only placement
    /// instruction that existed was "metaDescription must include the target keyword", so two of
    /// its five checks marked down drafts for doing something nobody had requested.
    ///
    /// <para>
    /// Naturally, and once. A keyword pushed into every heading is the stuffing the density check
    /// fails a draft for a few lines later.
    /// </para>
    /// </summary>
    /// <summary>
    /// No section that is a tools listing, wherever the section came from.
    ///
    /// <para>
    /// The ban lived in the outline prompts only. A body prompt may add sections beyond the
    /// outline -- that is what provenance licenses -- so the plan came back clean and the body then
    /// wrote "Top Tools for Automated Data Entry &amp; Processing" with one h3 per product anyway
    /// (Jeff, 2026-09-28). Banning it at planning time and not at writing time bans it nowhere.
    /// </para>
    /// </summary>
    /// <summary>
    /// What the tools are FOR, which the ban alone does not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jeff, 2026-10-01: <i>"Pillar is required; same as Blog and all other long-form content
    /// types, to discuss tools as a solution to the problem identified in the Angle for SEO
    /// Problem-Solution"</i>.
    /// </para>
    /// <para>
    /// <see cref="NoToolsSectionInstruction"/> says where the tools may not go and how they are
    /// named. It says nothing about why they are on the page, so a writer could satisfy it by
    /// mentioning each partner once in passing and linking it — the letter of the rule with none of
    /// its point. The tools are the answer to the problem the angle identifies; that is the reason
    /// they are mentioned at all.
    /// </para>
    /// <para>
    /// Paired with the ban at both call sites deliberately. A prohibition without the obligation it
    /// exists to protect is how "name each where it earns the mention" became a checklist item.
    /// </para>
    /// </remarks>
    private const string ToolsAsSolutionInstruction =
        "TOOLS ARE THE SOLUTION: this page must discuss the partner tools as the answer to the "
        + "problem its Angle for SEO identifies.\n"
        + "- Express the problem in the reader's own terms first -- the workflow bottleneck the "
        + "keyword implies -- then work through the solutions the tool or tools provide.\n"
        + "- NO DEDICATED OR REPEATABLE FORMAT. Not a section for this, not a paragraph per tool, "
        + "not the same sentence shape five times with the names swapped. Each tool appears where "
        + "the argument reaches it, at the length that point deserves. A reader must not be able to "
        + "see the template.\n"
        + "- Write the mechanics, not the marketing. Translate what the evidence says the tool does "
        + "into how it removes that specific friction, in your own editorial voice. Do not reuse "
        + "vendor slogans, buzzwords or self-promotional phrasing from the source, and do not "
        + "describe the product in general -- only what it does about this problem.\n"
        + "- Do not quote. No blockquotes, no pull-quotes, no verbatim testimonial lines; this is "
        + "analysis in your voice. Claims still come from the supplied evidence, not from what you "
        + "already believe about these products.\n"
        + "- Every declared partner tool is discussed on this basis. A tool named without saying what "
        + "it solves has not been discussed.";

    /// <summary>
    /// The same ban, said at the planning stage: an outline heading is what the body writer is then
    /// handed as its assignment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three outline prompts each carried their own wording — <i>"Do not include a Tools H2. Tool
    /// names belong in body sentences, not as outline headings."</i> — which is narrower than the rule
    /// the body prompt and <c>GccToolsSectionGuard</c> enforce. It does not cover <i>"Choosing the
    /// Right Tools for Your Accounts Payable Needs"</i>, a heading the body instruction names
    /// explicitly, so the metadata call planned exactly that and a live generate refused on it.
    /// </para>
    /// <para>
    /// <b>That is the trap, not a slip.</b> The body writer receives a planned outline as assigned
    /// slots, so it was told never to write a tools section while being handed one as its heading. It
    /// followed the outline, which is the reasonable reading, and the retry re-wrote against the same
    /// outline — so no retry could succeed. A guard whose retry cannot be satisfied is a trap, and
    /// this is the second time that shape has been written here.
    /// </para>
    /// <para>
    /// So the planning stage now carries the ban itself rather than a paraphrase of it. One rule, one
    /// constant, three call sites that cannot disagree.
    /// </para>
    /// </remarks>
    private const string NoToolsSectionOutlineInstruction =
        NoToolsSectionInstruction
        + "\nThis applies to the outline you are planning now: no sectionOutline heading may be a "
        + "tools list, a tools-selection heading, or a product name. The body writer is handed these "
        + "headings as its assignment and cannot decline one.";

    private const string NoToolsSectionInstruction =
        "NO TOOLS SECTION: do not write a section that lists tools, whatever it is called -- not "
        + "\"Top Tools for ...\", not \"Choosing the Right Tools\", not a heading per product with a "
        + "product name in it. Name the tools in running prose where each one earns the mention. A "
        + "section whose job is to enumerate products is the "
        + "one shape this page must not have, and it is not licensed by a heading of that shape "
        + "existing on the site.";

    /// <summary>
    /// Everything the SEO score measures, said to the writer in the writer's terms.
    ///
    /// <para>
    /// These rules were accreted one sentence at a time, in whichever prompt was open when a
    /// failure was reported, and they ended up spread across outline, lede and body prompts while
    /// <c>GcwSeoAnalyzer</c> measured a different list. That is why a draft came back scoring 40
    /// with keyword-in-lede, density and length all failing: nothing had asked for any of them
    /// where those things are written.
    /// </para>
    ///
    /// <para>
    /// The numbers come from <see cref="GccLongFormTypes.GetSeoLengthRules"/> -- the scorer's own
    /// source -- so the floor the writer is given and the floor it is judged against cannot drift.
    /// They were separate constants in separate files: the blog prompt asked for 2,000 while the
    /// score required 1,800, and the tool prompt asked for 3,000 against a floor of 1,500.
    /// </para>
    ///
    /// <para>
    /// Every number here is the whole page's, and a batched body hands this block to each call that
    /// writes part of it. Left unscaled, a call writing two of six sections is told to produce the
    /// page's entire word floor and the page's entire keyword count -- so three calls either aim at
    /// three times the page or, more likely, read the numbers as unreachable and ignore them.
    /// <paramref name="sectionsInThisCall"/> and <paramref name="sectionsInThePage"/> are what turn
    /// the page's budget into this call's share.
    /// </para>
    /// </summary>
    /// <param name="sectionsInThisCall">Sections this response is responsible for.</param>
    /// <param name="sectionsInThePage">Sections the whole page has. Equal to
    /// <paramref name="sectionsInThisCall"/> when the body is written in one call.</param>
    /// <param name="ownsTheKeywordHeading">Whether this call carries the page's keyword-bearing H2.
    /// The scorer wants at least one and the outline rules cap it at two, so exactly one call is
    /// asked for it -- told to every batch, a six-section page comes back with three.</param>
    /// <summary>
    /// How many times a call writing <paramref name="sectionsInThisCall"/> of a page's
    /// <paramref name="sectionsInThePage"/> sections owes the keyword: its share of the page's count.
    /// </summary>
    /// <remarks>
    /// One definition, read by <see cref="SeoBodyInstruction"/>, which tells the writer the number,
    /// and by the check that counts what came back
    /// (<c>GccGenerateService.GenerateSectionsInBatchesAsync</c>) -- so a batch is never told one
    /// figure and measured against another. The page's count is 0.6% of its word floor: mid-band in
    /// the scorer's 0.4-2.5%, so a page that lands near it passes without reading as stuffed, and the
    /// lede and the appended questions section, which this does not count, cannot dilute it below the
    /// floor.
    /// </remarks>
    internal static int SeoKeywordMentionsFor(string contentType, int sectionsInThisCall, int sectionsInThePage)
    {
        var (minWords, _, _) = GccLongFormTypes.GetSeoLengthRules(contentType);
        var mentions = Math.Max(4, (int)Math.Round(minWords * 0.006));
        if (sectionsInThisCall >= sectionsInThePage) return mentions;

        var share = Math.Max(1d, sectionsInThisCall) / Math.Max(1, sectionsInThePage);
        return Math.Max(1, (int)Math.Round(mentions * share));
    }

    private static string SeoBodyInstruction(
        string keyword,
        string contentType,
        int sectionsInThisCall,
        int sectionsInThePage,
        bool ownsTheKeywordHeading)
    {
        var (minWords, minSections, _) = GccLongFormTypes.GetSeoLengthRules(contentType);
        var mentions = SeoKeywordMentionsFor(contentType, sectionsInThePage, sectionsInThePage);
        var perSection = minWords / Math.Max(minSections + 2, 1);

        var writesWholePage = sectionsInThisCall >= sectionsInThePage;
        var wordsHere = perSection * Math.Max(1, sectionsInThisCall);
        var mentionsHere = SeoKeywordMentionsFor(contentType, sectionsInThisCall, sectionsInThePage);

        return new StringBuilder()
            .AppendLine("=== WHAT THIS PAGE IS SCORED ON ===")
            .AppendLine(writesWholePage
                ? $"LENGTH: {minWords:N0} words is the floor, not the aim. Below it the page fails "
                  + "outright. Across your sections that is roughly "
                  + $"{perSection:N0}+ words each -- three to five substantial paragraphs per section. "
                  + "A total alone is satisfiable by one long section and several thin ones, which is "
                  + "how a piece asked for a floor came back at half of it."
                : $"LENGTH: the finished page has a {minWords:N0}-word floor and fails outright below "
                  + $"it. You are writing {sectionsInThisCall} of its {sectionsInThePage} sections. Aim "
                  + $"for about {wordsHere:N0} words here -- roughly {perSection:N0}+ each, three "
                  + "to five substantial paragraphs per section. A short batch is not made up by "
                  + "another one; it is simply the page arriving under the floor.")
            .AppendLine(writesWholePage
                ? $"SECTIONS: at least {minSections} top-level sections, and more where the subject "
                  + "has more to say. Each one covers something the others do not."
                : $"SECTIONS: exactly the {sectionsInThisCall} top-level sections you were assigned, "
                  + "each covering something the others -- yours and the other calls' -- do not.")
            // The exact phrase, and said so. This read "and its natural variants", and the scorer
            // counts the phrase and nothing else: a tool page for "Automated Approval Workflows" came
            // back writing "approval workflows" and "automated workflows" throughout, with the phrase
            // itself four times in 2,024 words -- 0.2% against a 0.4% floor -- and a heading reading
            // "Manual Approval Workflows" where the keyword was owed (2026-10-05). The writer was
            // invited to use variants and then scored as if it had not used the keyword.
            .AppendLine(writesWholePage
                ? $"KEYWORD FREQUENCY: the exact phrase \"{keyword}\" appears at least {mentions} times "
                  + "across the piece -- roughly once every 200 words. It is counted as that phrase, "
                  + "word for word: a shortened or reworded form of it is fine prose and is not "
                  + "counted. Never twice in a paragraph. One mention in a long piece fails this as "
                  + "surely as forty do."
                : $"KEYWORD FREQUENCY: the exact phrase \"{keyword}\" appears at least {mentions} times "
                  + $"across the finished page, so at least {mentionsHere} in your sections -- roughly "
                  + "once every 200 words. It is counted as that phrase, word for word: a shortened or "
                  + "reworded form of it is fine prose and is not counted. Never twice in a paragraph. "
                  + "Your sections are counted when they come back, and a batch under its share is "
                  + "reported with the draft.")
            .AppendLine(ownsTheKeywordHeading
                ? $"HEADINGS: at least one H2 contains the exact phrase \"{keyword}\" -- that phrase, "
                  + "word for word, not a variant of it. Headings answer the question a "
                  + "reader arrived with -- \"What it costs to keep doing this by hand\" rather than "
                  + "\"Overview\" -- because a heading that names its question is the one a search "
                  + "engine and an answer engine can both use."
                : $"HEADINGS: the page's keyword-bearing H2 is written by another call, so none of "
                  + $"yours needs \"{keyword}\" in it -- put it in a heading only where it is the "
                  + "natural phrasing anyway. Headings answer the question a reader arrived with -- "
                  + "\"What it costs to keep doing this by hand\" rather than \"Overview\" -- because a "
                  + "heading that names its question is the one a search engine and an answer engine "
                  + "can both use.")
            .AppendLine(
                "DIRECT ANSWERS: each section answers its own heading in its first two sentences, "
                + "then develops it. Burying the answer four paragraphs down loses the reader and "
                + "loses the extract.")
            .AppendLine(
                "STRUCTURE: use a list where the content genuinely is a list -- steps, criteria, "
                + "what is included -- because a list is extracted more reliably than the same "
                + "material written as prose. Never as decoration, and never a list of three used "
                + "for rhythm.")
            .ToString()
            .TrimEnd();
    }

    /// <summary>
    /// The SEO rules that belong to a plan: what the headings have to do.
    /// </summary>
    private static string SeoOutlineInstruction(string keyword) =>
        $"HEADINGS AND THE KEYWORD: at least one H2 contains \"{keyword}\", and not more than two -- "
        + "a page with it in every heading fails on density. Every heading names the question its "
        + "section answers, in the reader's words, never a label like \"Overview\" or \"Key "
        + "Considerations\". A heading that states its question is the one an answer engine quotes.";

    /// <summary>
    /// The SEO rules that belong to an opening: the keyword, and the answer.
    ///
    /// <para>
    /// This lived in the outline prompts, which plan the piece and do not write the lede, so
    /// "keyword in lede" failed on a draft whose lede prompt had never been told.
    /// </para>
    /// </summary>
    private static string SeoLedeInstruction(string keyword) =>
        $"KEYWORD AND ANSWER: the opening contains \"{keyword}\" in a sentence that would be there "
        + "anyway -- not as a label, not bolted onto the first line. And it answers the question the "
        + "title asks within its first hundred words, before any history, context or scene-setting. "
        + "A reader who stops after the opening should already have the answer; everything after it "
        + "is why.";

    private const string HeadingProvenanceInstruction =
        "Every section you write, at every level including nested children, must be licensed by real " +
        "material above -- never invented from nothing. Tag each one with the \"provenance\" field the " +
        "JSON shape requires, using the exact brief field name, PAA question, competitor heading, " +
        "site subtopic, or retrieved source it is drawn from. A tag is the source's own text copied " +
        "exactly, never a description of where you found it: \"site:Invoice capture\", not " +
        "\"site:the subtopics list\". The five forms are \"plan:\" for a section you were assigned, " +
        "\"brief:<field name>\" -- the field\u0027s name, not the line as printed, so \"brief:primaryIntent\" " +
        "and never \"brief:Primary intent: ...\" -- \"paa:<question>\", \"competitor:<heading>\", " +
        "\"site:<subtopic>\" with no trailing punctuation, and \"evidence:<source>\" for retrieved " +
        "material, where <source> is the partner name, the page title, the section title or the host " +
        "exactly as the retrieved passage gives it. " +
        "If a subsection cannot honestly be tagged this way, do not write it. " +
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
            "Never block-quote any of it. A publisher does not quote themselves on their own site -- " +
            "their voice is the whole page, so their own words in a quote box read as padding. A " +
            "blockquote is for words that belong to someone else and carries a cite saying whose: a " +
            "partner's claim from the partner's own page, a named customer's testimonial. The " +
            "publisher's own material is simply used.");
        block.AppendLine(
            "Where their site is silent, write from the evidence -- but never fill their silence with " +
            "a plausible-sounding invention about them.");
        return block.ToString();
    }

    /// <summary>
    /// The one system message every long-form call sends: who is writing, how, the rules that never
    /// change, and the shape of the answer. Nothing in it depends on the project, the page, the batch,
    /// the evidence or a revision, so for a given contract it is the same text on every call -- which
    /// keeps the rules out from between the data they govern, and lets a provider's prompt cache read
    /// the whole prefix from the second call on.
    /// </summary>
    /// <remarks>
    /// Everything that varies is in the user message: what this type of page is, the brief, the
    /// publisher's site, the evidence, the continuity of the opening, the revision notes, and the
    /// assignment itself. See plans/content-creator-prompt-restructure.md.
    /// </remarks>
    internal static string SystemPrompt(string outputContract) =>
        new StringBuilder()
            .AppendLine("You are a senior consultant at an IT consulting firm that specializes in AI implementation, writing for that firm's prospective clients: expert, direct and consultative.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(FillerBanInstruction)
            .AppendLine(HeadingCraftInstruction)
            .AppendLine(SectionVarietyInstruction)
            .AppendLine(CurrencyInstruction)
            .AppendLine(GroundingInstruction)
            .AppendLine(ContentOnlyInstruction)
            .AppendLine("OUTPUT: respond with ONLY the JSON this contract describes -- no code fences, no commentary. Where the user message assigns sections, return one entry per assigned section, in the order given.")
            .AppendLine(outputContract)
            .ToString()
            .TrimEnd();

    /// <summary>Said last in every user message, so the evidence is not the final thing the model reads.</summary>
    private const string AnswerInTheContract =
        "Answer in the JSON the output contract in the system message describes. You write each heading yourself.";

    private const string GroundingInstruction =
        "GROUNDING: state a figure, price, percentage, customer, case study or quotation only when the "
        + "evidence in the user message gives it, and attribute it to the source that published it. Where "
        + "the evidence is silent, write less. Never invent a feature, an integration, an outcome or a "
        + "customer to fill a section.";

    private const string ContentOnlyInstruction =
        "CONTENT ONLY: the text of every run is plain words. Headings and lists are fields of the JSON, "
        + "and neither is ever characters in the text -- no #, no <h2>, no **, no [text](url), no URL.";

    /// <summary>
    /// How the prose sounds. Extended 2026-09-27 with Jeff's own brief, after a finished blog was
    /// reported as 100% AI-detected: "Act as a human copywriter explaining your draft to a colleague
    /// over coffee", clear everyday language, active voice, no formal opener and no wrap-up.
    /// </summary>
    private const string HumanRegisterInstruction =
        "WHO IS TALKING: a senior consultant who knows this work, advising a client in plain language. " +
        "Clear, everyday words. Active voice -- somebody does something, rather than something being " +
        "done. Direct and honest. " +
        "No formal opener and no wrap-up: do not introduce what you are about to cover, and do not " +
        "close by telling the reader what they just read. Start talking, and stop when you are done. " +
        "HOW THIS READS: the giveaway is rhythm, not vocabulary. " +
        "Vary sentence length on purpose. A paragraph whose sentences are all fifteen to twenty-five " +
        "words reads as machine-written however good each one is. Use short sentences -- three to " +
        "eight words -- and let some run long. " +
        "Vary paragraph length too: some are one sentence, some are six. Never a page of even blocks. " +
        "Drop the scaffolding: no Moreover, Furthermore, Additionally, Consequently, In conclusion, " +
        "It is worth noting, It is important to note. Start the sentence. " +
        "Break the symmetry: no \"not just X, but Y\", no three-item lists used for cadence rather " +
        "than because there are exactly three things, no three adjectives in a row, no paired " +
        "clauses balanced against each other line after line. " +
        "Never restate. A paragraph that summarises the paragraph above it is filler with good " +
        "manners, and a closing recap of points already made is the same thing at the end. " +
        "Commit. Say which option is worse and why, say what you would not do, leave something out " +
        "because it does not matter. Covering every angle evenly is how a page says nothing. " +
        "Be specific in a way a generic page could not be: \"a twelve-person AP team\" rather than " +
        "\"businesses\", the actual figure from the evidence rather than \"significant savings\", the " +
        "named product rather than \"leading platforms\". One concrete detail per section that could " +
        "not have been written about any other subject.";

    /// <summary>
    /// Words that mark a page as machine-written on sight.
    ///
    /// <para>
    /// The second list is Jeff's, given 2026-09-27 after a finished blog came back reported as 100%
    /// AI-detected. They are not bad words in themselves -- they are the words a model reaches for
    /// when it has nothing specific to say, which is why a detector and a reader both notice them.
    /// </para>
    /// </summary>
    private const string FillerBanInstruction =
        "Ban filler: cutting-edge, paradigm shift, transformative potential, seamless transition, " +
        "maximize ROI, unlock value. Write specific, verifiable claims instead of hype adjectives. " +
        "Ban these words outright, in any form: delve, testament, unlock, tapestry, beacon, realm, " +
        "dynamic, pivotal, navigating. If one of them is the word you want, the sentence has not " +
        "decided what it is saying yet -- say the thing instead.";

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
    /// <summary>
    /// Money is stated in US dollars or not at all (Jeff, 2026-10-05: "Any time a currency is listed
    /// needs to be in USD"). A tool page had stated ApprovalMax's prices in Australian dollars, because
    /// that is how the crawled page gave them. The guard enforces this
    /// (<c>GccDraftGuard</c>'s currency check); this tells the writer before it writes.
    /// </summary>
    internal const string CurrencyInstruction =
        "MONEY IS IN US DOLLARS ONLY: state a price or any amount of money only in US dollars, written " +
        "with a $ sign. When the evidence gives an amount in another currency (AUD, A$, GBP, £, EUR, € " +
        "and so on), do not state that amount at all -- never convert it, and never keep the number and " +
        "drop the currency. Say the vendor publishes its pricing and leave the figure out. A block " +
        "quotation is the one exception: it is reproduced exactly as published, currency included.";

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
    /// What the calls before this one wrote for the page, so this one does not make their points again: each
    /// earlier section's heading, how it opens, and the figures it cited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A long-form body is written two sections to a call, and every call was shown the outline and the
    /// opening but none of another call's text. Each therefore reached for the same strongest passage, the
    /// same figure and the same example in the evidence they all share. Calls run in order, so the earlier
    /// sections exist by the time a later one is asked (Jeff, 2026-10-10).
    /// </para>
    /// <para>
    /// It is not the earlier text. A section is one line here -- heading, first sentence, figures -- so the
    /// fifth call of a ten-section page reads a few hundred words of this beside tens of thousands of
    /// evidence. Plain text, built by code; no model is asked what the page says.
    /// </para>
    /// </remarks>
    internal static string? BuildWrittenSoFarBlock(IReadOnlyList<Section>? writtenSoFar)
    {
        if (writtenSoFar is not { Count: > 0 }) return null;

        var lines = new List<string>();
        foreach (var section in writtenSoFar)
        {
            if (string.IsNullOrWhiteSpace(section.Heading)) continue;

            var line = new StringBuilder("- ").Append(section.Heading.Trim());
            var opening = FirstSentenceOf(section);
            if (opening.Length > 0) line.Append(" -- ").Append(opening);

            var figures = GeekAPI.Services.ContentCreator.Guardrail.GccFigureGrammar
                .Find(ContentDocumentText.Flatten(new ContentDocument(section, [])))
                .Select(figure => figure.Written.Trim())
                .Where(written => written.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxFiguresPerWrittenSection)
                .ToList();
            if (figures.Count > 0) line.Append(" Figures cited: ").Append(string.Join(", ", figures)).Append('.');

            lines.Add(line.ToString());
        }

        if (lines.Count == 0) return null;

        return new StringBuilder()
            .AppendLine("=== ALREADY WRITTEN ON THIS PAGE (earlier sections -- do not make these points again) ===")
            .AppendLine("One line for each section an earlier call wrote: its heading, how it opens, and the figures it cited.")
            .AppendLine(string.Join(Environment.NewLine, lines))
            .Append(
                "Your sections cover different ground. Do not restate a point, an example or a figure from " +
                "this list. Where one of them matters to your section, refer back to it in a clause and go on.")
            .ToString();
    }

    /// <summary>The most figures listed for one earlier section: enough to name what it leaned on, not its every number.</summary>
    private const int MaxFiguresPerWrittenSection = 8;

    /// <summary>The longest opening sentence shown for one earlier section.</summary>
    private const int MaxWrittenOpeningChars = 220;

    /// <summary>
    /// How a section opens: the first sentence of its first paragraph, or of its first subsection's when the
    /// section itself begins with a subheading.
    /// </summary>
    private static string FirstSentenceOf(Section section)
    {
        var first = ContentDocumentText.ParagraphTexts(section).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
        if (first is null)
        {
            foreach (var child in section.Children)
            {
                var fromChild = FirstSentenceOf(child);
                if (fromChild.Length > 0) return fromChild;
            }

            return string.Empty;
        }

        var text = first.Trim();
        var end = -1;
        foreach (var stop in new[] { ". ", "? ", "! " })
        {
            var at = text.IndexOf(stop, StringComparison.Ordinal);
            if (at >= 0 && (end < 0 || at < end)) end = at;
        }

        var sentence = end >= 0 ? text[..(end + 1)] : text;
        return sentence.Length <= MaxWrittenOpeningChars
            ? sentence
            : sentence[..MaxWrittenOpeningChars].TrimEnd() + "...";
    }

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
    ///
    /// <para>
    /// An ask still needs somewhere to land, and naming the action was not enough on its own: the
    /// writer knew to ask and had no destination, so it sent the reader off the page. Jeff,
    /// 2026-09-26: "the shared schedule component is on each page including Blog posts and has a
    /// link of href="#consultationAppointment2xl"". The scheduler is already below whatever the
    /// reader is reading, which makes the destination an in-page anchor, not a contact page, not a
    /// URL, and not an email address. That is the same missing-wire shape as KnownCrawlTools and as
    /// the publisher profile: the component exists, the reader is already on it, and nothing told
    /// the writer so.
    /// </para>
    /// </summary>
    /// <summary>
    /// The block quotation every tool page carries, and where its words have to come from.
    ///
    /// <para>
    /// A tool page is an advertisement for that partner, which is what makes a quote box belong on
    /// it. Jeff, 2026-09-26: <i>"I want a blockquote in each tool"</i>. It is a required element of
    /// the type, not an option the writer weighs -- <see cref="BuildPublisherSiteBlock"/> already
    /// says a blockquote is for "a partner's claim from the partner's own page", and on this page
    /// that is the whole subject.
    /// </para>
    ///
    /// <para>
    /// Required does not mean invented. The words have to be a span already in the evidence, cited
    /// to the page it came from, and <c>GccToolQuoteGuard</c> rejects the draft when they are not --
    /// this instruction asks, that guard enforces. A path with no partner evidence cannot satisfy
    /// either and must refuse before it gets here, rather than reach this prompt and fabricate.
    /// </para>
    /// </summary>
    /// <summary>
    /// The one block quotation a tool page carries, and — the part this got wrong — what it has to be
    /// <i>about</i>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The quote is the partner saying how their product solves the problem</b> — Jeff, 2026-10-01:
    /// <i>"The quote the application is suppose to return is how Tool x solves problem y."</i> This said
    /// only "the span that best supports a point the page actually makes", which is any sentence on the
    /// partner's site, and the candidate list is already shape-filtered rather than
    /// meaning-filtered — so the writer was choosing from forty arbitrary sentences with no stated
    /// target. That is the same misunderstanding the probe's selector carried: it preferred a customer
    /// testimonial, which is praise, not a solution.
    /// </para>
    /// <para>
    /// The authoritative wording for this already existed and the writer never saw it:
    /// <c>GccAngleQuoteQuestion</c>'s <c>problem_solution</c> spec asks for <i>"a major pain point of
    /// the manual or status-quo way of handling {subject}, and how this partner's product resolves
    /// it"</i>. A tool page is about one product and one keyword, so that requirement holds whatever
    /// the brief's angle says — the angle changes which <i>kind</i> of answer is preferred, and the
    /// create does not carry one yet.
    /// </para>
    /// </remarks>
    /// <summary>What every batch after the first is told: the page's one quotation is not its to write.</summary>
    private static string ToolQuotationWrittenElsewhere(string productName) =>
        "NO QUOTATION IN THIS PART: this page's one block quotation of " + productName + " is written by "
        + "another call. Write no paragraph of type \"quote\" here. When a section needs what "
        + productName + " says, put it in your own words and attribute it: name the page it comes from "
        + "in a short run of its own, with that page's URL as that run's \"href\" -- never an href on the "
        + "sentence or the paragraph.";

    private static string ToolQuotationInstruction(string productName, string targetKeyword) =>
        "QUOTE " + productName + " ONCE, IN THEIR OWN WORDS: this page carries at most one block "
        + "quotation -- a paragraph of type \"quote\" -- and only when a listed span earns it. Choose it from the "
        + "numbered QUOTABLE SPANS below and answer with its number: {\"type\":\"quote\","
        + "\"candidate\":<number>,\"runs\":[],\"cite\":null}. Do not write the sentence out, and do "
        + "not shorten, edit or combine spans -- the words and the cite are taken from the list by "
        + "that number, not from your reply, so anything you type into the quotation is discarded "
        + "and a number that is not on the list is refused.\n"
        + "WHAT THE QUOTE MUST SAY: how " + productName + " solves the problem this page is about -- "
        + "the pain of doing " + targetKeyword
        + " the manual or status-quo way, and what their product does about it. Choose the span that "
        + "states a capability, a mechanism or a measured outcome against that problem.\n"
        + "NOT a compliment and NOT a testimonial: \"we love it\", \"the team has been great\", "
        + "\"best decision we made\" say nothing about the problem and do not qualify however warmly "
        + "they read. A general description of the product with no problem attached does not qualify "
        + "either. If no span in front of you says how the problem is solved, there is no quotation to "
        + "write -- say so by writing none rather than stretching the nearest sentence to fill the "
        + "slot.\n"
        + "Put it in the section whose point it supports, where the reader has just been told "
        + "something and the quote is " + productName + " saying it themselves -- not stacked at the "
        + "top, not left to the end as decoration. "
        + "What it may not be: a paraphrase tidied into quotation marks, a claim you are confident "
        + "they make, wording assembled from several places, or a sentence of your own typed into a "
        + "quote paragraph without a number. If no listed span says it, it is not quotable, and the "
        + "draft is rejected rather than published with an invented one.";

    /// <summary>
    /// The spans the writer may quote -- the same list GccToolQuoteGuard will check the draft
    /// against.
    /// </summary>
    /// <remarks>
    /// Shown because the guard and the writer have to be looking at one list. The instruction used
    /// to say "take its words from the partner evidence below ... a testimonial or an isolated
    /// claim", which pointed at the extraction JSON -- so a partner whose extraction filed nothing
    /// under those two headings left the writer with nothing verbatim in front of it, and it
    /// correctly wrote no quotation, while the guard held spans from the retrieved pages that the
    /// writer never saw. The page was then refused for not using them (2026-10-01: "28 quotable
    /// partner span(s) were supplied and none was used").
    /// </remarks>
    private static string QuotableSpansBlock(IReadOnlyList<GccQuoteCandidate> candidates)
    {
        var sb = new StringBuilder();
        // Numbered, because the number is what the writer answers with. Unnumbered, the only way to
        // choose one was to retype it, and a retyped sentence has to be found again -- which is the
        // match that lost Stampli's page on 2026-10-03. The cite is printed so the writer can see
        // where each span comes from; it is not something the writer supplies.
        sb.AppendLine("QUOTABLE SPANS -- the only wording this page may quote, by number:");
        foreach (var candidate in candidates)
        {
            sb.AppendLine($"{candidate.Id}. \"{candidate.Text}\"  [cite: {candidate.PageUrl}]");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The closing instruction for one call of a batched body: the real ask when this call owns the
    /// page's final section, and an explicit "do not close" when it does not.
    ///
    /// <para>
    /// "The last section ends by asking for..." is unambiguous in a single call and ambiguous in
    /// every batch of a page. Handed to all of them it produces a closing per call -- three asks and
    /// three sign-offs on one page; handed to none of them the page just stops. So the page's last
    /// batch gets the ask and the others are told plainly that the page continues past them.
    /// </para>
    /// </summary>
    private static string BatchClosingInstruction(
        ProjectGenerationContext context,
        IReadOnlyList<string> batch,
        IReadOnlyList<string>? fullOutline) =>
        OwnsTheClosing(batch, fullOutline)
            ? context.PageBuildsClosing ? PageAddsTheClosingInstruction : ClosingCallToActionInstruction(context)
            : "This call does not end the page -- sections you were not given follow yours. End your "
              + "last section on its own material: no summary of what came before, no wrap-up of the "
              + "page, and no call to action. "
              + (context.PageBuildsClosing
                  ? "The page adds its own closing after its final section."
                  : "The closing is written by the call that owns the final section.");

    /// <summary>
    /// What the call that ends a Content Creator page is told: the page adds the booking line itself
    /// (<c>GccClosing</c>), so the last section ends on its own material and asks for nothing. It replaced
    /// a block of about three thousand characters of what not to write, which handed the writer the brief's
    /// internal CTA setting and the operator's questions and was refused for what it did with both.
    /// </summary>
    private const string PageAddsTheClosingInstruction =
        "END OF THE PAGE: the page adds its own booking line after your last section, so end that section on "
        + "a concrete point of its own material -- a decision, a figure, a next step inside the topic. Write "
        + "no call to action and nothing that asks the reader to book, call, click or answer anything, and do "
        + "not trail off into a reflection (\"consider how this could apply\").";

    /// <summary>
    /// Whether this call writes the page's final section. True for an unbatched call, which owns
    /// the whole outline and therefore its end.
    /// </summary>
    private static bool OwnsTheClosing(
        IReadOnlyList<string> batch, IReadOnlyList<string>? fullOutline) =>
        fullOutline is not { Count: > 0 }
        || batch.Count == 0
        || string.Equals(batch[^1], fullOutline[^1], StringComparison.Ordinal);

    private static string ClosingCallToActionInstruction(ProjectGenerationContext context)
    {
        var scheduler = context.ConsultationAnchorHref;
        var hasScheduler = !string.IsNullOrWhiteSpace(scheduler);

        var ask = !string.IsNullOrWhiteSpace(context.CtaType)
            ? context.CtaType
              + (string.IsNullOrWhiteSpace(context.CtaLabel) ? string.Empty : $", worded as \"{context.CtaLabel}\"")
            : hasScheduler && !string.IsNullOrWhiteSpace(context.ConsultationCtaLabel)
                ? $"the reader to book time, worded as \"{context.ConsultationCtaLabel}\""
                : "the one action this reader should take next";

        // With no anchor configured there is no destination to give, and a destination the writer
        // makes up is a link to a page that does not exist. The ask is then made in words only.
        var destination = hasScheduler
            ? "That ask is a link: put it on a run in the closing paragraph with href "
              + $"\"{scheduler}\". The scheduler it opens is part of every page on this site -- blog "
              + "posts included -- so it is already further down the page the reader is on. Write it "
              + "as something they do here, not somewhere they go: never \"visit our site\", never "
              + "\"head over to our contact page\", never an email address, and never any other URL "
              + "or path. That href is the only destination this closing gets."
            : "Name no destination: you have not been given one, and a URL, path or email address "
              + "you supply yourself points at a page that does not exist. Make the ask in words.";

        return "CLOSING: the last section ends by asking for " + ask + ". One ask, stated plainly, "
            + "addressed to the reader, naming who does what next. " + destination + " "
            + "Do NOT end on a reflection -- \"it may be beneficial to explore\", \"consider how this "
            + "could apply\", \"these examples provide insight\", \"to understand the potential impact "
            + "further\". Those name no action and no actor; they are a piece trailing off, and they "
            + "are what every draft has closed on so far. If the reader finishes and does not know "
            + "what they are being asked to do, the ending has failed.";
    }

    /// <summary>
    /// How the opening reads the retrieved evidence. Needed because the research block is written
    /// for the body: its rule 5 says a tool with a "Target Entity Match" line "is named in the
    /// piece and its claims are cited from those passages", which is right across a whole article
    /// and wrong in three paragraphs -- handed the block unqualified, the lede would open by
    /// listing every partner.
    ///
    /// <para>
    /// The opening still needs the evidence, because it is where the page's factual claims are
    /// set. The pillar lede prompt has said "a number may appear only if it is in the supplied
    /// evidence or published by this publisher" since it was written, while no evidence was
    /// supplied to it -- so the one rule that bounded its figures could not be satisfied or
    /// broken. That is what this fixes: the constraint now has something to resolve against.
    /// </para>
    /// </summary>
    private const string LedeEvidenceInstruction =
        "HOW TO USE THE EVIDENCE BELOW IN THE OPENING: it is here so the opening is true, not so " +
        "it gets covered. The opening names no partner or tool unless the brief's angle is about " +
        "that one product -- the body names them, with citations, section by section. Do not open " +
        "with a list of vendors, and do not attach a capability to one here. " +
        "What the evidence is for: any figure, timeframe, cost, volume or limitation in these " +
        "paragraphs must appear in a passage below, and the pain you open on must be a pain the " +
        "passages actually describe -- not a generic one written to sound like the category. " +
        "If the evidence does not support a number, write the sentence without one. An opening " +
        "with no figures is finished; an opening with an invented figure is not.";

    private static readonly string LedeLengthInstruction =
        $"LENGTH: the opening runs {ContentLengthTargets.LedeRangeLabel} words across 3-4 paragraphs, " +
        $"and no paragraph in it is shorter than {ContentLengthTargets.LedeParagraphMinWords} words. " +
        "This is the paragraph that decides whether the rest gets read, so give it room: the hook, " +
        "the turn that names what is at stake, and the line that says who this is for and what they " +
        "get. A three-sentence opening is not a short opening, it is an opening that has not started.";

    /// <summary>
    /// The opening asks nothing of the reader. The page's one ask is at its end, built by code
    /// (Jeff, 2026-10-07); an opening that books, calls or links a vendor is a second ask in the wrong
    /// place. The tool page's opening read "CTA: book_now" three times (the lede-type guidance, the
    /// brief block and a call-to-action line), took it for a button label, and linked the vendor's
    /// homepage -- the page was refused for a link that led nowhere the evidence went.
    /// </summary>
    private const string LedeAskInstruction =
        "THE OPENING ASKS NOTHING OF THE READER: it ends on its own material. The page's one invitation to " +
        "the reader sits at its end, so this opening does not ask them to book, call, sign up or click.";

    /// <summary>
    /// The lede carries a heading. It is this page's first H2 -- <c>PillarPrompts</c> says so
    /// ("Its lede IS its first H2"), <c>GccGenerateService</c> stores it as
    /// <c>lede with { Tag = "h2" }</c>, its outline slot is a <see cref="SectionSlot.Cover"/> the
    /// writer names, and <c>SectionHtmlRenderer</c> emits the tag. Jeff, 2026-09-29: "While you are
    /// correct normally lede paragraphs have no heading, in this codebase they do."
    ///
    /// <para>
    /// This contract had no "heading" key between 2026-09-23 and 2026-09-29, while the pillar
    /// prompt's user block went on saying "You write its heading." -- so whether a page shipped an
    /// h2 on its opening came down to whether the model volunteered a key nobody had asked it for.
    /// Two of the three places that describe the lede said one thing and the third said the other.
    /// </para>
    ///
    /// <para>
    /// <b>Why it was removed, and why that reason is handled here rather than by removing it
    /// again.</b> The lede rendered through the same path as a body section, so a page could carry
    /// two headlines stacked -- the redundancy Jeff reported as "How Automated Data Entry &amp;
    /// Processing Can Transform Your Business then Transform Your Business with Automated Data Entry
    /// &amp; Processing seem redundant". That is a real defect and it is specifically what
    /// <see cref="LedeHeadingInstruction"/> addresses: the heading must not restate the title, and
    /// it is held to the same craft rules as every other heading on the page. The alternative --
    /// no heading at all -- also removes the page's first H2, which the outline counts on.
    /// </para>
    ///
    /// <para>
    /// The twelve <c>ledeType</c> values describe the opening <i>paragraphs</i>, not the heading.
    /// Nobody picks "anecdotal" for a heading; the type shapes the prose under it.
    /// </para>
    /// </summary>
    private static readonly string LedeJsonContract =
        "{\"ledeType\": \"summary\"|\"immediateIdentification\"|\"delayedIdentification\"|\"singleItem\"|\"anecdotal\"|\"narrative\"|\"sceneSetting\"|\"startlingStatement\"|\"directAddress\"|\"question\"|\"quote\"|\"wordplay\", " +
        "\"heading\": \"...\" (this page's first H2, in your own words -- see the heading rules; never a restatement of the title), " +
        "\"paragraphs\": [" + ParagraphJsonShape + ", ...] (the opening itself, running under that heading)" +
        "}";

    /// <summary>
    /// The rules for the one heading the opening writes. Separate from
    /// <see cref="HeadingCraftInstruction"/> because the opening's heading has a constraint no other
    /// heading has: the page title sits immediately above it, so a heading that restates the title
    /// prints the same sentence twice. That redundancy is why the heading was dropped from the
    /// contract in 2026-09-23 rather than governed -- see <see cref="LedeJsonContract"/>.
    /// </summary>
    private const string LedeHeadingInstruction =
        "THE OPENING'S HEADING: write one, and it is this page's first H2. " +
        "It must not restate the page title. The title is printed immediately above it, so a heading " +
        "that repeats the title's claim in different words prints the same sentence twice -- " +
        "\"How Invoice Capture Can Transform Your Business\" above \"Transform Your Business with " +
        "Invoice Capture\" is one thought, set twice, and the reader reads it as a mistake. " +
        "The title names what the page is about; this heading names what the opening itself does -- " +
        "the situation the reader is in, or the thing this page settles for them. " +
        "It is not \"Overview\", \"Introduction\" or \"Lede\": those name the slot, not the content.";


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
        // page title. They were removed on 2026-09-23 with the heading itself, then restored on
        // 2026-09-29 with it -- as LedeHeadingInstruction, stated once next to the heading's other
        // rules rather than buried in the lede-type guidance, which is about choosing the opening's
        // twelve types and not about what its heading may say.

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
            || !string.IsNullOrWhiteSpace(context.LengthBand) || !string.IsNullOrWhiteSpace(context.WritingNotes)
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
            // No CTA line: the opening asks nothing of the reader (LedeAskInstruction), and the brief's
            // CtaType is an internal setting ("book_now"), not words, which the writer took for a
            // button label.
            if (!string.IsNullOrWhiteSpace(context.LengthBand))
                sb.AppendLine($"Length band: {context.LengthBand}");
            if (!string.IsNullOrWhiteSpace(context.WritingNotes))
                sb.AppendLine($"Writing notes: {context.WritingNotes}");
            // Only this brief's row. It used to print all four, which put "problem_solution",
            // "comparative", "case_study_data" and "ultimate_guide" into the prompt as bare tokens a
            // line above "Pick ONE ledeType" -- and the model returned "problem_solution" AS the
            // ledeType, which ParseLedeTypeStrict refused, failing the blog outright. Three of the four
            // rows could never apply anyway: angle is a required brief field, so exactly one is live and
            // the rest were noise that did nothing but offer a wrong answer.
            var anglePreference = LedeTypesPreferredForAngle(context.ContentAngle);
            if (anglePreference is not null)
                sb.AppendLine($"For this brief's angle, prefer one of: {anglePreference}");
            sb.AppendLine("Lede guidance by audience:");
            sb.AppendLine("  affinity/in_market → more narrative/anecdotal room");
            sb.AppendLine("  detailed_demographics/your_data → more directAddress/question");
            sb.AppendLine("Lede guidance by intent/funnel:");
            sb.AppendLine("  informational → summary/narrative/sceneSetting; transactional/commercial_investigation → directAddress/question/singleItem; navigational → immediateIdentification");
            sb.AppendLine("  awareness → anecdotal/narrative/sceneSetting; consideration → question/singleItem; action → directAddress/singleItem");
            sb.AppendLine("If audience notes conflict with segment, follow notes. Tone and E-E-A-T must be honored in lede voice.");
            // Said explicitly because the guidance above reads "<brief value> -> <ledeTypes>" and the
            // model answered with the left side. The 12 names are the only legal answers.
            sb.AppendLine("The angle, audience, intent and funnel-stage names above are brief values, NOT "
                + "ledeType values. NEVER return one of them as ledeType -- the only legal ledeType values "
                + "are the 12 listed above.");
        }
        sb.Append("Pick ONE ledeType from the 12 that best fits this brief (audience + angle + intent/funnel/tone) + heading/topic.");
        return sb.ToString();
    }

    /// <summary>
    /// The lede types that suit one angle, or null when the angle is unrecognised.
    /// </summary>
    /// <remarks>
    /// Returns the right-hand side only. The caller must not print the angle name beside it: the angle
    /// reaching the model as a bare token next to the ledeType ask is what produced
    /// <c>ledeType: "problem_solution"</c> and a refused blog. The angle is already stated once, in prose,
    /// by <see cref="DescribeAngle"/>.
    /// </remarks>
    private static string? LedeTypesPreferredForAngle(string? angle) =>
        (angle ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "comparative" => "question, startlingStatement, singleItem -- stakes and contrast",
            "problem_solution" => "anecdotal, sceneSetting, directAddress, question -- pain first",
            "case_study_data" => "immediateIdentification, singleItem, quote, startlingStatement -- evidence first",
            "ultimate_guide" => "summary, delayedIdentification, directAddress -- comprehensive framing",
            _ => null,
        };

    private static string BuildBriefBodyGuidance(ProjectGenerationContext context)
    {
        var sb = new StringBuilder();
        // A page that builds its own closing has no use for the brief's CTA setting, and printing it ("CTA:
        // book_now -- weave naturally into closing") is what put the raw token in front of the writer.
        var ctaApplies = !context.PageBuildsClosing && !string.IsNullOrWhiteSpace(context.CtaType);
        var hasAny = !string.IsNullOrWhiteSpace(context.PrimaryIntent) || !string.IsNullOrWhiteSpace(context.BuyingStage) || !string.IsNullOrWhiteSpace(context.ToneOfVoice)
            || ctaApplies || !string.IsNullOrWhiteSpace(context.LengthBand) || !string.IsNullOrWhiteSpace(context.WritingNotes)
            || context.EeatSignals is { Count: > 0 }
            || !string.IsNullOrWhiteSpace(context.AudienceSegment) || !string.IsNullOrWhiteSpace(context.AudienceNotes)
            || context.AudienceDetails is { Count: > 0 };
        if (!hasAny) return string.Empty;
        sb.AppendLine("=== BRIEF CONTROLS (honor in body) ===");
        // Who the piece is for, first, because every line under it is a decision made about this
        // reader. The audience reached the lede prompts and the tool body's own block and no other
        // body -- so a pillar and a blog were written to a reader the operator had named and the
        // writer had never been told about. The tool page's copy of this line is gone now that one
        // rendering carries it; it never had the details list, which this does.
        if (!string.IsNullOrWhiteSpace(context.AudienceSegment) || !string.IsNullOrWhiteSpace(context.AudienceNotes)
            || context.AudienceDetails is { Count: > 0 })
        {
            var who = new StringBuilder("WHO THIS IS FOR: ");
            who.Append(string.IsNullOrWhiteSpace(context.AudienceSegment) ? "see the notes below" : context.AudienceSegment);
            if (context.AudienceDetails is { Count: > 0 } aud)
                who.Append($" — details: {string.Join(", ", aud)}");
            if (!string.IsNullOrWhiteSpace(context.AudienceNotes))
                who.Append($" — notes: {context.AudienceNotes}");
            sb.AppendLine(who.ToString());
            sb.AppendLine("Write to that reader specifically: their vocabulary, their constraints, the decision they are actually making. A passage that would read the same to any reader has not used this.");
        }
        if (!string.IsNullOrWhiteSpace(context.PrimaryIntent))
            sb.AppendLine($"Primary intent: {context.PrimaryIntent}" + (string.IsNullOrWhiteSpace(context.SecondaryIntent) ? "" : $" + {context.SecondaryIntent}"));
        if (!string.IsNullOrWhiteSpace(context.BuyingStage))
            sb.AppendLine($"Buying stage: {context.BuyingStage} — align examples/CTAs to funnel (awareness=educate, consideration=compare, action=convert).");
        if (!string.IsNullOrWhiteSpace(context.ToneOfVoice))
            sb.AppendLine($"Tone of voice: {context.ToneOfVoice} — hold this voice throughout (consultant_professional=objective authority, informational_instructional=clear stepwise, commercial_balanced=balanced benefits/tradeoffs).");
        if (context.EeatSignals is { Count: > 0 } ee2)
            sb.AppendLine($"E-E-A-T signals to demonstrate: {string.Join(", ", ee2)}.");
        if (ctaApplies)
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


    private const string SocialJsonContract =
        "{\"text\": string}";

    private const string ColdOutreachJsonContract =
        "{\"subject\": string, \"bodyText\": string (50-125 words), \"ctaLabel\": string}";

    private const string ImagePromptSectionItemJsonContract =
        "{\"sourceType\": \"pillar-hero|blog-hero|pillar|blog\", \"heading\": string (exact H2 text, or the exact title for a -hero item), \"order\": number, \"prompt\": string (40-400 words), \"width\": number, \"height\": number, \"imageModel\": string, \"stylePreset\": string, \"alchemy\": boolean, \"photoReal\": boolean, \"notes\": string|null}";

    private const string ImagePromptSectionsJsonContract =
        "{\"sections\": [" + ImagePromptSectionItemJsonContract + ", ...]}";


    public ChatCompletionRequest BuildArticleLedePrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft metadata,
        string? revisionNotes = null,
        string? existingLedeHeading = null,
        string? evidenceBlock = null)
    {
        var system = SystemPrompt(LedeJsonContract);

        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine("Write the opening lede for a schema.org TechnicalArticle pillar — third person, expert, consultative, like a senior consultant advising a prospective client.")
            .AppendLine($"Publisher positioning: {context.ImplementerPositioning}")
            .AppendLine(SeoLedeInstruction(context.TargetKeyword))
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("Do NOT start with \"How\" or a question.")
            .AppendLine("PAIN BEFORE SOLUTION (required): the first paragraph must open on the practitioner's pain with the manual / status-quo process ")
            .AppendLine("for the target keyword (cost, delay, error, risk, wasted hours) — before naming AI or an intelligent solution.")
            .AppendLine("Only after that pain is established, introduce how an AI-assisted approach changes the situation.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(LedeAskInstruction)
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

        var ledeNotes = ScopeRevisionNotesForLede(revisionNotes, existingLedeHeading, metadata.SectionOutline);
        var revisionBlock = BuildRevisionNotesBlock(ledeNotes, sectionHeading: existingLedeHeading);
        if (revisionBlock is not null)
        {
            user.AppendLine(revisionBlock);
        }

        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            .AppendLine($"Article title: {metadata.Title}")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Meta description: {metadata.MetaDescription}")
            .AppendLine(AnswerInTheContract);

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: 2048);
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
            .AppendLine("CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. " +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. ")
            .AppendLine("A hypothetical scenario may still use a concrete operational outcome for punch (e.g. \"month-end close compressed from two weeks to three days\"), ")
            .AppendLine("but it MUST be explicitly labeled hypothetical/illustrative — e.g. \"a hypothetical mid-sized manufacturer\" or ")
            .AppendLine("\"in a representative scenario\". Never phrase it as something that already happened to a real client. ")
            .AppendLine("Do not reuse a stock \"40% reduction\" (or similar) percentage across sections — vary outcomes and make them operationally specific.")
            .AppendLine(CurrencyInstruction)
            .AppendLine($"Target {ContentLengthTargets.PillarSectionMinWords}-{ContentLengthTargets.PillarSectionTargetMaxWords} words for this section. Do not write other sections.")
            .AppendLine("With the exception of the Lede, article headings are never questions.")
            .AppendLine("Tools listed in the research brief must be woven into sentences where they are relevant to this section — never as a Tools heading or catalog.")
            // The same decision the batch builder makes, made by the same code. This builder appended
            // ClosingCallToActionInstruction unconditionally, so every section of a pillar was told to
            // end the page -- survivable while the closing was one ask, and not once the operator's
            // discovery questions joined it: six of seven sections were handed them and told to pose
            // them.
            //
            // A single section IS a batch of one, so BatchClosingInstruction answers it as-is. Writing
            // the gate out again here is what let the two drift in the first place; the rule is one
            // rule and now has one implementation.
            .AppendLine(BatchClosingInstruction(context, [sectionHeading], fullOutline))
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




    public ChatCompletionRequest BuildBlogFaqSectionPrompt(
        ProjectGenerationContext context,
        BlogMetadataDraft metadata,
        IReadOnlyList<string> faqQuestions) =>
        FaqSectionPrompt(
            context, metadata.Title, faqQuestions,
            pageKind: "BlogPosting blog", heading: "Frequently Asked Questions", isRegeneration: false, revisionNotes: null);

    /// <summary>
    /// One FAQ section from the operator's own questions, for the pillar (People Also Ask) and the
    /// blog (Frequently Asked Questions) alike; the two differ only in the page named and the heading.
    /// Called once per batch of questions (<c>GccGenerateService.WriteFaqInBatchesAsync</c>), so a
    /// long list never meets the 3,072-token budget in one reply.
    /// </summary>
    private ChatCompletionRequest FaqSectionPrompt(
        ProjectGenerationContext context,
        string title,
        IReadOnlyList<string> faqQuestions,
        string pageKind,
        string heading,
        bool isRegeneration,
        string? revisionNotes)
    {
        var paaBlock = string.Join("\n", faqQuestions.Select((q, i) => $"  - Q{i + 1}: {q}"));

        var briefBody = BuildBriefBodyGuidance(context);
        var system = new StringBuilder()
            .AppendLine("You are a senior technical content writer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine(briefBody)
            .AppendLine($"Write ONLY the \"{heading}\" FAQ section of a {pageKind}.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine($"This section's tag is \"h2\" and heading is exactly \"{heading}\". Each question is a child Section: tag \"h3\", heading is the question verbatim, paragraphs holds a 2-4 sentence answer.")
            .AppendLine("Direct, factual answers. Third person.")
            .AppendLine($"Answers must sound like {context.PublisherName} ({context.ImplementerPositioning}), not a generic textbook FAQ — reflect the same consultative brand voice as the rest of the article, not interchangeable boilerplate.")
            .ToString();

        if (isRegeneration)
        {
            system += Environment.NewLine + "REGENERATION: use fresh phrasing.";
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, sectionHeading: heading);
        if (revisionBlock is not null)
        {
            system += Environment.NewLine + revisionBlock;
        }

        var user = new StringBuilder()
            .AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ArticleFaq,
                $"Write the {heading} FAQ section."))
            .AppendLine()
            .AppendLine($"Article title: {title}")
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

    public ChatCompletionRequest BuildToolFaqFromQuestionsPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<string> questions,
        string evidenceBlock)
    {
        var questionBlock = string.Join("\n", questions.Select((q, i) => $"  - Q{i + 1}: {q}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Write ONLY the FAQ section of the tool overview page for {app.Name}, answering the operator's questions.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\" and heading is exactly \"Frequently Asked Questions\". Each answered " +
                "question is a child Section: tag \"h3\", heading is the question verbatim, paragraphs holds a 2-4 sentence answer.")
            .AppendLine($"Answer ONLY from the PARTNER EVIDENCE in the user message -- {app.Name}'s own pages. If no passage " +
                "answers a question, LEAVE THAT QUESTION OUT: no child for it and no placeholder. Never answer from general " +
                "knowledge, and never state a capability, price or figure the evidence does not state.")
            .AppendLine($"Answers sound like {context.PublisherName} ({context.ImplementerPositioning}): third person, direct, factual.")
            .AppendLine(CurrencyInstruction)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Page topic: {pillarMetadata.Title}")
            .AppendLine("Questions to answer, only where the evidence answers them:")
            .AppendLine(questionBlock)
            .AppendLine()
            .AppendLine("=== PARTNER EVIDENCE (the partner's own pages; the only source for these answers) ===")
            .AppendLine(evidenceBlock.Length > 0 ? evidenceBlock : "(nothing was retrieved -- answer no question)")
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.3,
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
            .AppendLine(SeoOutlineInstruction(context.TargetKeyword))
            .AppendLine(NoToolsSectionOutlineInstruction)
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
            .AppendLine(SeoLedeInstruction(context.TargetKeyword))
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            .AppendLine("Prefer a creative (hook/narrative) opening; use a summary (direct thesis-first) opening only if a creative angle genuinely doesn't fit this topic.")
            .AppendLine("The opening is the hook, then the turn that names what is at stake, then who this is for.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(LedeAskInstruction)
            .AppendLine(HumanRegisterInstruction)
            .AppendLine(CurrencyInstruction)
            .AppendLine(BuildPublisherSiteBlock(context))
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary:")
            .AppendLine(LedeHeadingInstruction)
            .AppendLine(HeadingCraftInstruction)
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
            .AppendLine(
                $"Aim for {ContentLengthTargets.BlogRangeLabel} words. The scored floor stated below is "
                + "lower than that aim, and a piece that only clears the floor is a thin one.")
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
            MaxOutputTokens: LongFormBodyMaxOutputTokens));
    }

    public ChatCompletionRequest BuildStandaloneBlogMetadataPrompt(ProjectGenerationContext context)
    {
        var system = new StringBuilder()
            .AppendLine("You are a content marketer for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences, no commentary.")
            .AppendLine(BlogMetadataJsonContract)
            .AppendLine("This is a standalone deep-dive blog — there is no companion pillar article. Title should be a conversational hook, question, or numbered angle.")
            .AppendLine(SeoOutlineInstruction(context.TargetKeyword))
            // The ban lived only in the pillar's outline prompt, so nothing ever told a blog not to
            // write one -- which is why "Choosing the Right AI Tools for ..." turned up on them. It
            // then carried a weaker paraphrase than the body prompt's, which is how "Choosing the
            // Right Tools for Your Accounts Payable Needs" was planned and then refused.
            .AppendLine(NoToolsSectionOutlineInstruction)
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

    public ChatCompletionRequest BuildStandaloneBlogLedePrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? evidenceBlock = null)
    {
        var system = SystemPrompt(LedeJsonContract);

        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine("Write the opening lede for a schema.org BlogPosting deep-dive — conversational but substantive; first/second person allowed.")
            .AppendLine(SeoLedeInstruction(context.TargetKeyword))
            // The same lede-type guidance the pillar's opening gets: twelve types, chosen against the brief.
            .AppendLine(BuildLedeTypeGuidance(context))
            .AppendLine("The opening is the hook, then the turn that names what is at stake, then who this is for.")
            .AppendLine(LedeLengthInstruction)
            .AppendLine(LedeAskInstruction)
            .AppendLine(LedeHeadingInstruction)
            .AppendLine()
            .AppendLine(BuildPublisherSiteBlock(context));

        // The opening is where the page's factual claims are set, so it is written against the same
        // retrieved evidence the body gets -- see LedeEvidenceInstruction. Only the research half is
        // passed in: the competitor-heading block refers to provenance rules the lede contract does
        // not carry.
        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            user.AppendLine(LedeEvidenceInstruction).AppendLine(evidenceBlock);
        }

        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine(AnswerInTheContract);

        return new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.7,
            MaxOutputTokens: 2048);
    }

    /// <summary>
    /// Obligation slots as the body prompts present them: what the section must cover, its proportion, and
    /// any guidance -- the writer supplies the heading.
    /// </summary>
    /// <remarks>
    /// The shape the tool body (<c>:2450-2465</c>) and pillar body (<c>:1663-1670</c>) already use, pulled
    /// out when the blog joined them rather than written a third time.
    /// </remarks>
    private static string RenderSlots(IReadOnlyList<SectionSlot> slots)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            sb.AppendLine(slot.WritesItsOwnHeading
                ? $"{i + 1}. Cover: {slot.Covers}"
                : $"{i + 1}. \"{slot.Heading}\"");
            if (slot.Depth is { Length: > 0 })
                sb.AppendLine($"   Roughly {slot.Depth} -- for proportion between sections, not a quota.");
            if (slot.Guidance is { Length: > 0 })
                sb.AppendLine($"   {slot.Guidance}");
        }

        return sb.ToString().TrimEnd();
    }

    public ChatCompletionRequest BuildStandaloneBlogBodyPrompt(
        ProjectGenerationContext context, BlogMetadataDraft metadata, string? revisionNotes = null,
        bool requireHeadingProvenance = false, string? evidenceBlock = null, Section? lede = null,
        IReadOnlyList<SectionSlot>? sectionBatch = null, int batchIndex = 0,
        IReadOnlyList<SectionSlot>? fullOutline = null)
    {
        // The outline is `BlogPrompts.OutlineFor`'s obligations, not headings off the metadata. It read
        // metadata.SectionOutline until 2026-10-02, which is what let the model decide what the sections
        // WERE -- and so let "Best Tools for X" be a valid answer that nothing upstream could prevent.
        //
        // `sectionBatch` is the slice this call owns; the whole plan still goes to the model as context, so
        // a batch neither re-covers what another owns nor closes a page it cannot see continuing.
        var blogOutline = fullOutline ?? sectionBatch ?? [];
        var blogBatch = sectionBatch is { Count: > 0 } ? sectionBatch : blogOutline;
        var isBlogBatch = blogBatch.Count != blogOutline.Count;

        var system = SystemPrompt(requireHeadingProvenance ? SectionsArrayJsonContractWithProvenance : SectionsArrayJsonContract);

        // What a blog post is. The same text on every call of the type.
        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine("A standalone deep-dive blog post written from the research brief and keyword — there is no pillar article to repurpose.")
            .AppendLine("Substantive paragraphs with examples and implementation context; first/second person allowed.")
            .AppendLine(
                $"Aim for {ContentLengthTargets.BlogRangeLabel} words. The scored floor stated below is "
                + "lower than that aim, and a piece that only clears the floor is a thin one.")
            // A whole-document target is a number the model cannot act on while writing section
            // three of six. Pillar has carried a per-section range all along and lands in its band;
            // blog carried only the total and came back at 791 words against 1,800-2,500 (Jeff,
            // 2026-09-23). Both constants already existed and nothing on this path used them.
            .AppendLine($"Each section runs {ContentLengthTargets.BlogSectionMinWords}-{ContentLengthTargets.BlogSectionTargetMaxWords} words. " +
                $"That is what {ContentLengthTargets.BlogSectionCountMin}-{ContentLengthTargets.BlogSectionCountTarget} sections of real depth adds up to -- " +
                "a section coming in at half of it has not finished making its point, it has not been written concisely.")
            .AppendLine(NoToolsSectionInstruction)
            .AppendLine(ToolsAsSolutionInstruction)
            .AppendLine();

        user.AppendLine(BuildBriefBodyGuidance(context));
        user.AppendLine(BuildPublisherSiteBlock(context));
        user.AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.BlogSection,
            "Write the blog body sections from this research. Ground claims in the brief; do not invent statistics."));
        user.AppendLine();

        if (requireHeadingProvenance)
        {
            if (!string.IsNullOrWhiteSpace(evidenceBlock))
            {
                user.AppendLine(evidenceBlock);
            }

            user.AppendLine(HeadingProvenanceInstruction +
                " A section whose heading matches one of the advisory H2s below may tag its own" +
                " provenance \"plan\". Any heading you refine, replace, or add beyond those — at any" +
                " level, including nested children — needs a real tag from the rules above.");
        }

        var blogContinuity = BuildLedeContinuityBlock(lede);
        if (blogContinuity is not null)
        {
            user.AppendLine(blogContinuity);
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes);
        if (revisionBlock is not null)
        {
            user.AppendLine(revisionBlock);
        }

        // What this call writes. The part that differs from one batch to the next, last.
        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            .AppendLine(isBlogBatch
                ? $"Write {blogBatch.Count} of this post's sections in this response. The word aim above is the "
                  + "whole post's, across every call; yours is the per-section range."
                : string.Empty)
            .AppendLine(SeoBodyInstruction(
                context.TargetKeyword, GccLongFormTypes.Blog,
                blogBatch.Count, blogOutline.Count,
                SectionSlot.BatchOwnsKeywordHeading(blogBatch, blogOutline, batchIndex)))
            .AppendLine($"Target keyword: {context.TargetKeyword}")
            .AppendLine($"Blog title: {metadata.Title}")
            .AppendLine($"Blog meta description: {metadata.MetaDescription}")
            .AppendLine()
            // Obligations, and the writer names each heading. Not "advisory H2s to refine" -- that wording
            // described a planned heading the model had itself invented, and there are none now.
            .AppendLine(isBlogBatch
                ? $"Write ONLY these {blogBatch.Count} top-level (h2) sections, in this order. Each entry says "
                  + "what that section is responsible for; you write its heading:"
                : $"Write {blogBatch.Count} top-level (h2) sections, in this order. Each entry says what that "
                  + "section is responsible for; you write its heading:")
            .AppendLine(RenderSlots(blogBatch))
            .AppendLine()
            .AppendLine(isBlogBatch
                ? "THE REST OF THIS POST, written by other calls -- do not cover these, do not recap "
                  + "them, and do not write a conclusion for the post unless its closing section is "
                  + "listed above as yours:" + Environment.NewLine
                  + RenderOutline(blogOutline) + Environment.NewLine
                : string.Empty)
            .AppendLine("Write the blog body sections. Name platforms from the research brief in running prose where they fit.")
            .AppendLine(BatchClosingInstruction(
                context,
                [.. blogBatch.Select(sl => sl.Label)],
                [.. blogOutline.Select(sl => sl.Label)]))
            .AppendLine(AnswerInTheContract);

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: new List<ChatMessage> { new(ChatRole.System, system), new(ChatRole.User, user.ToString()) },
            Temperature: 0.7,
            MaxOutputTokens: LongFormBodyMaxOutputTokens));
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

    /// <summary>
    /// The case-study rule for a tool body, stated against what the extraction holds.
    /// </summary>
    /// <remarks>
    /// The extraction is serialized with web defaults, so its case studies are the <c>caseStudies</c>
    /// array. Absent, empty or unreadable all mean the same thing to the writer -- there are none it may
    /// report -- so each produces the rule that says so.
    /// </remarks>
    internal static string ToolCaseStudyRule(string? extractedToolResearchJson)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(extractedToolResearchJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(extractedToolResearchJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("caseStudies", out var studies)
                    && studies.ValueKind == JsonValueKind.Array)
                {
                    count = studies.GetArrayLength();
                }
            }
            catch (JsonException)
            {
                count = 0;
            }
        }

        return count == 0
            ? "CRITICAL: there is no case-study data available, so there are no case studies to report. Not named ones, and not anonymous ones. "
            : $"CRITICAL: the only case studies you may report are the {count} in PARTNER DATA (caseStudies), each under its named client and "
              + "with only the outcome and metric that entry states. No others -- not named ones, and not anonymous ones. ";
    }

    public ChatCompletionRequest BuildToolBodyPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        string toolSlug,
        IReadOnlyList<SectionSlot> outline,
        string? revisionNotes = null,
        string? extractedToolResearchJson = null,
        Section? lede = null,
        IReadOnlyList<SectionSlot>? fullOutline = null,
        int batchIndex = 0,
        string? evidenceBlock = null,
        IReadOnlyList<GccQuoteCandidate>? quoteCandidates = null,
        IReadOnlyList<Section>? writtenSoFar = null)
    {
        // One rendering of the outline, from the one definition (ToolPrompts.Outline). It used to be
        // three hand-written prose lists inside this prompt beside a fourth copy in ToolPrompts and a
        // fifth in GccGenerateService.
        var sectionBlock = new StringBuilder();
        for (var i = 0; i < outline.Count; i++)
        {
            var slot = outline[i];
            sectionBlock.AppendLine(slot.WritesItsOwnHeading
                ? $"{i + 1}. Cover: {slot.Covers}"
                : $"{i + 1}. \"{slot.Heading}\"");
            if (slot.Depth is { Length: > 0 })
            {
                // A slot that carries its share of the page's floor is asked for its range and held to that
                // share, stated once for the call below. One that carries none is held to its lower figure.
                sectionBlock.AppendLine(slot.OwedWords is null
                    ? $"   {slot.Depth}. The lower figure is owed; the range sizes this section against the others."
                    : $"   {slot.Depth}. The range sizes this section against the others.");
            }
            if (slot.Guidance is { Length: > 0 })
            {
                sectionBlock.AppendLine($"   {slot.Guidance}");
            }
        }

        var system = SystemPrompt(SectionsArrayJsonContract);

        // What a tool page is, and who it is about. Same text on every call of this page.
        var user = new StringBuilder()
            .AppendLine("=== THIS PAGE ===")
            .AppendLine($"Editorial standard: {ContentLengthTargets.ToolEditorialDefinition}")
            .AppendLine("A tool overview page published with schema.org SoftwareApplication metadata — expert technical tone, not breaking news.")
            // What this page IS. Kept deliberately consistent with GccPartnerExtractionService's
            // wording, since both run over the same material.
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
            .AppendLine($"Only describe real, verifiable capabilities of {app.Name} — never invent a feature, integration, or claim to fill space.")
            .AppendLine("When persisted tool research is provided, treat it as the authoritative source — do not re-extract or contradict it.")
            .AppendLine($"Frame the implementation material as {context.PublisherName} ({context.ImplementerPositioning}) closing the gap for a client — consultative, not a sales pitch.")
            // What the extraction actually holds, not a fixed sentence.
            .AppendLine(ToolCaseStudyRule(extractedToolResearchJson) +
                "\"A mid-sized retail company reduced invoice processing time by 75%\" and \"a tech startup saw a 90% reduction in errors\" are " +
                "fabrications whether or not a company is named -- dropping the name does not make an invented outcome reportable, it only makes it " +
                "unfalsifiable. Never write \"many businesses have\", \"one company saw\", \"for instance, a firm in this sector\", or any figure " +
                "attached to an unnamed customer. A number may appear only if it is in the supplied evidence or published by this publisher. " +
                "A quantified outcome is fine for narrative punch only if explicitly labeled hypothetical/illustrative — avoid recycling a stock 40% line.")
            .AppendLine($"Tie the opening and closing sections to this project's use-case ({context.TargetKeyword}). Name sibling platforms from the research brief only when a real contrast helps — this page is about {app.Name}, not a roundup.");

        // Who the page is for, and what it has to do for them (Jeff's partner-page template,
        // 2026-09-23). What stays here is what is Tool's alone: what this reader wants to know about
        // this product. The audience itself is rendered once, by BuildBriefBodyGuidance.
        user.AppendLine($"They are weighing {app.Name} and want three questions answered: is it right for a business my size, "
            + $"what does it fix for my team specifically, and why hire {context.PublisherName} to set it up instead of doing it myself.");
        user.AppendLine("Translate capability into consequence. Every feature you state must land with what it means for "
            + "that reader — hours returned, errors removed, a job that stops needing a person. A capability listed without "
            + "its consequence is a spec sheet, and they can already read the vendor's own.");
        user.AppendLine("Lead with outcomes, not mechanism. Plain language over jargon, concrete over abstract.");
        user.AppendLine($"The implementation section is where you answer the DIY question: what {context.PublisherName} "
            + $"({context.ImplementerPositioning}) does that makes {app.Name} work in their environment — configuration, data "
            + "mapping, integration with what they already run, training. Earn the claim, never assert it.");
        user.AppendLine();

        // The operator's controls over how the evidence is written up, and what the publisher says of itself.
        user.AppendLine(BuildBriefBodyGuidance(context));
        user.AppendLine(BuildPublisherSiteBlock(context));

        user.AppendLine(ResearchBriefBuilder.Build(context, ResearchBriefPhase.ToolBody, $"Write the tool overview page for {app.Name}."))
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
            // proof -- into our words (Jeff, 2026-09-23: "Tools should be paraphrasing Partner data").
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

        // Evidence the call carries beyond the partner's own pages: the competitor and own-site
        // blocks and the foreign amounts to leave out.
        if (!string.IsNullOrWhiteSpace(evidenceBlock))
        {
            user.AppendLine(evidenceBlock);
        }

        var toolContinuity = BuildLedeContinuityBlock(lede);
        if (toolContinuity is not null)
        {
            user.AppendLine(toolContinuity);
        }

        var toolWritten = BuildWrittenSoFarBlock(writtenSoFar);
        if (toolWritten is not null)
        {
            user.AppendLine(toolWritten);
        }

        var revisionBlock = BuildRevisionNotesBlock(revisionNotes, toolSlug: toolSlug);
        if (revisionBlock is not null)
        {
            user.AppendLine(revisionBlock);
        }

        // What this call writes. The part that differs from one batch to the next, last.
        user.AppendLine()
            .AppendLine("=== ASSIGNMENT ===")
            // The page carries exactly one quotation, and a page written in batches is several calls:
            // the first batch carries it; the rest are told it is written elsewhere and not handed the
            // spans at all.
            .AppendLine(batchIndex == 0
                ? ToolQuotationInstruction(app.Name, context.TargetKeyword)
                : ToolQuotationWrittenElsewhere(app.Name))
            .AppendLine(batchIndex == 0 && quoteCandidates is { Count: > 0 }
                ? QuotableSpansBlock(quoteCandidates)
                : string.Empty)
            .AppendLine("No introductory paragraphs before the first section.")
            .AppendLine($"Write {outline.Count} top-level (h2) sections, in this order. Each entry says what that " +
                "section is responsible for; you write its heading:")
            .AppendLine(sectionBlock.ToString().TrimEnd())
            // When this call writes part of the page, the rest of the plan is context: it stops a
            // batch re-covering what another owns, and stops it closing the page it cannot see
            // continues.
            .AppendLine(fullOutline is { Count: > 0 } && fullOutline.Count != outline.Count
                ? "THE REST OF THIS PAGE, written by other calls -- do not cover these, do not recap "
                  + "them, and do not write a conclusion for the page:" + Environment.NewLine
                  + RenderOutline(fullOutline)
                : string.Empty)
            .AppendLine(SeoBodyInstruction(
                context.TargetKeyword, GccLongFormTypes.Tool,
                outline.Count, Math.Max(outline.Count, fullOutline?.Count ?? outline.Count),
                SectionSlot.BatchOwnsKeywordHeading(outline, fullOutline, batchIndex)))
            // The batch's own floor, not the page's. This used to print ContentLengthTargets.
            // ToolTargetMinWords-ToolTargetMaxWords -- the whole page's 3,500-5,000 words -- under
            // "the sections above", so a 2-of-6 batch was told it owed the full page's word count
            // for two sections: four-plus times its real share, flatly contradicting
            // SeoBodyInstruction's own "~1,200 words" two lines above (confirmed live in Chaserhq's
            // and Bill's tool-page batches, 2026-10-09: every batch came back 40-45% under its
            // actual, lower floor). GccGenerateService.BatchFloorWords is the same sum
            // BatchShortfalls uses to grade the draft afterward, so the number stated here and the
            // number enforced later cannot drift apart again.
            // Said only when the sections carry a floor. A revision assigns sections by their headings, with
            // no size of their own, and "at least 0 words" would be the one number in the prompt that is wrong.
            .AppendLine(GccGenerateService.BatchFloorWords(outline) is var owedHere and > 0
                ? $"Length: at least {owedHere:N0} words across the {outline.Count} " +
                    (outline.Any(slot => slot.OwedWords is not null)
                        ? "sections above -- this call's share of the page's floor. The range beside each section is what to aim for."
                        : "sections above -- the sum of each section's own lower figure, which is owed.")
                : string.Empty)
            .AppendLine("Depth, never padding: do not restate a point in new words, do not invent a feature, figure or integration to fill a section. " +
                $"When the evidence for a section is thin, go further into what it does support -- the mechanism, what it changes for this reader's week, what deploying it involves with {context.PublisherName} -- rather than closing the section short.")
            .AppendLine($"Equal to a Pillar page in ambition, not a thinner treatment -- {(fullOutline ?? outline).Count} substantial sections across the page, not four.")
            .AppendLine($"This word target is for the {outline.Count} sections above only -- a separate FAQ section, when the tool has " +
                "partner FAQ data, is generated afterward and is additional, not part of this budget.")
            .AppendLine(BatchClosingInstruction(
                context, [.. outline.Select(sl => sl.Label)], [.. (fullOutline ?? outline).Select(sl => sl.Label)]))
            // Only the call that writes the closing is told where the ask goes; every other batch has just
            // been told it does not close the page.
            .AppendLine(OwnsTheClosing([.. outline.Select(sl => sl.Label)], [.. (fullOutline ?? outline).Select(sl => sl.Label)])
                ? "Place it after the reader has reason to act — never a banner, never repeated per section."
                : string.Empty)
            .AppendLine($"Write expert third-person technical prose focused on {app.Name}, grounded in this use-case.")
            .AppendLine(AnswerInTheContract);

        return WithSectionsArraySchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user.ToString())],
            Temperature: 0.5,
            // 16384 to match BuildArticleSectionBatchPrompt (Pillar's own body-batch call) now that
            // Tool targets the same 3,000-5,000 word range across its JSON-structured sections.
            MaxOutputTokens: LongFormBodyMaxOutputTokens));
    }

    /// <summary>
    /// The tool page's FAQ section, built from the partner answers the extraction read off the
    /// partner's pages. Those answers are model-extracted and not checked against the page text, so
    /// the prompt does not call them verified.
    /// Interface contract: <see cref="IContentPromptBuilder.BuildToolFaqSectionPrompt"/>.
    /// </summary>
    /// <remarks>
    /// <b>Writing, not Utility</b> — and it was Utility until 2026-10-03, with the comment "structured,
    /// short, no prose a reader reads". That last clause was simply wrong: this section ships on the tool
    /// page under its own headings. Classed Utility it resolved to <c>UtilityModel</c>, so every tool
    /// page's FAQ was written by the cheap model while the rest of the same page used the writing one —
    /// a quality seam nobody chose, invisible in the output because both halves render identically.
    /// Image prompts are genuinely utility work: nobody reads them. An answer under an H3 is the page.
    /// </remarks>
    public ChatCompletionRequest BuildToolFaqSectionPrompt(
        ProjectGenerationContext context,
        ArticleMetadataDraft pillarMetadata,
        SchemaBuilders.SoftwareApplicationDescriptor app,
        IReadOnlyList<GccPartnerFaqAsset> faqBank)
    {
        var faqBlock = string.Join(
            "\n\n",
            faqBank.Select((f, i) =>
                $"  Q{i + 1}: {f.Question}\n  Answer: {f.VerifiedAnswer}\n  Source: {f.OriginProofUrl}"));

        var system = new StringBuilder()
            .AppendLine("You are a senior technical writer for an IT consulting firm.")
            .AppendLine(BrandTones.ForWebpages())
            .AppendLine($"Write ONLY the FAQ section of the tool overview page for {app.Name}.")
            .AppendLine("Respond with ONLY a single valid JSON Section object — no code fences, no commentary.")
            .AppendLine(SectionJsonContract)
            .AppendLine("This section's tag is \"h2\" and heading is exactly \"Frequently Asked Questions\". Each " +
                "question is a child Section: tag \"h3\", heading is the question (verbatim or lightly tightened for " +
                "clarity), paragraphs holds the answer.")
            .AppendLine("Every answer below was taken from the partner's own site -- paraphrase and " +
                $"tighten it into {context.PublisherName}'s ({context.ImplementerPositioning}) voice, but never change " +
                "its factual content, add a claim not in the answer given, or drop the substance to shorten it.")
            .AppendLine("Use every question provided, in the order given, none invented and none skipped.")
            .AppendLine(CurrencyInstruction)
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Tool name: {app.Name}")
            .AppendLine($"Pillar topic: {pillarMetadata.Title}")
            .AppendLine("=== PARTNER FAQ (from the partner's own pages — paraphrase, do not re-derive) ===")
            .AppendLine(faqBlock)
            .ToString();

        return WithSectionSchema(new ChatCompletionRequest(
            Messages: [new(ChatRole.System, system), new(ChatRole.User, user)],
            Temperature: 0.3,
            MaxOutputTokens: 4096));
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
