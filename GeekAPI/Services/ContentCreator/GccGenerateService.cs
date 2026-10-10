using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.PromptBuilders;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.ContentCreator.Guardrail;
using GeekAPI.Services.Gcw;
using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentCreator;
using Microsoft.Extensions.Options;

using GeekAPI.Services.ContentCreator;

using GeekAPI.HttpClients;
namespace GeekAPI.Services.ContentCreator;


public sealed record SiteAnalysisDto(Guid Id, string Domain, string Status);

/// <summary>
/// Content Creator generation helpers. Source of truth = Content Writer v2 only
/// (prompt builders + LLM providers). Do not call Content Writer v3 generators.
/// </summary>
// Partial: GeneratePillarBodyAsync lives in Writers/GccGenerateService.Pillar.cs
// (plans/single-source-of-responsibility.md, section 6 step 2, 2026-10-09 -- a pure,
// behaviour-preserving physical move, zero change to any caller).
public partial class GccGenerateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>The document wire format (Paragraph discriminator uses "type"): one definition, <see cref="GccDocumentJson"/>.</summary>
    private static readonly JsonSerializerOptions CwDocumentJson = GccDocumentJson.Options;

    private readonly IContentPromptBuilder _prompts;
    private readonly GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePromptRegistry _types;
    private readonly IContentProviderFactory _cwProviders;
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly IBlogPostingSchemaBuilder _blogSchema;
    private readonly IArticleSchemaBuilder _articleSchema;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccGenerateService> _logger;
    private readonly IGccPartnerExtractionBank _extractionBank;
    private readonly GccCompetitorAnalysisResolver _competitorAnalysis;
    private readonly GeekAPI.Services.ContentCreator.Partner.GccPartnerExtractionService _partnerExtraction;
    private readonly IGccProjectReader _projects;
    private readonly GccPublisherProfileResolver _publisherProfile;
    private readonly GccKnownToolsResolver _knownTools;

    /// <summary>
    /// The partner URLs the operator entered on this create's project -- the authority on which
    /// products a page must name. Empty when the create belongs to no project, which is a create
    /// with no partners rather than an error.
    /// </summary>
    private async Task<IReadOnlyList<string>> PartnerUrlsForAsync(GccCreateDto create, CancellationToken ct)
    {
        if (create.ProjectId is not { } projectId) return [];
        var project = await _projects.GetProjectAsync(projectId, ct);
        return project?.PartnerUrls ?? [];
    }

    public GccGenerateService(
        IContentPromptBuilder prompts,
        GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePromptRegistry types,
        IContentProviderFactory cwProviders,
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IBlogPostingSchemaBuilder blogSchema,
        IArticleSchemaBuilder articleSchema,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccGenerateService> logger,
        GccCompetitorAnalysisResolver competitorAnalysis,
        GeekAPI.Services.ContentCreator.Partner.GccPartnerExtractionService partnerExtraction,
        IGccProjectReader projects,
        GccPublisherProfileResolver publisherProfile,
        GccKnownToolsResolver knownTools,
        IGccPartnerExtractionBank extractionBank)
    {
        _extractionBank = extractionBank;
        _prompts = prompts;
        _types = types;
        _cwProviders = cwProviders;
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _blogSchema = blogSchema;
        _articleSchema = articleSchema;
        _company = company.Value;
        _logger = logger;
        _competitorAnalysis = competitorAnalysis;
        _partnerExtraction = partnerExtraction;
        _projects = projects;
        _publisherProfile = publisherProfile;
        _knownTools = knownTools;
    }

    public static SiteSectionContextDto? ParseSiteSection(string? json) =>
        GccSiteSection.ParseSiteSection(json);

    /// <summary>
    /// Required gate: every Generate must have a crawl id (site_analysis_profiles.Id).
    /// Domain-only grounding (crawl id with no section) is allowed — Generate uses
    /// page-section trees for "must mention" injection. Handoff-created sections must have
    /// non-empty relatedPages. Applies to all types including imagePrompt/aiTool (no exemption);
    /// per-H2 image prompts must include siteSection+tree with at least one top-level section.
    /// </summary>
    public static void ValidateSiteSectionGate(Guid? projectSiteRunId, SiteSectionContextDto? section) =>
        GccSiteSection.ValidateSiteSectionGate(projectSiteRunId, section);

    /// <summary>
    /// Per-H2 image-prompt gate: requires a primary long-form with at least one top-level section.
    /// Call after ValidateSiteSectionGate when imagePrompt is among OutputTypes.
    /// </summary>
    public static void ValidateImagePromptRequiresLongForm(string startingContentType, bool hasLongForm, int h2Count)
    {
        if (!hasLongForm)
            throw new InvalidOperationException(
                $"{ToDisplayType(startingContentType)} must include at least one top-level section before generating image prompts");

        if (h2Count == 0)
            throw new InvalidOperationException(
                $"{ToDisplayType(startingContentType)} must include at least one top-level section before generating image prompts");
    }

    private static string ToDisplayType(string raw)
    {
        var t = raw?.Trim().ToLowerInvariant();
        return t switch
        {
            "pillar" => "Pillar",
            "blog" => "Blog",
            "techarticle" => "TechArticle",
            _ => "Primary long-form"
        };
    }

    /// <summary>
    /// Generate reads persisted BriefJson from the create only — not client request bodies.
    /// </summary>
    /// <remarks>
    /// <c>lengthBand</c> is not required. The brief carries no length band: length is each output type's
    /// own (decision J6). It was required here, and for a day it was waived only at the project route --
    /// while the tool page path calls this again, so every tool page of the first project run was refused
    /// "brief required: missing lengthBand" (2026-10-05). One rule, in the one place it is checked.
    /// </remarks>
    public static void ValidateBriefRequired(GccCreateDto create)
    {
        if (string.IsNullOrWhiteSpace(create.BriefJson))
            throw new InvalidOperationException("brief required");

        using var doc = JsonDocument.Parse(create.BriefJson);
        var root = doc.RootElement;
        static string? S(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        // Compat-first: accept the new Google-aligned field names OR the legacy
        // names during the migration window. Prefer the first non-empty value.
        static string? Any(JsonElement el, params string[] names)
        {
            foreach (var n in names)
            {
                var v = S(el, n);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }
        static bool HasArrayItem(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array && p.GetArrayLength() > 0;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Any(root, "primaryIntent", "intent"))) missing.Add("primaryIntent");
        if (string.IsNullOrWhiteSpace(S(root, "buyingStage"))) missing.Add("buyingStage");
        if (string.IsNullOrWhiteSpace(Any(root, "audienceSegment", "audiencePrimary"))) missing.Add("audienceSegment");
        if (string.IsNullOrWhiteSpace(Any(root, "audienceNotes", "audienceDetail"))) missing.Add("audienceNotes");
        if (string.IsNullOrWhiteSpace(S(root, "angle"))) missing.Add("angle");
        // toneOfVoice/eeatSignals are new; only enforce when the brief has already
        // been migrated (legacy briefs carry a numeric toneOfVoice object, no eeatSignals).
        var isNewBrief = S(root, "toneOfVoice") is not null
            || root.TryGetProperty("eeatSignals", out _)
            || root.TryGetProperty("primaryIntent", out _);
        if (isNewBrief)
        {
            if (string.IsNullOrWhiteSpace(S(root, "toneOfVoice"))) missing.Add("toneOfVoice");
            if (!HasArrayItem(root, "eeatSignals")) missing.Add("eeatSignals");
        }
        if (missing.Count > 0)
            throw new InvalidOperationException($"brief required: missing {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Content types disabled 2026-09-22 (Jeff) pending a written, approved resolve plan -- see
    /// plans/content-type-dispatch-and-richness.md (content-creator-v2). Revised same day: Pillar
    /// and Blog re-enabled -- they have real, independent dedicated generators and only break in one
    /// specific combination (multi-select alongside a sibling long-form type, tracked as its own bug
    /// fix, not a reason to disable a type that mostly works). Tool remains the one long-form type
    /// excluded outright -- it meets the bar these others don't (always independently generated via
    /// GenerateStartingContentAsync, never repurposed, never mis-routed).
    /// Everything still in this set has no real, correctly-routed implementation at all:
    /// TechArticle/Comparison/Alternatives/CaseStudy/Guide/Listicle/Service/Local/Whitepaper are
    /// always the generic fallback inside GenerateStartingContentAsync; LinkedInDocument has zero
    /// content-type-specific treatment; EmailNewsletter/EmailStoryNurture/EmailTransactional all
    /// currently produce cold-outreach-shaped content regardless of which is selected -- not a thin
    /// version of the right thing, the wrong thing. Email-cold-outreach, Social, Ads, and standalone
    /// Image prompt are not in this set -- cold outreach is the one email variant genuinely
    /// implemented, and Social/Ads/Image prompt were never flagged as broken.
    /// Normalized by stripping hyphens/spaces/slashes and lowercasing, so "tech-article"/"techArticle"
    /// and "PDF / LinkedIn document"/"linkedin-document" both match one entry each.
    /// </summary>
    private static readonly HashSet<string> DisabledContentTypes = new(StringComparer.Ordinal)
    {
        "techarticle", "comparison", "alternatives", "casestudy", "guide", "listicle",
        "service", "local", "whitepaper", "linkedindocument",
        "emailnewsletter", "emailstorynurture", "emailtransactional",
    };

    public static bool IsContentTypeDisabledPendingImplementation(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var normalized = new string(contentType.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return DisabledContentTypes.Contains(normalized);
    }

    /// <summary>
    /// The Brief as labeled prose, one line per populated field — never a raw JSON dump. Stage 3:
    /// dumping <c>create.BriefJson</c> verbatim meant every field arrived with equal, unweighted
    /// emphasis and no guidance on how to resolve a conflict between them; a model reads "follow
    /// this one when they disagree" only if that instruction exists in the same prose it's reading,
    /// not buried as a sibling JSON key.
    /// </summary>
    internal static string BuildBriefFieldsBlock(BriefFields brief)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BRIEF ===");
        if (!string.IsNullOrWhiteSpace(brief.Segment))
        {
            var line = $"Audience segment: {brief.Segment}";
            if (brief.Details is { Count: > 0 })
                line += $" ({string.Join(", ", brief.Details)})";
            sb.AppendLine(line);
        }
        if (!string.IsNullOrWhiteSpace(brief.Notes))
            sb.AppendLine($"Audience notes: {brief.Notes} — if this conflicts with the segment above, follow the notes.");
        if (!string.IsNullOrWhiteSpace(brief.Angle))
            sb.AppendLine($"Angle: {brief.Angle}");
        if (!string.IsNullOrWhiteSpace(brief.PrimaryIntent))
        {
            var line = $"Primary intent: {brief.PrimaryIntent}";
            if (!string.IsNullOrWhiteSpace(brief.SecondaryIntent))
                line += $" + {brief.SecondaryIntent}";
            sb.AppendLine(line);
        }
        if (!string.IsNullOrWhiteSpace(brief.BuyingStage))
            sb.AppendLine($"Buying stage: {brief.BuyingStage} — align examples/CTAs to funnel (awareness=educate, consideration=compare, action=convert).");
        if (!string.IsNullOrWhiteSpace(brief.ToneOfVoice))
            sb.AppendLine($"Tone of voice: {brief.ToneOfVoice} — hold this voice throughout.");
        if (brief.EeatSignals is { Count: > 0 })
            sb.AppendLine($"E-E-A-T signals to demonstrate: {string.Join(", ", brief.EeatSignals)}.");
        if (!string.IsNullOrWhiteSpace(brief.LengthBand))
            sb.AppendLine($"Length band: {brief.LengthBand} — respect target length.");
        return sb.ToString();
    }

    /// <summary>
    /// A tool page's title: the product, then the project's keyword ("Ramp: Automated Approval Workflows").
    /// The keyword is the topic without its department, <see cref="GccTopic.KeywordOf"/>, so another project's
    /// keyword gives another title.
    /// </summary>
    internal static string ToolPageTitle(string productName, string? topic)
    {
        var keyword = GccTopic.KeywordOf(topic).Trim();
        return keyword.Length == 0 ? productName : $"{productName}: {keyword}";
    }

    /// <summary>
    /// The brief's fields alone, with none of the retrieved research. For a caller that hands the research
    /// to the writer itself, once: <see cref="BuildBriefAndResearchBlock"/> as a tool page's source context
    /// was printed three times per call (the publisher block, "Tool summary:" and the evidence block), 54%
    /// of a tool call, and no check reads the two extra copies.
    /// </summary>
    public static string BuildBriefOnlyBlock(GccCreateDto create) =>
        BuildBriefFieldsBlock(ExtractBriefFields(create.BriefJson)).TrimEnd();

    public static string BuildBriefAndResearchBlock(GccCreateDto create)
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildBriefFieldsBlock(ExtractBriefFields(create.BriefJson)));
        var researchBlock = BuildResearchBlock(create);
        if (researchBlock.Length > 0)
            sb.AppendLine(researchBlock);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Everything <see cref="BuildBriefAndResearchBlock"/> renders beyond the Brief itself:
    /// quoteable research (retrieved from the crawl index), uploaded Keyword SERP files, and the
    /// SERP index. Split out so the pillar/blog live path -- which builds its own Brief-controls
    /// block separately via <c>BuildPillarContext</c>/<c>BuildBriefBodyGuidance</c> -- can pull in
    /// research without duplicating the Brief a second time. Stage 2: this was the retrieved
    /// evidence <c>GccGroundingResolver</c> resolves and merges into <c>ResearchJson</c> that
    /// pillar/blog never read back out.
    /// </summary>
    internal static string BuildResearchBlock(GccCreateDto create)
    {
        var sb = new StringBuilder();
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (research?.Quoteables is { Count: > 0 })
        {
            sb.AppendLine("=== QUOTEABLE RESEARCH (partner/tool evidence) ===");
            // Attribution is the requirement, not a nicety: drafts previously named partners and
            // tools with capabilities nobody could source. Every claim about a partner or tool must
            // trace to one of the passages below, and carry that passage's URL.
            //
            // The passages are no longer flat prose. A retrieved chunk arrives already carrying
            // where it sat on its page and what it linked to (HttpGeekCrawlerRagClient.RenderChunk),
            // and the model cannot use structure nobody described to it. A passage extracted from a
            // fetched partner page rather than retrieved from the index carries fewer of these
            // lines, which is why each is described as optional rather than promised.
            sb.AppendLine("How to read a passage. A passage may carry labelled lines above or around");
            sb.AppendLine("its text. Not every passage carries every label. The labels are:");
            sb.AppendLine("  <title> (<url>)            the page the passages beneath it come from. The URL is");
            sb.AppendLine("                              never written by you.");
            sb.AppendLine("  Section: <title>            the heading that passage sits under on its page.");
            sb.AppendLine("  Target Entity Match: <name> the partner tool that passage's own links point at.");
            sb.AppendLine("  Context: / Specific detail: the surrounding block, then the matched sentence.");
            sb.AppendLine("  Linked from this section:   the link text under that heading.");
            sb.AppendLine();
            sb.AppendLine("What they mean for what you write:");
            sb.AppendLine("- Section: is the feature or business category the passage belongs to. A claim");
            sb.AppendLine("  drawn from a passage belongs in the part of the piece that covers that");
            sb.AppendLine("  category. Do not carry a pricing passage into an integrations discussion");
            sb.AppendLine("  because the sentence happens to fit there.");
            sb.AppendLine("- Target Entity Match: <name> means that passage is evidence about that named");
            sb.AppendLine("  tool, established from the links in the passage itself. Treat the named tool");
            sb.AppendLine("  as the authoritative subject of that passage: its claims are that tool's");
            sb.AppendLine("  claims, and they are not evidence about any other product.");
            sb.AppendLine("- Context: is background for the sentence under Specific detail:. Quote the");
            sb.AppendLine("  detail; use the context to get it right, not as a second claim.");
            sb.AppendLine();
            sb.AppendLine("Rules for this block, and they are not optional:");
            sb.AppendLine("1. Any claim about a partner, tool or product must come from a passage below,");
            sb.AppendLine("   and from one whose Target Entity Match or Section places it with that");
            sb.AppendLine("   product. A passage labelled for one tool does not support a claim about");
            sb.AppendLine("   another, however similar the products are.");
            // The writer links nothing (Jeff, 2026-10-10). Every shape that let it -- a URL in the text,
            // an href on a run, an anchor list, a target id -- failed on a real run. Attribution is in
            // words; a partner tool's page is linked by GccToolLinker after the reply is read.
            sb.AppendLine("2. Attribute it in words: in the paragraph that carries the claim, say which product");
            sb.AppendLine("   or page it comes from. You write no URL anywhere: not in \"text\", not as an href,");
            sb.AppendLine("   not as [title](url). Never attribute a claim to a page whose passages do not state");
            sb.AppendLine("   it.");
            sb.AppendLine("3. Quote verbatim or paraphrase closely. Do not extrapolate a capability,");
            sb.AppendLine("   price, integration or limitation that no passage states.");
            sb.AppendLine("4. If the evidence does not cover something, omit it. Do not fill the gap.");
            sb.AppendLine("5. A tool named by a Target Entity Match line has evidence here by definition,");
            sb.AppendLine("   so it is named in the piece and its claims are cited from those passages.");
            sb.AppendLine("   There is no case where a labelled tool is left out for want of evidence:");
            sb.AppendLine("   the label is the evidence, and the passage under it is what to cite. This");
            sb.AppendLine("   does not license the reverse -- a tool with no labelled passage is still");
            sb.AppendLine("   governed by rules 1 and 4, and is named plainly with no claims attached");
            sb.AppendLine("   rather than given capabilities nothing here states.");
            sb.AppendLine();
            // Every quoteable is read (per-page heading/paragraph trimming below still bounds
            // prompt size).
            for (var i = 0; i < research.Quoteables.Count; i++)
            {
                var q = research.Quoteables[i];
                // No origin condition. There used to be one, testing RetrievalMode == "rag_chunk"
                // and labelling everything else "operator-supplied" -- but the only producer of an
                // operator-supplied quoteable was the Wiki/.edu/.gov upload path, removed
                // 2026-09-29 with the UI control that fed it. What the condition actually caught
                // was a null RetrievalMode, which is what GccPartnerUrlResearchService leaves on a
                // partner page it fetched and extracted. So real partner evidence was announced to
                // the model as an operator upload -- which the lines above define as plain prose
                // carrying none of the structure labels. Evidence was being discredited by a test
                // for a case that no longer exists.
                sb.AppendLine($"{q.Title} ({q.Url})");
                foreach (var h in q.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                    sb.AppendLine($"- H{h.Level}: {h.Text}");
                foreach (var p in q.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                    sb.AppendLine($"- {p}");
                sb.AppendLine();
            }
        }

        if (research?.SerpPages is { Count: > 0 } serpPages)
        {
            // One labeled block per uploaded Keyword SERP file: title→URL + related searches only.
            // No PAA (always discarded from these uploads) and no Shape.Guidance (advisory —
            // surfaced in the UI only).
            foreach (var page in serpPages)
            {
                sb.AppendLine($"=== KEYWORD SERP: {page.FileName} ===");
                foreach (var o in page.Organics)
                    sb.AppendLine($"- {o.Title} ({o.Url})");
                if (page.RelatedSearches.Count > 0)
                {
                    sb.AppendLine("Related searches:");
                    foreach (var r in page.RelatedSearches) sb.AppendLine($"- {r}");
                }
                sb.AppendLine();
            }
        }

        if (research?.SerpIndex is { } serp)
        {
            if (serp.OrganicTitles.Count > 0)
            {
                sb.AppendLine("SERP organic titles (index):");
                foreach (var t in serp.OrganicTitles.Take(12)) sb.AppendLine($"- {t}");
            }
            if (serp.PeopleAlsoAsk.Count > 0)
            {
                sb.AppendLine("People Also Ask (index):");
                foreach (var t in serp.PeopleAlsoAsk.Take(15)) sb.AppendLine($"- {t}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The line the page ends on, from the operator's brief and the company's wording. The questions are
    /// read here, at the page, and never reach the writer.
    /// </summary>
    private IReadOnlyList<Paragraph> ClosingFor(GccCreateDto? create) =>
        GccClosing.Paragraphs(
            _company, create is null ? [] : GccNicheFramingReader.DiagnosisQuestions(create.BriefJson));

    /// <summary>
    /// Part 4 — Consultant / four-phase methodology system-appendix, injected at the
    /// GeekAPI call site (NOT by editing the external content-writer-v2 prompt builder).
    /// Applied when toneOfVoice == consultant_professional, or the angle is the
    /// comprehensive ultimate-guide. Returns "" when it should not apply.
    /// </summary>
    /// <remarks>
    /// It carries no voice line, no instruction about how the page closes, and no sampling advice
    /// (Jeff, 2026-10-07). The voice is the one the system message sets, and a second one named here
    /// was the writer's other voice; the FAQ is appended by code, so "Close with an FAQ" told the
    /// writer to write what the page already gets; and the temperature is the call's, not the prose's.
    /// </remarks>
    public static string BuildConsultantAppendix(GccCreateDto create)
    {
        if (string.IsNullOrWhiteSpace(create.BriefJson)) return string.Empty;
        string? tone = null, angle = null;
        try
        {
            using var doc = JsonDocument.Parse(create.BriefJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("toneOfVoice", out var t) && t.ValueKind == JsonValueKind.String)
                tone = t.GetString();
            if (root.TryGetProperty("angle", out var a) && a.ValueKind == JsonValueKind.String)
                angle = a.GetString();
        }
        catch (JsonException)
        {
            return string.Empty;
        }

        var isConsultant = string.Equals(tone, "consultant_professional", StringComparison.OrdinalIgnoreCase);
        var isUltimateGuide = string.Equals(angle, "ultimate_guide", StringComparison.OrdinalIgnoreCase);
        if (!isConsultant && !isUltimateGuide) return string.Empty;

        return string.Join('\n', new[]
        {
            "=== ROLE & METHOD (consultant appendix) ===",
            "Write as a Senior IT Consultant advising local SMBs on AI implementation and business-process",
            "automation. Assume peer-level technical knowledge; high scannability.",
            "Weave these four phases into the narrative (do not label them mechanically):",
            "1. Business Objectives Alignment — the measurable goal / pain point (ROI, bottlenecks, cost of inaction).",
            "2. Data Quality Assessment — integrity, schema, storage (pooling, JSONB, validation).",
            "3. Tech Selection & Architecture — specific tools over generics (decoupled services, routing, benchmarks).",
            "4. Pilot Implementation Strategy — execution, smoke tests, validation (local integration, TDD, sandboxed rollout).",
            "Constraints: ban AI filler / clichés. Emit no markup of any kind — structure is carried",
            "by the section contract, not by characters in the text.",
        });
    }

    public sealed record HierarchyMatchDto(
        string[] Path,
        string[] ChildHeadings,
        string SourcePageUrl,
        string MatchedHeading,
        string Kind,
        IReadOnlyList<ToolsByHeading> ToolsByHeading);

    public sealed record CrawlTool(string Name, string? Href);

    public sealed record ToolsByHeading(string Heading, IReadOnlyList<CrawlTool> Tools);

    public sealed record ToolExtractDiag(
        string? MatchedHeading,
        string? PageUrl,
        int LinkCount,
        int HeadingCount,
        int DirectChildCount,
        int DeeperHeadingCount,
        IReadOnlyList<string> Headings);

    /// <summary>One partner's tool page, or the reason it could not be written.</summary>
    /// <param name="Ledger">What this page was made from -- its calls, discarded drafts, candidates,
    /// passages and pre-flight verdict -- for the version's evidence. Null for a page refused before
    /// drafting.</param>
    public sealed record ToolPageOutcome(
        string ProductName, string? BodyJson, string? Refusal)
    {
        public bool Written => BodyJson is not null;
    }

    /// <summary>
    /// Whether one partner can ground a tool page — decided <i>before</i> a word is drafted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is extraction and not a page count.</b> The gate is
    /// <see cref="HasSufficientPartnerData"/>, which measures what extraction <i>found</i>: a capability
    /// signal (features or citable claims) plus breadth across at least 3 of the payload categories. Page
    /// and paragraph volume do not predict it — a partner with 231 pages and 3,516 prose paragraphs
    /// fails if extraction pulled one feature and two integrations. So a cheap pre-flight over retrieval
    /// counts would have passed all five partners and then produced three pages, which is a green light
    /// that means nothing. The only thing that predicts this gate is running the gate.
    /// </para>
    /// <para>
    /// <b>It is not extra cost.</b> Extraction already ran per partner inside drafting; this hoists it
    /// ahead of drafting and carries the result forward in <see cref="Extraction"/>, so a partner that
    /// passes is extracted once, not twice.
    /// </para>
    /// <para>
    /// <b>A verdict for a thin partner, not a throw.</b> The same gate inside <see cref="GenerateToolPageAsync"/> refuses
    /// by throwing, which means its reason is only reachable from a catch. A pre-flight has to be able to
    /// report on five partners without any of them aborting the others, so this returns the finding; an
    /// exception from the assessment itself is turned into one by <see cref="AssessOrReportFaultAsync"/>.
    /// </para>
    /// </remarks>
    /// <param name="Coverage">
    /// The operator-facing reason, from <see cref="DescribePartnerDataCoverage"/> — which separates an
    /// extraction <i>fault</i> ("the provider call threw") from a genuine data shortage. Both leave the
    /// categories empty, so without that split a broken pipeline reads as a thin partner.
    /// </param>
    /// <param name="Extraction">
    /// The extraction to draft from, present only when <paramref name="Ready"/>. Never serialized: it is
    /// the full extraction payload and this record goes to the operator over the hub.
    /// </param>
    public sealed record GccPartnerToolReadiness(
        string ProductName,
        string Host,
        bool Ready,
        string Coverage,
        int PagesAttempted,
        int PagesFailed,
        int PopulatedCategories,
        bool HasCapabilitySignal,
        [property: JsonIgnore] GccPartnerExtractionDocument? Extraction = null,
        /// <summary>True when the extraction was read from the bank rather than paid for on this
        /// run. Said, never silent: a cache nobody can see is how a stale result becomes invisible.</summary>
        bool Reused = false,
        DateTime? BankedAtUtc = null)
    {
        /// <summary>
        /// How many categories <see cref="PopulatedCategories"/> is out of. Sent with the count so the
        /// page prints the number the gate uses: the workspace had "of 22" written into it, and went
        /// on saying 22 after freshness and disclosures left the schema and the count became twenty.
        /// </summary>
        public int TotalCategories { get; init; } = PartnerDataCategoryCount;
    }

    /// <summary>
    /// <see cref="AssessPartnerToolReadinessAsync"/> for one partner of several, where an exception from
    /// assessing it is that partner's finding and not the run's. The pre-flight promises to report on every
    /// partner "without any of them aborting the others"; extraction or the bank throwing for one of
    /// five used to abort all five. The finding says it could not be assessed and why, and the exception's
    /// type, stack and inner exceptions go to the log and to the run's record.
    /// </summary>
    private async Task<GccPartnerToolReadiness> AssessOrReportFaultAsync(
        GccPartnerToolSlice slice, ContentGeneratorProvider provider, CancellationToken ct, Guid createId)
    {
        try
        {
            return await AssessPartnerToolReadinessAsync(slice, provider, ct, createId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex, "Tool page pre-flight for {Product} ({Host}) failed with a fault", slice.ProductName, slice.Host);
            await GccRunLog.RecordIfAnyAsync(
                "fault", new { step = "tool pre-flight", partner = slice.ProductName, fault = GccRunFault.Describe(ex) });
            return new GccPartnerToolReadiness(
                slice.ProductName, slice.Host, Ready: false, Coverage: $"could not be assessed: {ex.Message}",
                PagesAttempted: 0, PagesFailed: 0, PopulatedCategories: 0, HasCapabilitySignal: false);
        }
    }

    /// <summary>
    /// Runs extraction and the sufficiency gate for one partner and reports the finding, drafting
    /// nothing. The pre-flight unit — <see cref="GenerateToolPagesPerPartnerAsync"/> calls it for every
    /// slice before it writes anything, and it is callable on its own to answer "which partners are
    /// ready" without starting a generate.
    /// </summary>
    public async Task<GccPartnerToolReadiness> AssessPartnerToolReadinessAsync(
        GccPartnerToolSlice slice,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        Guid? createId = null)
    {
        // No retrieved pages is a real finding, not a reason to skip the partner: it reports as
        // "no extractable partner pages" rather than disappearing from the readiness list.
        GccPartnerExtractionDocument? extraction = null;
        var reused = false;
        DateTime? bankedAt = null;
        if (slice.Pages.Count > 0)
        {
            // Read before paying. The extraction is ~21 concurrent model calls per partner and it
            // was recomputed on every generate -- on 2026-10-03 four or five times, twice by runs
            // that finished extracting and then died on a provider error with nothing kept. The
            // bank is keyed by a digest of the exact pages, so it is reused only while they are
            // what they were, and is shared by every create on the same partner.
            var digest = PartnerPagesDigest(slice.ProductName, slice.Pages);
            var banked = await _extractionBank.FindBankedAsync(slice.Host, digest, ct);
            if (banked is not null)
            {
                extraction = JsonSerializer.Deserialize<GccPartnerExtractionDocument>(banked.ExtractionJson, PartnerExtractionJsonOpts)
                    ?? throw new InvalidOperationException(
                        $"The banked extraction for {slice.Host} ({digest}) is unreadable. It is not re-extracted "
                        + "silently: a bank row that cannot be read is a defect to see, not a cache miss.");
                reused = true;
                bankedAt = banked.ExtractedAtUtc;
            }
            else
            {
                extraction = await _partnerExtraction.ExtractFromPagesAsync(slice.Pages, [slice.ProductName], ToLlm(provider), ct);

                // Successes only. A failed page is a fault -- a draining balance, a deprecated
                // parameter -- and banking it would make a billing incident a permanent property
                // of the partner. Banked per partner, not at the end, so a run that dies on the
                // fourth partner keeps the first three.
                if (extraction.PagesFailed == 0)
                {
                    await _extractionBank.BankAsync(
                        new BankGccPartnerExtractionCommand(
                            slice.Host,
                            digest,
                            createId,
                            slice.ProductName,
                            JsonSerializer.Serialize(extraction, PartnerExtractionJsonOpts),
                            extraction.PagesAttempted),
                        ct);
                }
            }
        }

        // A failed page is a fault, not a shortage, and partial extraction is failure (AGENTS.md).
        // This read HasSufficientPartnerData alone, so a partner with 5 of 7 pages failed on a
        // provider error and three categories filled from the other two was marked ready, drafted
        // from the surviving subset, and -- because the row read ready -- shown without the
        // coverage line that names the fault.
        var ready = extraction is not null && extraction.PagesFailed == 0 && HasSufficientPartnerData(extraction);

        var coverage = reused
            ? $"reused banked extraction from {bankedAt:yyyy-MM-dd HH:mm} UTC; {DescribePartnerDataCoverage(extraction)}"
            : DescribePartnerDataCoverage(extraction);

        return new GccPartnerToolReadiness(
            slice.ProductName,
            slice.Host,
            ready,
            coverage,
            extraction?.PagesAttempted ?? 0,
            extraction?.PagesFailed ?? 0,
            extraction is null ? 0 : CountPopulatedPartnerDataCategories(extraction),
            extraction is not null
                && (extraction.FeatureInventory.Count > 0 || extraction.Citables.Count > 0),
            ready ? extraction : null,
            Reused: reused,
            BankedAtUtc: bankedAt);
    }

    /// <summary>
    /// The bank key for a partner's pages: SHA-256 over the pages sorted by URL, each contributing
    /// its URL and paragraph text, with the product name and extractor version in front.
    /// </summary>
    /// <remarks>
    /// Not the crawl run id: a re-crawl refills the same run id in place (AGENTS.md), so a run-id
    /// stamp would match forever. Not the URL set: a re-crawl yields the same URLs with new text.
    /// The product name is in because extraction is asked for one product by name and a renamed
    /// partner is a different question; the extractor version is in so a schema change re-extracts.
    /// </remarks>
    internal static string PartnerPagesDigest(string productName, IReadOnlyList<GccQuoteablePage> pages)
    {
        var sb = new StringBuilder();
        sb.Append("extractor:").Append(GccPartnerExtractionDocument.CurrentExtractorVersion).Append('\n');
        sb.Append("product:").Append(productName.Trim()).Append('\n');
        foreach (var page in pages.OrderBy(p => p.Url, StringComparer.Ordinal))
        {
            sb.Append(page.Url).Append('\n');
            foreach (var paragraph in page.Paragraphs) sb.Append(paragraph).Append('\n');
            sb.Append('\0');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// A tool page per declared partner — the set-of-five path, and the priority (Jeff, 2026-10-02).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each page is <i>that product × this keyword</i>, grounded only in that partner's own evidence.
    /// Before this, one page was written with <c>create.Topic</c> as the product name over the pooled
    /// research, so extraction searched every partner's pages for a product named after the keyword and
    /// returned 1 of 22 payload categories twice, against partners carrying 84–226 quotable spans each.
    /// </para>
    /// <para>
    /// <b>Each page stands alone</b> (Jeff, 2026-10-02). A partner with too little evidence refuses its
    /// own page and that refusal is returned, not thrown — the others still ship, as do the other types of
    /// the run when one is refused (<see cref="GccRunSettlement"/>). The caller decides what to do when
    /// <i>every</i> partner refuses.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ToolPageOutcome>> GenerateToolPagesPerPartnerAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? mustMentionBlock = null,
        IReadOnlyList<GccGroundedPassage>? passages = null,
        // One product instead of all of them. Same slicing, same grounding, same gate -- Jeff,
        // 2026-10-02: five at once is the priority and single pages are also needed, so this is one
        // method with two call shapes rather than a second path that can drift from this one.
        string? onlyProduct = null,
        // Invoked once, with every partner's verdict, after the pre-flight and BEFORE any drafting.
        // The operator asked to be told before creation (Jeff, 2026-10-02), which is only possible
        // between the two phases -- afterwards is a report, not a pre-flight.
        Func<IReadOnlyList<GccPartnerToolReadiness>, Task>? onReadiness = null,
        // Some of the partners instead of all of them, named by their declared URLs. Jeff,
        // 2026-10-09: "Seeing as a single tool can fail, need a way to select just one tool." Null or
        // empty means every partner, as before.
        IReadOnlyList<string>? onlyPartners = null)
    {
        var partnerUrls = await PartnerUrlsForAsync(create, ct);

        IReadOnlyList<GccPartnerToolSlice> slices;
        if (!string.IsNullOrWhiteSpace(onlyProduct))
        {
            var wanted = GccPartnerToolSlices.ForProduct(create, partnerUrls, passages ?? [], onlyProduct);
            if (wanted is null)
            {
                // Named a product this project declares no partner for. Refusing by name beats writing
                // an ungrounded page about it.
                return [new ToolPageOutcome(
                    onlyProduct.Trim(),
                    null,
                    $"Refused: '{onlyProduct.Trim()}' is not one of this project's declared partners, so "
                    + "there is no crawl to ground a tool page on.")];
            }

            slices = [wanted];
        }
        else
        {
            slices = GccPartnerToolSlices.Build(create, partnerUrls, passages ?? []);
            if (onlyPartners is { Count: > 0 })
            {
                // Matched by host, the way the slices themselves are keyed. A URL that is not one of
                // this project's partners is refused by name rather than written about ungrounded.
                var unknown = onlyPartners
                    .Where(p => !slices.Any(s => string.Equals(
                        s.Host, GccRequiredToolMentions.HostKeyOf(p), StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (unknown.Count > 0)
                {
                    return [.. unknown.Select(p => new ToolPageOutcome(
                        p.Trim(),
                        null,
                        $"Refused: '{p.Trim()}' is not one of this project's declared partners, so there "
                        + "is no crawl to ground a tool page on."))];
                }

                var wantedHosts = onlyPartners
                    .Select(GccRequiredToolMentions.HostKeyOf)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                slices = slices.Where(s => wantedHosts.Contains(s.Host)).ToList();
            }
        }

        if (slices.Count == 0)
        {
            // No declared partners is not a thin page, it is a project that cannot have tool pages at
            // all. Said once, here, rather than five identical refusals.
            return [new ToolPageOutcome(
                create.Topic,
                null,
                "Refused: this project declares no partner URLs, so there is no product for a tool page "
                + "to be about. A tool page is about one partner's product.")];
        }

        // Phase 1 -- pre-flight. Every partner is assessed before any partner is drafted, so the
        // operator learns "three of five can be grounded" before the expensive half starts rather
        // than by counting the artifacts afterwards.
        var readiness = new List<GccPartnerToolReadiness>(slices.Count);
        foreach (var slice in slices)
        {
            readiness.Add(await AssessOrReportFaultAsync(slice, provider, ct, create.Id));
        }

        if (onReadiness is not null) await onReadiness(readiness);

        foreach (var verdict in readiness.Where(r => !r.Ready))
        {
            _logger.LogInformation(
                "Tool page pre-flight: {Product} ({Host}) cannot be grounded -- {Coverage}",
                verdict.ProductName, verdict.Host, verdict.Coverage);
        }

        // Phase 2 -- draft only what passed, reusing phase 1's extraction so a ready partner is
        // extracted once. A partner that failed the pre-flight is refused here by name, with the
        // coverage finding verbatim: the reason is already known, so drafting it to discover the
        // same refusal would be work with a known answer.
        var outcomes = new List<ToolPageOutcome>(slices.Count);
        foreach (var slice in slices)
        {
            var verdict = readiness.First(r => string.Equals(r.Host, slice.Host, StringComparison.OrdinalIgnoreCase));
            if (!verdict.Ready)
            {
                outcomes.Add(new ToolPageOutcome(
                    slice.ProductName,
                    null,
                    $"Refused: a tool page about {slice.ProductName} cannot be grounded in "
                    + $"{slice.Host}'s crawl -- {verdict.Coverage}."));
                continue;
            }

            using var piece = GccRunLog.ForPiece($"tool: {slice.ProductName}");
            try
            {
                var body = await GenerateStartingContentAsync(
                    slice.Narrow(create) with { StartingContentType = "tool" },
                    section,
                    provider,
                    ct,
                    mustMentionBlock,
                    slice.Passages,
                    toolName: slice.ProductName,
                    partnerExtraction: verdict.Extraction);
                outcomes.Add(new ToolPageOutcome(slice.ProductName, body, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Still caught: passing the pre-flight means the page can be grounded, not that every
                // later guard (quote verification, required mentions, provenance) will pass.
                if (GccRunFault.IsRefusal(ex))
                {
                    _logger.LogInformation(
                        "Tool page for {Product} ({Host}) was not written: {Reason}",
                        slice.ProductName, slice.Host, ex.Message);
                }
                else
                {
                    // Not a guard's refusal: the code failed while writing this page. Its stack goes to
                    // the log and to the run's record, so the cause can be read rather than guessed.
                    _logger.LogError(
                        ex, "Tool page for {Product} ({Host}) failed with a fault", slice.ProductName, slice.Host);
                    await GccRunLog.RecordIfAnyAsync(
                        "fault", new { step = "tool page", partner = slice.ProductName, fault = GccRunFault.Describe(ex) });
                }

                outcomes.Add(new ToolPageOutcome(slice.ProductName, null, ex.Message));
            }
        }

        return outcomes;
    }

    public async Task<string> GenerateStartingContentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? mustMentionBlock = null,
        IReadOnlyList<GccGroundedPassage>? passages = null,
        // The product this page is about, when the caller knows it. Topic is the problem (GccTopic), so
        // it cannot also be the subject -- that collapse is what sent extraction looking for a product
        // named after the keyword. Null keeps the old behaviour for callers with no partner to name.
        string? toolName = null,
        // Extraction already run and already gated by the tool pre-flight. Passed down so a partner
        // that passed readiness is not extracted a second time to draft it. Null means extract here,
        // which is every non-tool type and every caller that did no pre-flight.
        GccPartnerExtractionDocument? partnerExtraction = null)
    {
        ValidateSiteSectionGate(create.ProjectSiteRunId, section);
        ValidateBriefRequired(create);
        var briefBlock = BuildBriefAndResearchBlock(create);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            briefBlock = $"{briefBlock}\n\n{mustMentionBlock}";

        // Accepts both spellings: the frontend's content-types.ts sends kebab-case ("image-prompt");
        // "imagePrompt" is kept too since it's what this check used to require exclusively, and
        // nothing here can prove no other caller still sends it.
        if (string.Equals(create.StartingContentType, "imagePrompt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(create.StartingContentType, "image-prompt", StringComparison.OrdinalIgnoreCase))
        {
            // Topic only. This also required Notes, which the frontend never sends (createGccCreate
            // posts notes: null and collects none), so "Image prompt" -- an enabled picker option --
            // failed every generate it was part of, and under one-fails-all took the pillar and
            // blog beside it down too. The brief is already required by ValidateBriefRequired above
            // and is the context an image prompt is written from; notes are read when present.
            if (string.IsNullOrWhiteSpace(create.Topic))
                throw new InvalidOperationException("Standalone image prompt requires a topic.");
            return await GenerateImagePromptJsonAsync(
                create.Topic,
                string.IsNullOrWhiteSpace(create.Notes) ? briefBlock : $"{briefBlock}\n\n{create.Notes}",
                null,
                provider,
                ct);
        }

        // Accepts both spellings: content-types.ts's live picker sends "tool" ("Tool page"), never
        // "aiTool" -- confirmed directly, 2026-09-22, while scoping partner-grounding work.
        // GccGroundingResolver.RequiredCrawlTypes already hedged both spellings as separate keys;
        // this routing check hadn't. Without this fix, selecting "Tool page" skipped this branch
        // entirely and fell through to the generic long-form path below, bypassing partner
        // grounding altogether -- so "aiTool" is kept only because something might still send it,
        // not because it's the live value.
        if (string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(create.StartingContentType, "tool", StringComparison.OrdinalIgnoreCase))
        {
            // The brief, not the research: GenerateToolPageAsync hands the research to the tool prompts once,
            // as their evidence block (BuildBriefOnlyBlock).
            var toolSourceBrief = BuildBriefOnlyBlock(create);
            if (!string.IsNullOrWhiteSpace(mustMentionBlock))
                toolSourceBrief = $"{toolSourceBrief}\n\n{mustMentionBlock}";
            var tool = await GenerateToolPageAsync(
                toolName: string.IsNullOrWhiteSpace(toolName) ? create.Topic : toolName.Trim(),
                brief: create.Notes,
                sourceContext: $"{toolSourceBrief}\n\n{BuildAudience(create, section)}",
                department: string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department,
                relatedArticleUrl: null,
                provider: provider,
                ct: ct,
                create: create,
                passages: passages,
                extraction: partnerExtraction);
            return JsonSerializer.Serialize(new
            {
                // "Ramp: Automated Approval Workflows": a tool is written within the keyword and the
                // problem it solves, so its title says both (Jeff, 2026-10-07). The product stays in its
                // own field, which the export's slug reads, so the URL stays /tools/.../ramp.
                title = ToolPageTitle(tool.Name, create.Topic),
                productName = tool.Name,
                metaDescription = tool.Metadata.MetaDescription,
                summary = tool.Metadata.Summary,
                warnings = tool.Warnings ?? [],
                body = tool.Document,
                jsonLdSchema = tool.JsonLdSchema,
            }, CwDocumentJson);
        }

        // Content Creator long-form: CWV2 standalone blog body + persisted brief/research.
        var llm = GetLlm(provider);
        var consultantAppendix = BuildConsultantAppendix(create);
        var sourceContext = $"{briefBlock}\n\n{BuildAudience(create, section)}";
        if (consultantAppendix.Length > 0)
            sourceContext = $"{sourceContext}\n\n{consultantAppendix}";
        var brief = ExtractBriefFields(create.BriefJson);
        var context = BuildMinimalContext(
            create.Topic,
            sourceContext,
            ToLlm(provider),
            create.Department,
            brief.Segment,
            brief.Details,
            brief.Notes,
            brief.Angle,
            brief.PrimaryIntent,
            brief.SecondaryIntent,
            brief.BuyingStage,
            brief.ToneOfVoice,
            brief.EeatSignals,
            brief.LengthBand);
        // The outline is planned for this post, not taken from a constant. The three headings that
        // used to sit here -- "Overview", "Key considerations", "Next steps" -- shipped on every
        // blog this path produced, and a section called "Key considerations" has nothing in
        // particular to say, which is the whole of why these came back short (Jeff, 2026-09-23:
        // "The headings reflect why content word count is so drastically low").
        var metaResult = await llm.CompleteAsync(_prompts.BuildStandaloneBlogMetadataPrompt(context), ct);
        var planned = RequireCompleteMetadata(
            LlmResponseJsonParser.Parse<BlogMetadataDraft>(metaResult.Content, "standalone blog metadata"),
            "standalone blog metadata");
        var metadata = planned with
        {
            MetaDescription = Truncate(planned.MetaDescription, 160),
        };

        // The hook first, so the body continues it rather than restarting in reference voice at the
        // first H2. This path used to promote the model's first body section into the lede slot,
        // which is both no hook at all and one section short.
        var blogLedeResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogLedePrompt(context, metadata), ct);
        var (standaloneLede, _) = LlmResponseJsonParser.ParseLede(blogLedeResult.Content, "standalone blog lede");

        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes: null, lede: standaloneLede),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "standalone blog body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 blog body returned no sections.");
        var blogDocument = new ContentDocument(standaloneLede with { Tag = "h2" }, sections);
        blogDocument = ContentGuardrail.Apply(blogDocument).Document;
        return JsonSerializer.Serialize(blogDocument, CwDocumentJson);
    }

    public async Task<string> GenerateImagePromptJsonAsync(
        string topic,
        string? notes,
        string? artifactContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var result = await llm.CompleteAsync(
            _prompts.BuildStandaloneImagePrompt(topic, notes, artifactContext),
            ct);

        // The model's own fields, read and written back by this code. A reply that is not an image prompt
        // is refused. It used to be wrapped whole as the `prompt` with a canned style, which put whatever the
        // model had said -- an apology, an explanation, a half-written object -- into the field an image is
        // generated from, and called it a success.
        var reply = LlmResponseJsonParser.Parse<StandaloneImagePromptReply>(
            result.Content ?? string.Empty,
            "the page's image prompt",
            "image prompt",
            static r => string.IsNullOrWhiteSpace(r.Prompt) ? "the reply carried no prompt" : null);
        return JsonSerializer.Serialize(reply, JsonOpts);
    }

    private sealed record StandaloneImagePromptReply(
        string? Prompt,
        string? Style,
        string? NegativePrompt,
        string? AspectRatio,
        string? ImageModel,
        string? StylePreset,
        string? Notes);

    public async Task<string> GenerateRepurposePackAsync(
        string sourceJson,
        IReadOnlyList<string> channels,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        if (channels.Count == 0)
            throw new InvalidOperationException("At least one channel required for pack.");

        // Plan §7: one LLM call for chosen channels (not one call per post).
        var llm = GetLlm(provider);
        var channelList = string.Join(", ", channels);
        var brief =
            $"Produce ONE pack JSON for ONLY these channel slots (one variant object per slot, same order): {channelList}. " +
            "Shape: { \"variants\": [ { \"channel\": string, \"title\": string, \"headline\": string|null, \"body\": string, \"cta\": string|null, \"hashtags\": string[]|null } ] }. " +
            "Reply with valid JSON only.\nSource content:\n" + sourceJson;
        var request = new ChatCompletionRequest(
            Messages:
            [
                new ChatMessage(ChatRole.System, "You write marketing channel packs as strict JSON only."),
                new ChatMessage(ChatRole.User, brief),
            ],
            Temperature: 0.4);
        var result = await llm.CompleteAsync(request, ct);

        var pack = LlmResponseJsonParser.Parse<JsonElement>(
            result.Content ?? string.Empty,
            "the repurpose request",
            "social/ads pack",
            static element =>
                element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("variants", out var variants)
                && variants.ValueKind == JsonValueKind.Array
                && variants.GetArrayLength() > 0
                    ? null
                    : "the reply carried no non-empty variants array");
        return pack.GetRawText();
    }

    public static IReadOnlyList<PackVariant> ParsePackVariants(string packJson)
    {
        using var doc = JsonDocument.Parse(packJson);
        var list = new List<PackVariant>();
        foreach (var el in doc.RootElement.GetProperty("variants").EnumerateArray())
        {
            var channel = el.TryGetProperty("channel", out var c) ? c.GetString() ?? "" : "";
            var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var body = el.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var headline = el.TryGetProperty("headline", out var h) ? h.GetString() : null;
            var cta = el.TryGetProperty("cta", out var ct) ? ct.GetString() : null;
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException("Social/ads pack variant missing channel or body.");
            list.Add(new PackVariant(channel.Trim(), title.Trim(), headline, body.Trim(), cta));
        }

        if (list.Count == 0)
            throw new InvalidOperationException("Social/ads pack has no variants.");
        return list;
    }

    public sealed record PackVariant(
        string Channel,
        string Title,
        string? Headline,
        string Body,
        string? Cta);

    /// <summary>
    /// CWV2 tool page: body + ToolMetadataDraft + SoftwareApplication JSON-LD
    /// (same contract as ToolPageGenerator.GenerateOneToolAsync).
    /// </summary>
    public sealed record ToolPageResult(
        string Name,
        string Slug,
        ContentDocument Document,
        ToolMetadataDraft Metadata,
        string JsonLdSchema,
        string? RelatedArticleUrl,
        int WordCount,
        /// <summary>What the page ships with that the operator should see. Empty is the normal case.</summary>
        IReadOnlyList<string>? Warnings = null);

    public async Task<ToolPageResult> GenerateToolPageAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        string department,
        string? relatedArticleUrl,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? preferredSlug = null,
        GccCreateDto? create = null,
        IReadOnlyList<GccGroundedPassage>? passages = null,
        // A pre-flight's extraction for this same product and these same pages. Supplied, it replaces
        // the call below; the gate still runs on it, so a supplied document is verified here exactly
        // as a freshly extracted one is and this stays fail-closed at the drafting site.
        GccPartnerExtractionDocument? extraction = null)
    {
        var llmType = ToLlm(provider);
        var llm = GetLlm(provider);

        var name = toolName.Trim();
        var slug = string.IsNullOrWhiteSpace(preferredSlug) ? Slugify(name) : preferredSlug.Trim();
        var description = string.Join(
            "\n",
            new[] { brief, sourceContext }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var app = new SoftwareApplicationDescriptor(name, string.IsNullOrWhiteSpace(description) ? null : description);

        // Partner grounding, 2026-09-22: this page was never grounded in the real partner extraction
        // spec (plans/partner-extraction-complete.md, restored after being swept as collateral) even
        // though the create form's own commit message once claimed it was. The quoteables here are
        // whatever GccGroundingResolver already resolved for this create's partner URLs and merged
        // into ResearchJson -- the same data pillar/blog now read via Stage 2, just run through the
        // richer extraction service instead of rendered as prose.
        var toolResearch = create is null ? null : GccResearchFetchService.Deserialize(create.ResearchJson);
        var partnerPages = toolResearch?.Quoteables ?? [];
        // The product's own domain, for the SoftwareApplication's url. The partner crawl seeds are
        // the vendor's own pages, so it is known here -- and only here; the orchestrator's tool
        // path has no such source and leaves it unset rather than guessing.
        //
        // Fail closed on ambiguity: several partners' pages can land in one create's research until
        // per-partner pages ship (plans/tool-page-per-partner.md), and picking one of several
        // origins would attach the wrong company's domain to this product.
        var partnerOrigins = partnerPages
            .Select(pg => Uri.TryCreate(pg.Url, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) : null)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (partnerOrigins.Count == 1)
        {
            app = app with { Url = partnerOrigins[0] };
        }

        var partnerExtraction = extraction ?? (partnerPages.Count == 0
            ? null
            : await _partnerExtraction.ExtractFromPagesAsync(partnerPages, [name], llmType, ct));
        // Same gate as the pre-flight: a failed page is a fault, and a page drafted from the
        // pages that happened to survive is the middle state AGENTS.md forbids.
        var groundedExtraction = partnerExtraction is not null
            && partnerExtraction.PagesFailed == 0
            && HasSufficientPartnerData(partnerExtraction)
            ? partnerExtraction
            : null;

        // Fail closed, not a silent degrade to generic content: GccGroundingResolver already
        // refuses this content type at the controller before generation starts when the project
        // has no partner URLs or none are indexed (RequiredFor("aitool") = [Partner]). This is the
        // second half of that same policy -- grounding can pass that gate (partner URLs exist,
        // something is indexed) and still hand back pages extraction finds nothing usable in. A
        // Tool/Partner page written from generic brief text in that state is exactly the "aiTool
        // output type... resolves grounded tool/partner data server-side" claim that was never
        // actually true -- it must refuse here, not quietly repeat that gap.
        //
        // Strengthened 2026-09-22 (Jeff): "does any one of 22 payload categories have anything in
        // it" was a reported failure that did not terminate -- a create with a single ICP entry and
        // nothing else passed this gate, then wrote 3,500-5,000 words across six sections with zero
        // code-level check on any of it. Tool is the revenue-critical content type and must meet at
        // least the rigor Pillar/Blog get from GccHeadingProvenanceGuard's per-heading evidence
        // check, not less -- see HasSufficientPartnerData below for the real bar.
        if (create is not null && groundedExtraction is null)
        {
            var coverage = DescribePartnerDataCoverage(partnerExtraction);
            // "Refused:" is not decoration -- GenerateAsync's first catch filters on that prefix to
            // answer 400 (an operator-facing decision), and everything else falls through to a 503
            // "Generate validation/config failed". This refusal is deliberate and correct, so it
            // was being reported to the operator as "Service Unavailable", as though GeekAPI were
            // down (Jeff, 2026-09-22). Same prefix convention GccGroundingResolver's refusals get.
            // Extraction is asked for one product by name, and on the Create path that name is
            // create.Topic -- so a create whose topic is a keyword rather than a product sends the
            // extractor looking for a product that does not exist, across partner sites that are full
            // of material about real ones. Five partners with 84-226 quotable spans each and 130+
            // features between them yielded 1 of 22 categories, twice, with different partner sets
            // (Jeff's own per-partner counts, 2026-10-01). So the refusal names what it searched for:
            // without that, abundant evidence reads as missing evidence and the operator re-crawls
            // partners that were never the problem.
            var searchedFor = string.IsNullOrWhiteSpace(name) ? "(no name)" : name.Trim();
            throw new InvalidOperationException(
                // '{name}' is the tool page being written, not the source of the evidence -- the
                // evidence comes from the project's partner sites. The old phrasing read as though
                // partner grounding were expected to be found inside that term (Jeff, 2026-09-22).
                $"Refused: Partner grounding required. The tool page '{name}' must be grounded in "
                + $"the project's partner evidence; indexed partner crawl data exists, but "
                + $"extracting it yielded too little to write a full page ({coverage}). "
                + $"Extraction searched {partnerPages.Count} retrieved partner page(s) for a product "
                + $"named \"{searchedFor}\" -- if that is a keyword rather than one partner's product, "
                + "that is why the categories are empty, and no amount of partner crawling will fill "
                + "them. Not generating a thinly-grounded page.");
        }

        var extractedToolResearchJson = groundedExtraction is null
            ? null
            : JsonSerializer.Serialize(groundedExtraction, PartnerExtractionJsonOpts);
        var toolType = RequireType("tool");

        var paragraphs = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceContext))
            paragraphs.Add(sourceContext.Trim());
        if (!string.IsNullOrWhiteSpace(brief))
            paragraphs.Add(brief.Trim());

        // Built by the shared BuildMinimalContext, not by hand. This was a duplicate of that
        // method's construction, identical field for field except that it passed none of the brief
        // -- no ContentAngle, no AudienceSegment, no PrimaryIntent, no ToneOfVoice -- so
        // BuildLedeTypeGuidance's `hasBrief` check was false on the tool path and the lede type was
        // chosen with nothing to go on. Jeff, 2026-09-23: "The right lede, based off of the 'Angle
        // for SEO' already supplied but apparently not used?" -- supplied, and never delivered.
        //
        // Pillar reaches the same method through BuildPillarContext and passes all seventeen
        // fields; Tool passing none is exactly the second-class treatment that keeps recurring.
        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        var toolBrief = ExtractBriefFields(create?.BriefJson);
        // The create's topic, not the product's name. This passed `name`, so BuildMinimalContext
        // derived TargetKeyword from "Stampli" -- and every SEO instruction the writer gets
        // interpolates TargetKeyword: the opening slot became "the problem this reader has with
        // Stampli today", SeoBodyInstruction asked for "Stampli" in a heading, and the page was
        // scored (GccController.Seo, GcwSeoAnalyzer) against the create's keyword, which it had
        // never been told. 0.00% density on a page about exactly that keyword, 2026-10-03. The
        // product is the page's subject and reaches the prompt as `app`; the keyword is the topic's.
        // The legacy no-create path has only the name, and keeps it.
        var toolTopic = create?.Topic is { Length: > 0 } createTopic ? createTopic : name;
        var context = BuildMinimalContext(
            toolTopic,
            string.Join("\n\n", paragraphs),
            llmType,
            dept,
            toolBrief.Segment,
            toolBrief.Details,
            toolBrief.Notes,
            toolBrief.Angle,
            toolBrief.PrimaryIntent,
            toolBrief.SecondaryIntent,
            toolBrief.BuyingStage,
            toolBrief.ToneOfVoice,
            toolBrief.EeatSignals,
            toolBrief.LengthBand);

        // Equal to Pillar's outline in count and per-section depth (Jeff, 2026-09-22: Tool must be
        // equal in word count to Pillar if not longer). It is read from ToolPrompts rather than
        // written out again here: this literal was the third copy of that list, sitting under a
        // comment saying it had to be kept in sync with a fourth copy inside BuildToolBodyPrompt.
        // EvidenceBlock carries the QUOTEABLE RESEARCH block: the passages GccGroundingResolver
        // merged into ResearchJson. The lede reads it from this context, and WriteToolDraftAsync below
        // keeps it and appends the competitor and own-site blocks after it, so the body sees all
        // three. ExtractedResearchJson is the other half -- the partner extraction, which reaches
        // the body as PARTNER DATA.
        //
        // Empty research renders an empty string and the append is skipped.
        // The operator's framing of this niche, narrowed to this product: its own override when one was
        // written, otherwise the category's. Resolved here because this is the only point that has both
        // the create's brief and the product's name -- which is what keys the override to a host.
        var toolPartnerUrls = create is null ? null : await PartnerUrlsForAsync(create, ct);
        var nicheFraming = create is null
            ? null
            : GccNicheFramingReader.ForProduct(create.BriefJson, toolPartnerUrls, name);
        // The operator's FAQ questions for this tool alone -- its own perTool entry, never the
        // category's. Answered in ToolFaqAsync from what a search of the partner's crawl found for
        // each, or left out and reported (2026-10-08: FAQ fields for the tool pages).
        IReadOnlyList<string> toolFaqQuestions = create is null
            ? []
            : GccNicheFramingReader.ToolFaqQuestions(create.BriefJson, toolPartnerUrls, name);
        // The host those questions are filed under, which is also what their searches are filed under.
        var toolFaqHost = create is null
            ? string.Empty
            : GccNicheFramingReader.HostForProduct(create.BriefJson, toolPartnerUrls, name);

        var toolOutlineCtx = new ContentTypes.ContentTypePromptContext(
            context, App: app, ToolSlug: slug, ExtractedResearchJson: extractedToolResearchJson,
            EvidenceBlock: create is null ? null : BuildResearchBlock(create),
            NicheFraming: nicheFraming);
        var pillarMeta = new ArticleMetadataDraft(
            Title: name,
            MetaDescription: Truncate((brief ?? name).Trim(), 160),
            Keywords: [.. new[] { name, context.TargetKeyword }
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            SectionOutline: [.. toolType.OutlineFor(toolOutlineCtx).Select(sl => sl.Label)]);

        // The hook is written before the body, so the body can continue it. It used to run after --
        // the page was drafted in reference voice and an opening was fitted to the front of it
        // afterwards, which is exactly how a page reads well for three paragraphs and then turns
        // into a chore (Jeff, 2026-09-23: "While it starts off nice with a story, it becomes dull
        // and a chore to read afterward").
        //
        // BuildArticleLedePrompt is the shared 12-type lede path, not a Tool-specific copy: the
        // taxonomy is chosen against this brief's audience, angle, intent and tone. Tool had no
        // lede call at all until 2026-09-23 -- its first body section was promoted into the lede
        // slot, so every tool page opened with a section headed "Overview" and the outline quietly
        // lost a section.
        //
        // The hook is additive, the way the FAQ section is: all six outline sections survive. Tool
        // must equal or exceed Pillar in length, so a lede that consumed a section would push it
        // the wrong way.
        //
        // Reached through toolType.Lede, not by calling the builder here. This called
        // _prompts.BuildArticleLedePrompt directly until 2026-09-29, which left ToolPrompts.Lede
        // with zero callers -- a type's own prompt decision made somewhere else, which is the exact
        // defect IContentTypePrompts exists to remove (see its docstring: "the choice of which to
        // call made in a switch elsewhere"). Routing it through the type is also what gets the
        // opening its evidence, since that is what carries EvidenceBlock.
        //
        // Every amount in this partner's evidence that is not in US dollars, named for the opening
        // and for each part of the body. Read from everything the page is written from -- the research
        // block and the extraction here, the competitor and own-site blocks further down.
        // The site's tools that are not this project's partners stay out of what this page reads too,
        // as they do for the pillar and the blog. None on the orchestrator's create-less path.
        IReadOnlyList<string> toolUnlisted = create is null ? [] : (await PartnerToolsAsync(create, ct)).Unlisted;
        var toolForeignAmounts = Guardrail.GccCurrencyGrammar.ForeignAmountsInstruction(string.Join(
            Environment.NewLine,
            toolOutlineCtx.EvidenceBlock ?? string.Empty,
            Guardrail.GccJsonEvidence.TextOf(extractedToolResearchJson),
            create is null ? string.Empty : BuildCompetitorResearchBlock(create),
            create is null ? string.Empty : BuildOwnSiteCoverageBlock(create, toolUnlisted)));
        var ledeResult = await llm.CompleteAsync(
            toolType.Lede(toolOutlineCtx with
            {
                Metadata = pillarMeta,
                EvidenceBlock = WithForeignAmountsNamed(toolOutlineCtx.EvidenceBlock, toolForeignAmounts),
            }),
            ct);
        var (toolLede, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, $"tool page '{name}' lede");

        // `brief` used to be passed positionally here, landing in the revisionNotes slot -- every
        // first-time generation had its own brief framed to the model as "REVISION REQUIRED --
        // address the reviewer's feedback," phantom feedback on a draft that never existed. It
        // already reaches the model correctly via app.Description ("Tool summary: ..." below), so
        // dropping it here removes a misleading duplicate, not the only copy.
        // In batches, same reason as the pillar: this page's own outline asks for 3,200-4,400 words
        // and a single response holds about 3,000 in this JSON.
        // Cut before the body is written, not after: the writer quotes from this list and the guard
        // checks the draft against it, so both must be looking at the same one.
        //
        // From the typed passages, not the retrieved pages. A passage carries the crawl page's
        // blocks mapped kind for kind, so a candidate is the page's own prose -- where
        // GccQuoteablePage.Paragraphs is RenderChunk output with "Section:" / "Context:" /
        // "Specific detail:" labels interleaved, which had to be stripped back off by guesswork. A
        // block the crawler typed as a quotation is also taken whole rather than sentence-split.
        //
        // ReadTypedPassagesAsync already paid for these on every generate and nothing read them
        // (GccGroundingOutcome.PartnerPassages had no consumer in the solution).
        var quoteCandidates = GccQuoteCandidates.From(passages ?? []);

        // The body's evidence is the research block AND the competitor/own-site blocks, never one in
        // place of the other. The body writer took a single block that replaced the context's, and
        // its callers passed the competitor/own-site text -- so the QUOTEABLE RESEARCH block set on
        // toolOutlineCtx reached the lede and was overwritten before every body call. The tool body,
        // the one type that must quote a partner, was written without the retrieved passages.
        // The tool page had no competitor evidence at all, while one of its sections is
        // "how a buyer should judge this product -- fit, pricing, and the adjacent approaches they
        // are also weighing". It was writing that section with no idea what the alternatives say.
        var toolCompetitorBlock = create is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                new[] { BuildPublisherPositionsBlock(create, toolUnlisted), BuildCompetitorResearchBlock(create), BuildOwnSiteCoverageBlock(create, toolUnlisted) }
                    .Where(b => b.Length > 0));

        // FAQ, additional to the body's own word-count target, not part of it (Jeff, 2026-09-22).
        // Sourced only from the partner FAQ pairs the extraction read off the partner's pages --
        // never invented and never re-derived the way Pillar's PAA-driven FAQ section has to answer
        // from scratch. The pairs are model-extracted and nothing checks them against the page text
        // before this call; GccPartnerFaqAsset.VerifiedAnswer is a field name, not a verification.
        //
        // Written once, after the body draft, and the draft the guard sees carries it. Two sources,
        // one section: the partner's own FAQ (paraphrased, never re-derived) and the operator's
        // questions for this tool, answered only from the partner's own pages. A question that is not
        // answered is left out and reported as a gap -- never answered from general knowledge
        // (2026-10-08: FAQ fields for the tool pages).
        //
        // Each question is answered from what a search of the partner's crawl found for that question
        // (GccGroundingResolver.FaqQuestions, filed in the research's FaqEvidence). Until 2026-10-10
        // the questions were never searched for: they were answered from whatever the page's other
        // searches had brought back, and a question none of those was about was reported as one "no
        // page of the partner's answers" when nothing had looked. So the report now says what was
        // checked, and there are four of them: not searched, searched and nothing found, shown
        // passages and not answered, and answered under a heading that is none of the questions.
        Section? toolFaqSection = null;
        var toolFaqWritten = false;
        async Task<Section?> ToolFaqAsync(List<string> shortfalls)
        {
            if (toolFaqWritten) return toolFaqSection;
            toolFaqWritten = true;
            Section? head = null;
            var children = new List<Section>();
            if (groundedExtraction is { FaqBank.Count: > 0 })
            {
                var faqResult = await llm.CompleteAsync(
                    _prompts.BuildToolFaqSectionPrompt(context, pillarMeta, app, groundedExtraction.FaqBank),
                    ct);
                head = LlmResponseJsonParser.ParseSection(faqResult.Content, "h2", $"tool page '{name}' FAQ section");
                children.AddRange(head.Children);
            }

            if (toolFaqQuestions.Count > 0)
            {
                // What becomes of each question, for the run's record: the same four outcomes the
                // operator is told, readable without the prompts.
                var outcomes = new List<object>(toolFaqQuestions.Count);
                // Only a question with passages is sent. One that was not searched for, or whose
                // search found nothing, has nothing to be answered from, and the model is not paid
                // to say so.
                var toAsk = new List<(string Question, IReadOnlyList<GccQuoteablePage> Pages)>();
                foreach (var question in toolFaqQuestions)
                {
                    var found = GccToolFaqEvidence.FoundFor(toolResearch?.FaqEvidence, toolFaqHost, question);
                    if (found is null)
                    {
                        shortfalls.Add($"FAQ: {toolFaqHost}'s crawl was not searched for \"{question}\"; it was left out.");
                        outcomes.Add(new { question, passages = 0, outcome = "not searched" });
                        continue;
                    }

                    if (GccToolFaqEvidence.PassageCount(found) == 0)
                    {
                        shortfalls.Add($"FAQ: a search of {toolFaqHost}'s crawl found nothing for \"{question}\"; it was left out.");
                        outcomes.Add(new { question, passages = 0, outcome = "nothing found" });
                        continue;
                    }

                    toAsk.Add((question, found));
                }

                for (var start = 0; start < toAsk.Count; start += PaaQuestionsPerFaqCall)
                {
                    var batch = toAsk.Skip(start).Take(PaaQuestionsPerFaqCall).ToList();
                    var answered = await llm.CompleteAsync(
                        _prompts.BuildToolFaqFromQuestionsPrompt(
                            context, pillarMeta, app, [.. batch.Select(b => b.Question)], GccToolFaqEvidence.Render(batch)),
                        ct);
                    var section = LlmResponseJsonParser.ParseSection(
                        answered.Content, "h2", $"tool page '{name}' FAQ, questions {start + 1}-{start + batch.Count}");
                    head ??= section;
                    // By reference: two answers may read alike, and each is still its own child.
                    var kept = new HashSet<Section>(ReferenceEqualityComparer.Instance);
                    foreach (var (question, pages) in batch)
                    {
                        var shown = GccToolFaqEvidence.PassageCount(pages);
                        var answer = section.Children.FirstOrDefault(c => AnswersQuestion(c.Heading, question));
                        if (answer is null)
                        {
                            shortfalls.Add(
                                $"FAQ: the writer was shown {shown} passage(s) from {toolFaqHost} for \"{question}\" "
                                + "and did not answer it; it was left out.");
                            outcomes.Add(new { question, passages = shown, outcome = "not answered" });
                            continue;
                        }

                        outcomes.Add(new { question, passages = shown, outcome = "answered" });
                        if (kept.Add(answer)) children.Add(answer);
                    }

                    // An answer under a heading that is none of the call's questions cannot be filed
                    // under one, and guessing which it meant would put an answer under a question it
                    // may not address. It is dropped, as it always was -- and now it is said.
                    foreach (var stray in section.Children.Where(c => !kept.Contains(c)))
                    {
                        shortfalls.Add(
                            $"FAQ: the writer answered under \"{stray.Heading}\", which is not a question it was sent; "
                            + "the answer was dropped.");
                        outcomes.Add(new { question = stray.Heading, passages = 0, outcome = "answered under another heading" });
                    }
                }

                await GccRunLog.RecordIfAnyAsync("faq", new { tool = name, host = toolFaqHost, questions = outcomes });
            }

            if (head is null || children.Count == 0) return null;
            toolFaqSection = head with { Children = children };
            return toolFaqSection;
        }

        // A tool page carries at most one block quotation of the partner, in their own published
        // words (Jeff, 2026-09-26: "I want a blockquote in each tool"). The prompt asks for it when
        // a listed span earns it; the guard is what keeps it honest -- without a check the model
        // could return one it wrote itself carrying a real company's URL on its cite, and
        // ContentGuardrail passes quotes through untouched by design. A page with no quotation is
        // not refused (Jeff, 2026-10-08): the guard reports `blockquote-missing` as a gap and the
        // page ships, because on a thin crawl no listed span may answer the problem and an invented
        // one is worse than none.
        //
        // Scoped to `create is not null` (no candidates, no quotation check), the same boundary the
        // partner-grounding refusal above draws. The legacy no-create path has no partner evidence at
        // all, so requiring a partner quote there would be requiring an invented one.
        // What each FAQ question's search found is evidence the writer is shown -- in the FAQ call, and
        // in no other block. The page's figure and currency checks read the evidence the writer was
        // shown, so these passages are part of it: without them an answer that takes "within 45
        // minutes" from its own passage refuses the whole page for stating a figure that appears in
        // none of its evidence.
        var toolFaqEvidenceText = string.Join(
            Environment.NewLine,
            toolFaqQuestions
                .Select(q => GccToolFaqEvidence.FoundFor(toolResearch?.FaqEvidence, toolFaqHost, q))
                .SelectMany(found => found ?? [])
                .SelectMany(page => page.Paragraphs));

        var toolGuardInputs = GuardInputsFor(
            create,
            context,
            ContentTypes.GccLongFormTypes.Tool,
            provenance: null,
            requiredTools: [name],
            evidenceText: string.Join(
                Environment.NewLine,
                toolOutlineCtx.EvidenceBlock ?? string.Empty,
                toolCompetitorBlock,
                toolFaqEvidenceText,
                brief ?? string.Empty),
            quoteCandidates: create is null ? null : quoteCandidates,
            extractionJson: extractedToolResearchJson);

        async Task<GccDraft> WriteToolDraftAsync()
        {
            var shortfalls = new List<string>();
            var callsUnderFloor = new List<string>();
            var written = await GenerateSectionsInBatchesAsync(
                llm,
                toolType,
                toolOutlineCtx with
                {
                    Metadata = pillarMeta,
                    Lede = toolLede,
                    EvidenceBlock = string.Join(
                        Environment.NewLine,
                        new[] { toolOutlineCtx.EvidenceBlock, toolCompetitorBlock, toolForeignAmounts }
                            .Where(b => !string.IsNullOrWhiteSpace(b))),
                    QuoteCandidates = quoteCandidates,
                },
                toolType.OutlineFor(toolOutlineCtx),
                $"Tool page '{name}'",
                ct,
                callsUnderFloor);

            // Snap first, judge second. The writer copies a candidate's words and copying drifts -- a
            // live run lost AvidXchange's page to a shortened span with an ellipsis added. Snapping
            // restores the system's own string for anything that matches a candidate; the guard still
            // refuses anything that matches none. A quotation chosen by number carries empty runs
            // until it is snapped, so it would be refused without this.
            List<Section> sections = create is null
                ? written
                : [.. Guardrail.GccToolQuoteGuard.SnapQuotesToCandidates(written, quoteCandidates)];
            // The keyword's shortenings put back on the opening and the body as written, before anything
            // code builds joins them.
            var (toolOpening, toolBody) = await RemapKeywordAsync(
                $"the tool page '{name}'", toolLede with { Tag = "h2" }, sections, toolOutlineCtx.Context.TargetKeyword);
            sections = toolBody;
            // A tool page links no other tool page (GuardInputsFor hands it none), so nothing is linked here.
            sections = await LinkToolsAsync($"the tool page '{name}'", sections, []);
            sections = GccClosing.AppendTo(sections, ClosingFor(create));
            if (await ToolFaqAsync(shortfalls) is { } faq) sections.Add(faq);
            return new GccDraft(new ContentDocument(toolOpening, sections), shortfalls, callsUnderFloor);
        }

        var (document, toolWarnings) = await GuardedDraftAsync(
            $"the tool page '{name}'",
            WriteToolDraftAsync,
            doc => Guardrail.GccDraftGuard.Tool(
                doc, toolGuardInputs with { AppendedSections = toolFaqSection is null ? 0 : 1 }),
            toolOutlineCtx.Context.TargetKeyword);

        // Per-H2 image prompts. Tool pages are long-form (a ten-section outline, equal to Pillar,
        // plus an optional FAQ section) and this is the revenue-critical content type. `section` is
        // accepted but unused inside GenerateSectionImagePromptsAsync, so null is correct here.
        document = await WithSectionImagePromptsAsync("tool", name, document, null, provider, toolWarnings, ct);

        var wordCount = ContentDocumentText.CountWords(document);

        var metaResult = await llm.CompleteAsync(
            _prompts.BuildToolMetadataPrompt(context, pillarMeta, app, document),
            ct);
        var metadata = RequireCompleteMetadata(
            LlmResponseJsonParser.Parse<ToolMetadataDraft>(metaResult.Content, "tool metadata"), "tool metadata");

        var metaDescription = metadata.MetaDescription.Length > 160
            ? metadata.MetaDescription[..160]
            : metadata.MetaDescription;
        metadata = metadata with { MetaDescription = metaDescription };

        // {base}/{department}/{descriptor}/{slug} -- Jeff, 2026-10-02: /tools/accounting/accounts-payable.
        // The descriptor directory is what stops one partner's pages colliding: the slug is the product
        // name, so Dext for accounts payable and Dext for expense management were the same URL.
        var toolUrl = GccContentPath.For(_company.ToolBaseUrl, create, slug);
        // Our page about the product. Distinct from app.Url, which is the product's own home.
        app = app with { PageUrl = toolUrl };
        var now = DateTime.UtcNow;
        // AreaServed/PublisherType come through like everywhere else: they describe the publisher
        // node, and this page's publisher is the operator, same as the pillar's.
        // Faq is independent of site data: it reads the tool page's own generated document.
        var schemaMeta = ContentMetadataFactory.For(
            context, name, metaDescription, toolUrl, pillarMeta.Keywords, document, now);

        var pillarUrl = string.IsNullOrWhiteSpace(relatedArticleUrl)
            ? GccContentPath.DirectoryFor(_company.ArticleBaseUrl, create)
            : relatedArticleUrl;

        // Partner grounding: when extraction actually yielded data, emit the real partner-extraction
        // §9 JSON-LD (fail-closed on price/review assertions with no library evidence) instead of the
        // generic single-description builder, which has no concept of pricing, offers, or reviews at
        // all. Falls back to the generic builder when ungrounded, so non-partner tool pages are
        // unaffected.
        string jsonLd;
        if (groundedExtraction is not null)
        {
            var partnerNode = GeekAPI.Services.ContentCreator.Partner.GccPartnerSoftwareApplicationJsonLd
                .TryBuild(groundedExtraction, partnerPages);
            if (partnerNode is not null)
            {
                GeekAPI.Services.ContentCreator.Partner.GccPartnerSoftwareApplicationJsonLd
                    .EnsureShipReadyOrThrow(partnerNode, groundedExtraction);
                jsonLd = JsonSerializer.Serialize(partnerNode, PartnerExtractionJsonOpts);
            }
            else
            {
                jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarUrl, app);
            }
        }
        else
        {
            jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarUrl, app);
        }

        if (string.IsNullOrWhiteSpace(jsonLd))
            throw new InvalidOperationException($"CWV2 tool JSON-LD schema builder returned empty for '{name}'.");

        return new ToolPageResult(
            name, slug, document, metadata, jsonLd, pillarUrl, wordCount, toolWarnings);
    }

    private static readonly JsonSerializerOptions PartnerExtractionJsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>True when extraction actually found something -- an all-empty document (no
    /// indexed partner pages, or pages with nothing this schema covers) must not be treated as
    /// "grounded" just because the call succeeded.</summary>
    /// <summary>True when extraction found enough to substantively ground a Tool page across all
    /// six required sections -- not just "some single field has one item," which let a create with
    /// a lone ICP entry and nothing else pass, then write 3,500-5,000 words with zero code-level
    /// check on any of it. Tool is the revenue-critical content type and must meet at least the
    /// rigor Pillar/Blog get from GccHeadingProvenanceGuard, not less (Jeff, 2026-09-22).
    /// Requires a real signal of what the product actually does -- FeatureInventory ("the
    /// definitive list of what the product does" per the extraction system prompt) or at least one
    /// isolated factual claim via Citables -- AND breadth across at least 3 of the
    /// <see cref="PartnerDataCategoryCount"/> payload categories, so sections beyond Key Capabilities have something real to draw from too.</summary>
    private static bool HasSufficientPartnerData(GccPartnerExtractionDocument extraction) =>
        (extraction.FeatureInventory.Count > 0 || extraction.Citables.Count > 0)
        && CountPopulatedPartnerDataCategories(extraction) >= 3;

    /// <summary>How many categories <see cref="CountPopulatedPartnerDataCategories"/> counts across.
    /// Twenty since 2026-10-04, when freshness and disclosures left the schema; the refusal prints it
    /// rather than a literal, so the message cannot keep naming a count the document no longer has.</summary>
    internal const int PartnerDataCategoryCount = 20;

    private static int CountPopulatedPartnerDataCategories(GccPartnerExtractionDocument extraction) =>
        (extraction.Citables.Count > 0 ? 1 : 0)
        + (extraction.Advertisements.Count > 0 ? 1 : 0)
        + (extraction.Comparisons.Count > 0 ? 1 : 0)
        + (extraction.Alternatives.Count > 0 ? 1 : 0)
        + (extraction.PricingCatalog.Count > 0 ? 1 : 0)
        + (extraction.Icp.Count > 0 ? 1 : 0)
        + (extraction.Integrations.Count > 0 ? 1 : 0)
        + (extraction.FaqBank.Count > 0 ? 1 : 0)
        + (extraction.CaseStudies.Count > 0 ? 1 : 0)
        + (extraction.Testimonials.Count > 0 ? 1 : 0)
        + (extraction.Awards.Count > 0 ? 1 : 0)
        + (extraction.FeatureInventory.Count > 0 ? 1 : 0)
        + (extraction.TechnicalConstraints.Count > 0 ? 1 : 0)
        + (extraction.OfferCtas.Count > 0 ? 1 : 0)
        + (extraction.Disqualifiers.Count > 0 ? 1 : 0)
        + (extraction.UseCasePlaybooks.Count > 0 ? 1 : 0)
        + (extraction.Categories.Count > 0 ? 1 : 0)
        + (extraction.BattlecardSlices.Count > 0 ? 1 : 0)
        + (extraction.DemoBeats.Count > 0 ? 1 : 0)
        + (extraction.ComplianceSnippets.Count > 0 ? 1 : 0);

    /// <summary>Diagnostic for the refusal message -- names what was and wasn't found, so "reported
    /// failure" means an operator can see why, not just that grounding failed.</summary>
    private static string DescribePartnerDataCoverage(GccPartnerExtractionDocument? extraction)
    {
        if (extraction is null) return "no extractable partner pages";
        var populated = CountPopulatedPartnerDataCategories(extraction);
        var hasCapabilitySignal = extraction.FeatureInventory.Count > 0 || extraction.Citables.Count > 0;

        // An extraction outage and a genuine data shortage both leave every category empty, so the
        // message has to separate them or the operator cannot tell a broken pipeline from a partner
        // site that simply has no pricing/feature content on the retrieved pages.
        var attempt = extraction.PagesFailed > 0
            ? $"{extraction.PagesFailed} of {extraction.PagesAttempted} page(s) FAILED extraction "
              + "(provider call threw -- this is a fault, not a data shortage) "
              + $"[first error: {extraction.FirstFailure ?? "unreported"}]; "
            : $"{extraction.PagesAttempted} page(s) extracted cleanly; ";

        return attempt
            + $"{populated} of {PartnerDataCategoryCount} payload categories populated (need at least 3), "
            + $"core capability signal (features or citable claims) {(hasCapabilitySignal ? "present" : "missing")}";
    }

    /// <summary>Legacy alias — prefer <see cref="GenerateToolPageAsync"/>. Every caller that has a
    /// create in scope must pass it through, same as the primary generate path -- omitting it here
    /// was silently reopening the exact ungrounded-tool-page gap Stage 2 closed.</summary>
    public async Task<(string Name, ContentDocument Document, string? MetaDescription, string? Summary)> GenerateToolAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        GccCreateDto? create = null)
    {
        var tool = await GenerateToolPageAsync(
            toolName, brief, sourceContext, "marketing", null, provider, ct, create: create);
        return (tool.Name, tool.Document, tool.Metadata.MetaDescription, tool.Metadata.Summary);
    }

    /// <summary>Serialize a CWV2 ContentDocument for legacy GCC artifact storage.</summary>
    public static string SerializeDocument(ContentDocument document) =>
        JsonSerializer.Serialize(document, CwDocumentJson);

    /// <summary>
    /// Attaches an <c>imagePrompt</c> field to a flat, short-form body JSON object (email/social —
    /// no <see cref="Section"/> to merge into the way pillar/blog's per-H2 prompts do). Pure and
    /// separately testable on purpose: the controller-level caller wraps this in the try/catch
    /// that makes image-prompt failure non-fatal to the primary content, which needs a live
    /// HTTP/DI pipeline to exercise; the merge itself does not.
    /// </summary>
    public static string MergeImagePromptField(string contentJson, string imagePromptJson)
    {
        var contentNode = System.Text.Json.Nodes.JsonNode.Parse(contentJson)?.AsObject()
            ?? throw new InvalidOperationException("Content body was not a JSON object.");
        var imagePromptNode = System.Text.Json.Nodes.JsonNode.Parse(imagePromptJson)
            ?? throw new InvalidOperationException("Image prompt generation returned non-JSON content.");
        contentNode["imagePrompt"] = imagePromptNode;
        return contentNode.ToJsonString();
    }

    /// <summary>
    /// The prompt set for a content type. Absent means nothing implements it -- which is the same
    /// fact DisabledContentTypes asserts separately today, and the reason those two collapse once
    /// every type resolves through here.
    /// </summary>
    private ContentTypes.IContentTypePrompts RequireType(string contentType) =>
        _types.Find(contentType)
        ?? throw new InvalidOperationException($"No prompt set is registered for content type '{contentType}'.");

    /// <summary>
    /// The operator's provider -- recording every call into the run's log when a run is being logged
    /// (<see cref="GccRunLog"/>), so the prompt, the answer, the model and the tokens of every call are on
    /// record whether the piece is saved or refused.
    /// </summary>
    private IContentGenerationProvider GetLlm(ContentGeneratorProvider provider)
    {
        var real = _cwProviders.Get(ToLlm(provider));
        return GccRunLog.Current is null ? real : new GccRecordingProvider(real);
    }

    /// <summary>
    /// The workflow provider for the one the operator chose. Every value is named: this was
    /// <c>== Anthropic ? Anthropic : OpenAi</c>, so any value it did not know -- a provider added to the
    /// enum and not here -- was silently written by OpenAI and recorded as the provider asked for.
    /// </summary>
    internal static LlmProviderType ToLlm(ContentGeneratorProvider provider) => provider switch
    {
        ContentGeneratorProvider.Anthropic => LlmProviderType.Anthropic,
        ContentGeneratorProvider.OpenAi => LlmProviderType.OpenAi,
        _ => throw new InvalidOperationException(
            $"Refused: provider '{provider}' has no workflow provider mapped, so nothing was generated."),
    };

    private ProjectGenerationContext BuildMinimalContext(
        string topic,
        string notes,
        LlmProviderType llmType,
        string? department = null,
        string? audienceSegment = null,
        IReadOnlyList<string>? audienceDetails = null,
        string? audienceNotes = null,
        string? contentAngle = null,
        string? primaryIntent = null,
        string? secondaryIntent = null,
        string? buyingStage = null,
        string? toneOfVoice = null,
        IReadOnlyList<string>? eeatSignals = null,
        string? lengthBand = null,
        GccPublisherProfileResolver.PublisherProfile? publisherProfile = null,
        IReadOnlyList<KnownCrawlTool>? knownTools = null)
    {
        // The operator's own home page, when the project site has been crawled. CrawledHeadings was
        // [] and CrawledParagraphs held only the create's Notes, so the writer had never seen the
        // site it was writing for and invented a methodology, a set of buying criteria and a closing
        // suggestion in place of the ones already published.
        var profile = publisherProfile ?? GccPublisherProfileResolver.PublisherProfile.Empty;
        var paragraphs = string.IsNullOrWhiteSpace(notes)
            ? new List<string>()
            : new List<string> { notes };
        paragraphs.AddRange(profile.Paragraphs);
        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        return new ProjectGenerationContext(
            ProjectName: topic,
            ProjectUrl: _company.ArticleBaseUrl,
            // The keyword half, not the whole topic -- and this is the one that produced a 0.00%
            // density score. Every SEO instruction the writer gets interpolates TargetKeyword:
            // SeoLedeInstruction asks for it in the lede, SeoOutlineInstruction and
            // SeoBodyInstruction ask for it in exactly one H2. Handed the whole topic, the writer was
            // being told to put "Accounts Payable: Automated Data Entry & Processing" verbatim into a
            // heading and an opening sentence, which no readable prose does -- so it appeared nowhere
            // and all three keyword checks failed on a draft that discussed the keyword throughout.
            //
            // GcwSeoAnalyzer has been scored against GccTargetKeyword.FromTopic since 2026-09-28. The
            // prompt side was never moved with it, so the writer and the scorer disagreed about what
            // the keyword even was. ProjectName and DetectedFocus deliberately keep the whole topic:
            // the context half exists so the model knows the subject area, which is the entire reason
            // the topic is written that way.
            TargetKeyword: GccTargetKeyword.FromTopic(topic),
            Department: dept,
            SiteName: _company.PublisherName,
            DetectedTone: "Professional, consultative",
            DetectedFocus: topic,
            CrawledHeadings: [.. profile.Headings],
            CrawledParagraphs: paragraphs,
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: [],
            PublisherName: _company.PublisherName,
            PublisherLogoUrl: _company.PublisherLogoUrl,
            AuthorName: _company.AuthorName,
            ArticleBaseUrl: _company.ArticleBaseUrl,
            BlogBaseUrl: _company.BlogBaseUrl,
            ToolBaseUrl: _company.ToolBaseUrl,
            ImplementerPositioning: _company.ImplementerPositioning,
            ConsultationAnchorHref: _company.ConsultationAnchorHref,
            ConsultationCtaLabel: _company.ConsultationCtaLabel,
            Provider: llmType,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null,
            AudienceSegment: audienceSegment,
            AudienceDetails: audienceDetails,
            AudienceNotes: audienceNotes,
            ContentAngle: contentAngle,
            PrimaryIntent: primaryIntent,
            SecondaryIntent: secondaryIntent,
            BuyingStage: buyingStage,
            ToneOfVoice: toneOfVoice,
            EeatSignals: eeatSignals,
            LengthBand: lengthBand,
            // Empty on every Create-path generate until 2026-09-27, which is why
            // AppendKnownToolsBrief never rendered and no draft ever linked a tool.
            KnownCrawlTools: knownTools,
            // Every Content Creator page ends on the line GccClosing builds, so the writer is not asked
            // for a closing and is not given the operator's questions.
            PageBuildsClosing: true);
    }

    /// <summary>
    /// A tool page's slug from its product name. The one definition: the page is generated with it,
    /// and <see cref="GccPartnerToolPages"/> builds the link to that page with it.
    /// </summary>
    internal static string ToolSlug(string productName) => Slugify(productName);

    private static string Slugify(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "tool" : s;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static readonly IReadOnlyDictionary<string, string> LegacyAudienceSegmentMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["affinity"] = "affinity",
            ["interest_affinity"] = "affinity",
            ["cold_prospect"] = "affinity",
            ["in_market"] = "in_market",
            ["life_events"] = "life_events",
            ["detailed_demographics"] = "detailed_demographics",
            ["your_data"] = "your_data",
            ["engaged_visitor"] = "your_data",
            ["lead"] = "your_data",
            ["customer"] = "your_data",
            ["lapsed"] = "your_data",
            ["lookalike"] = "your_data",
            ["custom"] = "custom",
            ["account_based"] = "custom",
            ["local_geo"] = "custom",
        };

    private static readonly IReadOnlyDictionary<string, string> LegacyAngleMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["comparative"] = "comparative",
            ["comparison"] = "comparative",
            ["problem_solution"] = "problem_solution",
            ["case_study_data"] = "case_study_data",
            ["case_study"] = "case_study_data",
            ["ultimate_guide"] = "ultimate_guide",
            ["howto_workflow"] = "ultimate_guide",
            ["explainer"] = "ultimate_guide",
            ["listicle"] = "ultimate_guide",
            ["objection_faq"] = "ultimate_guide",
        };

    private static readonly HashSet<string> ValidAudienceDetails = new(StringComparer.OrdinalIgnoreCase)
        { "demographic_attributes", "behavioral_triggers", "boolean_combination" };

    private static readonly IReadOnlyDictionary<string, string> LegacyAudienceDetailMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["demographic_attributes"] = "demographic_attributes",
            ["demographic"] = "demographic_attributes",
            ["firmographic"] = "demographic_attributes",
            ["behavioral_triggers"] = "behavioral_triggers",
            ["list_match"] = "behavioral_triggers",
            ["boolean_combination"] = "boolean_combination",
            ["life_event"] = "boolean_combination",
            ["buying_committee"] = "boolean_combination",
        };

    /// <summary>Single Brief incorporate — mirrors <c>GeekContentCreator/src/lib/content-creator/brief-catalog.ts:migrateBrief</c> as the one source of truth. Keep LEGACY_* maps in sync with that file.</summary>
    internal static (string? Segment, IReadOnlyList<string>? Details, string? Notes, string? Angle) ExtractBriefAudienceAngle(string? briefJson)
    {
        var fields = ExtractBriefFields(briefJson);
        return (fields.Segment, fields.Details, fields.Notes, fields.Angle);
    }

    /// <summary>People Also Ask lines the operator typed on the brief — newline string or JSON array.</summary>
    /// <summary>A list of questions from the brief, one per line in a string or one per array item --
    /// <c>paaQuestions</c> (the pillar's People Also Ask) and <c>blogFaqQuestions</c> (the blog's FAQ).</summary>
    private static IReadOnlyList<string>? ParseQuestionList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return null;
        }

        var list = new List<string>();
        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = prop.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                foreach (var line in raw.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (line.Length > 0)
                    {
                        list.Add(line);
                    }
                }
            }
        }
        else if (prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prop.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && el.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s.Trim());
                }
            }
        }

        return list.Count == 0 ? null : list;
    }

    internal static BriefFields ExtractBriefFields(string? briefJson)
    {
        if (string.IsNullOrWhiteSpace(briefJson)) return new BriefFields();
        try
        {
            using var doc = JsonDocument.Parse(briefJson);
            var root = doc.RootElement;
            string? S(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            string? Any(params string[] names)
            {
                foreach (var n in names)
                {
                    var v = S(n);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
                return null;
            }

            var rawSegment = Any("audienceSegment", "audiencePrimary");
            string? segment = null;
            if (!string.IsNullOrWhiteSpace(rawSegment) && LegacyAudienceSegmentMap.TryGetValue(rawSegment.Trim(), out var mappedSeg))
                segment = mappedSeg;

            IReadOnlyList<string>? details = null;
            List<string>? detailList = null;
            if (root.TryGetProperty("audienceDetails", out var detProp) && detProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in detProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        if (LegacyAudienceDetailMap.TryGetValue(s.Trim(), out var mappedDet))
                        {
                            detailList ??= new List<string>();
                            if (!detailList.Contains(mappedDet, StringComparer.OrdinalIgnoreCase))
                                detailList.Add(mappedDet);
                        }
                    }
            }
            else if (root.TryGetProperty("audienceModifiers", out var modProp) && modProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in modProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                    {
                        if (LegacyAudienceDetailMap.TryGetValue(s.Trim(), out var mappedDet))
                        {
                            detailList ??= new List<string>();
                            if (!detailList.Contains(mappedDet, StringComparer.OrdinalIgnoreCase))
                                detailList.Add(mappedDet);
                        }
                    }
            }
            if (detailList is not null) details = detailList;

            var notes = Any("audienceNotes", "audienceDetail");
            var exclude = S("audienceExclude");
            if (!string.IsNullOrWhiteSpace(exclude))
                notes = string.IsNullOrWhiteSpace(notes) ? $"Exclude: {exclude.Trim()}" : $"{notes.Trim()}\nExclude: {exclude.Trim()}";

            var rawAngle = S("angle");
            string? angle = null;
            if (!string.IsNullOrWhiteSpace(rawAngle) && LegacyAngleMap.TryGetValue(rawAngle.Trim(), out var mappedAngle))
                angle = mappedAngle;

            var primaryIntent = Any("primaryIntent", "intent");
            if (primaryIntent is not null && !new[] { "informational", "navigational", "commercial_investigation", "transactional" }.Contains(primaryIntent))
                primaryIntent = null;
            var secondaryIntent = S("secondaryIntent");
            if (secondaryIntent is not null && !new[] { "local", "freebies", "comparison" }.Contains(secondaryIntent))
                secondaryIntent = null;
            var buyingStage = S("buyingStage");
            if (buyingStage is not null && !new[] { "awareness", "consideration", "action" }.Contains(buyingStage))
            {
                // legacy map
                var legacyBuying = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["tof_interest"] = "awareness", ["mof_consideration"] = "consideration", ["decision"] = "action", ["bof_actions"] = "action" };
                if (!legacyBuying.TryGetValue(buyingStage, out var mapped)) buyingStage = null;
                else buyingStage = mapped;
            }
            var toneOfVoice = S("toneOfVoice");
            if (toneOfVoice is not null && toneOfVoice is not "consultant_professional" and not "informational_instructional" and not "commercial_balanced")
            {
                // legacy was numeric Record
                if (toneOfVoice is not null && toneOfVoice.StartsWith("{")) toneOfVoice = "commercial_balanced";
                else toneOfVoice = null;
            }
            IReadOnlyList<string>? eeatSignals = null;
            if (root.TryGetProperty("eeatSignals", out var eeatProp) && eeatProp.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var el in eeatProp.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.String && el.GetString() is string s && new[] { "first_hand_experience", "expertise", "authoritativeness", "trustworthiness" }.Contains(s))
                        if (!list.Contains(s)) list.Add(s);
                if (list.Count > 0) eeatSignals = list;
            }
            var lengthBand = S("lengthBand");
            IReadOnlyList<string>? paaQuestions = ParseQuestionList(root, "paaQuestions");
            IReadOnlyList<string>? blogFaqQuestions = ParseQuestionList(root, "blogFaqQuestions");
            var segNotes = notes;
            return new BriefFields
            {
                Segment = segment,
                Details = details,
                Notes = string.IsNullOrWhiteSpace(segNotes) ? null : segNotes.Trim(),
                Angle = angle,
                PrimaryIntent = string.IsNullOrWhiteSpace(primaryIntent) ? null : primaryIntent,
                SecondaryIntent = string.IsNullOrWhiteSpace(secondaryIntent) ? null : secondaryIntent,
                BuyingStage = string.IsNullOrWhiteSpace(buyingStage) ? null : buyingStage,
                ToneOfVoice = string.IsNullOrWhiteSpace(toneOfVoice) ? null : toneOfVoice,
                EeatSignals = eeatSignals,
                LengthBand = string.IsNullOrWhiteSpace(lengthBand) ? null : lengthBand.Trim(),
                PaaQuestions = paaQuestions,
                BlogFaqQuestions = blogFaqQuestions,
            };
        }
        catch (JsonException)
        {
            return new BriefFields();
        }
    }

    internal sealed record BriefFields
    {
        public string? Segment { get; init; }
        public IReadOnlyList<string>? Details { get; init; }
        public string? Notes { get; init; }
        public string? Angle { get; init; }
        public string? PrimaryIntent { get; init; }
        public string? SecondaryIntent { get; init; }
        public string? BuyingStage { get; init; }
        public string? ToneOfVoice { get; init; }
        public IReadOnlyList<string>? EeatSignals { get; init; }
        public string? LengthBand { get; init; }
        public IReadOnlyList<string>? PaaQuestions { get; init; }
        /// <summary>The blog's FAQ questions, written by the operator (2026-10-08): answered at the end
        /// of the blog from the brief and the evidence, the way the pillar's People Also Ask is.</summary>
        public IReadOnlyList<string>? BlogFaqQuestions { get; init; }
    }

    public static string SerializeAnalysisPayload(SiteAnalysisStoredPayload payload) =>
        JsonSerializer.Serialize(payload, JsonOpts);

    public static SiteAnalysisStoredPayload ParseAnalysisPayload(string? gapsJson)
    {
        if (string.IsNullOrWhiteSpace(gapsJson))
            throw new InvalidOperationException("Site analysis payload is missing.");

        try
        {
            using var doc = JsonDocument.Parse(gapsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyGaps = JsonSerializer.Deserialize<List<ContentGapDto>>(gapsJson, JsonOpts)
                    ?? throw new InvalidOperationException("Site analysis gaps JSON is invalid.");
                // Legacy analyses stored gaps only — no site pages. Callers must fail closed for section context.
                return new SiteAnalysisStoredPayload(legacyGaps, [], []);
            }

            return JsonSerializer.Deserialize<SiteAnalysisStoredPayload>(gapsJson, JsonOpts)
                ?? throw new InvalidOperationException("Site analysis payload JSON is invalid.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Site analysis payload JSON could not be parsed.", ex);
        }
    }

    public static IReadOnlyList<ContentGapDto> DeserializeGaps(string? gapsJson) =>
        ParseAnalysisPayload(gapsJson).Gaps;

    public static IReadOnlyList<RelatedPageDto> DeserializeSitePages(string? gapsJson) =>
        ParseAnalysisPayload(gapsJson).SitePages;

    /// <summary>
    /// Builds section context from stored Geek-SEO site pages for the chosen gap.
    /// Returns null when related pages cannot be resolved (caller must fail closed).
    /// </summary>
    public static SiteSectionContextDto? TryBuildSectionContext(
        Guid analysisId,
        SiteAnalysisStoredPayload payload,
        string gapTopic) =>
        GccSiteSection.TryBuildSectionContext(analysisId, payload, gapTopic);

    public static GcwSeoAnalyzer.SeoReport AnalyzeSeo(string bodyJson, string keyword, string? contentType) =>
        GcwSeoAnalyzer.Analyze(bodyJson, keyword, contentType);

    public static GcwPolishAnalyzer.PolishReport AnalyzePolish(string bodyJson) =>
        GcwPolishAnalyzer.Analyze(bodyJson, Array.Empty<string>());

    private static string BuildAudience(GccCreateDto create, SiteSectionContextDto? section)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Starting content type: {create.StartingContentType}");
        sb.AppendLine($"Topic / keyword: {create.Topic}");

        // The brief. This method is named BuildAudience and contained no audience: it is the only
        // channel Email and Social have to the brief, and it carried none of it -- no Angle for
        // SEO, no audience segment, no intent, no tone. Long-form types receive these through
        // ProjectGenerationContext as well; short form had nothing at all. Jeff, 2026-09-23, on the
        // angle governing structure: "But this pertains to all Content types, including short form."
        var brief = ExtractBriefFields(create.BriefJson);
        if (!string.IsNullOrWhiteSpace(brief.Angle))
            sb.AppendLine($"Angle for SEO: {brief.Angle} — this frames the piece; open and structure it accordingly.");
        if (!string.IsNullOrWhiteSpace(brief.PrimaryIntent))
            sb.AppendLine($"Primary intent: {brief.PrimaryIntent}"
                + (string.IsNullOrWhiteSpace(brief.SecondaryIntent) ? "" : $" (secondary: {brief.SecondaryIntent})"));
        if (!string.IsNullOrWhiteSpace(brief.BuyingStage))
            sb.AppendLine($"Buying stage: {brief.BuyingStage}");
        if (!string.IsNullOrWhiteSpace(brief.Segment))
            sb.AppendLine($"Audience: {brief.Segment}"
                + (brief.Details is { Count: > 0 } d ? $" — {string.Join(", ", d)}" : "")
                + (string.IsNullOrWhiteSpace(brief.Notes) ? "" : $" — {brief.Notes}"));
        else if (!string.IsNullOrWhiteSpace(brief.Notes))
            sb.AppendLine($"Audience notes: {brief.Notes}");
        if (!string.IsNullOrWhiteSpace(brief.ToneOfVoice))
            sb.AppendLine($"Tone of voice: {brief.ToneOfVoice} — hold this voice throughout.");
        if (!string.IsNullOrWhiteSpace(create.Notes))
            sb.AppendLine($"Operator notes: {create.Notes}");
        if (section is not null)
        {
            sb.AppendLine("SITE SECTION CONTEXT (required — do not generate keyword-only):");
            if (!string.IsNullOrWhiteSpace(section.GapSectionPath))
                sb.AppendLine($"Section path: {section.GapSectionPath}");
            sb.AppendLine($"Gap topic: {section.GapTopic}");
            if (section.TopicalNeighbors.Count > 0)
                sb.AppendLine($"Topical neighbors: {string.Join(", ", section.TopicalNeighbors)}");
            sb.AppendLine("Related existing pages (align voice, avoid duplication, cross-link sensibly):");
            foreach (var p in section.RelatedPages)
            {
                sb.AppendLine($"- {p.Title} ({p.Url})");
                if (p.Headings.Length > 0)
                    sb.AppendLine($"  Headings: {string.Join(" | ", p.Headings.Select(h => $"H{h.Level}: {h.Text}"))}");
                if (!string.IsNullOrWhiteSpace(p.Excerpt))
                    sb.AppendLine($"  Excerpt: {p.Excerpt}");
            }
        }
        return sb.ToString();
    }

    private static List<string> BuildEvidence(SiteSectionContextDto? section)
    {
        var list = new List<string>();
        if (section is null) return list;
        foreach (var p in section.RelatedPages)
        {
            list.Add($"{p.Title}: {p.Excerpt}");
        }
        return list;
    }

    /// <summary>
    /// The operator's framing of the problem for the email and the social post, or null when the brief
    /// carries none. The pillar, the blog and the tool page argue it; these two were given none of it
    /// until 2026-10-10.
    /// </summary>
    private static string? ShortFormFraming(GccCreateDto create) =>
        GccNicheFramingReader.ForCategory(create.BriefJson)?.ShortFormGuidance();

    // Copied from content-writer-v2 with Site Analyzer grounding integrated
    public async Task<string> GenerateEmailAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var briefBlock = $"Topic: {create.Topic}\nNotes: {create.Notes}";
        var groundingBlock = BuildAudience(create, section);

        var system = new StringBuilder()
            .AppendLine("You write cold outreach / sales emails for an IT consulting firm that specializes in AI implementation.")
            .AppendLine("Body must be 150-200 words.")
            .AppendLine("Pitch ONE clear idea. No HTML. No inline link syntax. Do not invent URLs.")
            .AppendLine("ctaLabel is short button/link text (e.g. \"Read the full guide\"). The destination URL is injected by the app.")
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine("{\"subject\": string, \"body\": string, \"ctaLabel\": string}")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(briefBlock)
            .AppendLine()
            .AppendLine(groundingBlock);
        if (ShortFormFraming(create) is { } framing)
            user.AppendLine().AppendLine(framing);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: 1024);

        var result = await llm.CompleteAsync(request, ct);

        // The page stores what this code wrote from the model's fields, not the model's text: a reply that
        // cannot be read is refused here instead of being saved as the page's body. It used to be returned
        // trimmed and unparsed, so a malformed reply became a stored email.
        var email = LlmResponseJsonParser.Parse<ColdEmailReply>(
            result.Content ?? string.Empty,
            "the email page",
            "cold-outreach email",
            static r =>
                string.IsNullOrWhiteSpace(r.Subject) ? "the reply carried no subject"
                : string.IsNullOrWhiteSpace(r.Body) ? "the reply carried no body"
                : string.IsNullOrWhiteSpace(r.CtaLabel) ? "the reply carried no ctaLabel"
                : null);
        return JsonSerializer.Serialize(email, JsonOpts);
    }

    private sealed record ColdEmailReply(string? Subject, string? Body, string? CtaLabel);

    public async Task<string> GenerateSocialPostAsync(
        GccCreateDto create,
        string platform,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var briefBlock = $"Topic: {create.Topic}\nNotes: {create.Notes}";
        var groundingBlock = BuildAudience(create, section);

        var (styleGuidance, lengthGuidance, maxTokens) = platform switch
        {
            "facebook" => (
                "Casual B2B link-share post: 30-50 words (~40-250 characters). Put the hook in the first line before \"See more\" truncates (~200 chars). 1 emoji max. End with a light CTA.",
                "Keep under 250 characters total when possible.",
                512),
            "linkedin" => (
                "Professional thought-leadership post: 200-300 words. Structure: (1) hook in first 30 words — mobile \"see more\" folds at ~210 chars, (2) context/problem, (3) 1-2 insights, (4) CTA. No emojis or at most one.",
                "Aim for 1,300-1,900 characters. Maximum 3,000 characters.",
                2048),
            _ => ("Professional tone, concise.", "Keep concise.", 1024)
        };

        var system = new StringBuilder()
            .AppendLine($"You write {platform} posts for an IT consulting firm that specializes in AI implementation.")
            .AppendLine(styleGuidance)
            .AppendLine(lengthGuidance)
            .AppendLine("Respond with ONLY a single valid JSON object — no code fences:")
            .AppendLine("{\"text\": string}")
            .AppendLine("JSON rules: one string value for text. Use \\n for line breaks.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(briefBlock)
            .AppendLine()
            .AppendLine(groundingBlock);
        if (ShortFormFraming(create) is { } framing)
            user.AppendLine().AppendLine(framing);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: maxTokens);

        var result = await llm.CompleteAsync(request, ct);

        // Stored as this code wrote it from the model's text field; see GenerateEmailAsync.
        var post = LlmResponseJsonParser.Parse<SocialPostReply>(
            result.Content ?? string.Empty,
            $"the {platform} page",
            "social post",
            static r => string.IsNullOrWhiteSpace(r.Text) ? "the reply carried no text" : null);
        return JsonSerializer.Serialize(post, JsonOpts);
    }

    private sealed record SocialPostReply(string? Text);

    /// <summary>The pillar's standing section plan. Headings the writer must fill, not invent.</summary>

    private ProjectGenerationContext BuildPillarContext(
        GccPublisherProfileResolver.PublisherProfile publisherProfile,
        IReadOnlyList<KnownCrawlTool> knownTools,
        GccCreateDto create,
        SiteSectionContextDto? section,
        string? mustMentionBlock,
        ContentGeneratorProvider provider)
    {
        var briefBlock = $"Topic: {create.Topic}\nNotes: {create.Notes}";
        var sourceContext = $"{briefBlock}\n\n{BuildAudience(create, section)}";
        var consultantAppendix = BuildConsultantAppendix(create);
        if (consultantAppendix.Length > 0)
            sourceContext = $"{sourceContext}\n\n{consultantAppendix}";
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            sourceContext = $"{sourceContext}\n\nMust mention:\n{mustMentionBlock}";

        var brief = ExtractBriefFields(create.BriefJson);
        return BuildMinimalContext(
            create.Topic,
            sourceContext,
            ToLlm(provider),
            create.Department,
            brief.Segment,
            brief.Details,
            brief.Notes,
            brief.Angle,
            brief.PrimaryIntent,
            brief.SecondaryIntent,
            brief.BuyingStage,
            brief.ToneOfVoice,
            brief.EeatSignals,
            brief.LengthBand,
            publisherProfile,
            knownTools);
    }

    /// <summary>
    /// A blog body as a <see cref="ContentDocument"/>, serialized — the same contract as the pillar
    /// and as the standalone blog path this service already used.
    /// </summary>
    public async Task<string> GenerateBlogBodyAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var competitorAnalyses = await ResolveCompetitorAnalysesAsync(create, ct);
        var partnerTools = await PartnerToolsAsync(create, ct);
        var context = BuildPillarContext(
            await _publisherProfile.ResolveAsync(create.ProjectId, ct),
            partnerTools.Linked,
            create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(
            create, competitorAnalyses, mustMentionBlock, await PartnerUrlsForAsync(create, ct));
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses, partnerTools.Unlisted);
        // Metadata first, because everything downstream needs what it produces. The title has to
        // exist before the lede is written or the hook just restates it, and the section outline is
        // what the body is actually written against.
        //
        // This used to run last, and the body was handed a hardcoded ["Overview", "Key
        // considerations", "Next steps"] instead -- three generic sections where the metadata
        // prompt itself asks for "5-6 conversational H2 headings -- hooks, numbered angles, or
        // how-to framing" and BlogSectionCountMin is 5. The model generated a real outline and it
        // was thrown away, which is why a blog came back at 791 words opening with "Overview"
        // (Jeff, 2026-09-23).
        var blogMetaResult = await llm.CompleteAsync(_prompts.BuildStandaloneBlogMetadataPrompt(context), ct);
        var blogMeta = RequireCompleteMetadata(
            LlmResponseJsonParser.Parse<BlogMetadataDraft>(blogMetaResult.Content, "blog metadata"), "blog metadata");
        var blogMetaDescription = blogMeta.MetaDescription.Length > 160
            ? blogMeta.MetaDescription[..160]
            : blogMeta.MetaDescription;
        var metadata = blogMeta with { MetaDescription = blogMetaDescription };

        var blogType = RequireType("blog");
        // The opening gets the retrieved evidence too. It did not until 2026-09-29: evidenceBlock
        // was built here and handed only to the body, so the lede and introduction -- the most-read
        // paragraphs on the page, and the ones that set every factual claim after them -- were
        // written from brief text alone while the evidence sat in a local three lines above. The
        // pillar lede prompt has always told the model "a number may appear only if it is in the
        // supplied evidence or published by this publisher"; no evidence was supplied, so that rule
        // could not be met or broken.
        //
        // The research half, not the whole block: see BuildPillarLedePrompt for why the competitor
        // headings stay out of a prompt that states no provenance rules.
        var ledeEvidence = WithPublisherPositions(BuildResearchBlock(create), create, partnerTools.Unlisted);
        // The amounts in this page's evidence that are not in US dollars, named for the opening and
        // for each part of the body.
        var blogForeignAmounts = Guardrail.GccCurrencyGrammar.ForeignAmountsInstruction(
            $"{evidenceBlock}{Environment.NewLine}{ledeEvidence}");
        // The operator's framing of the category, as the pillar gets it: what the post argues from.
        var blogPromptCtx = new ContentTypes.ContentTypePromptContext(
            context,
            BlogMetadata: metadata,
            EvidenceBlock: WithForeignAmountsNamed(ledeEvidence, blogForeignAmounts),
            NicheFraming: GccNicheFramingReader.ForCategory(create.BriefJson));
        var ledeResult = await llm.CompleteAsync(blogType.Lede(blogPromptCtx), ct);
        // Same mismatch as pillar above: this prompt asks for LedeJsonContract, so it is read with
        // ParseLede. Reading it as a sections array failed every blog generation.
        var (blogLede, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "blog lede");

        var blogRequiredTools = partnerTools.Required;
        var blogToolInstruction = partnerTools.Instruction;
        // The valid tags are stated up front: with no retry to name them after a refusal, the first
        // attempt has to be told what resolves.
        var blogEvidence = string.Join(
            Environment.NewLine,
            new[] { evidenceBlock, blogToolInstruction, GccHeadingProvenanceGuard.LicensedValues(evidence) }
                .Where(b => !string.IsNullOrWhiteSpace(b)));

        // In batches, same reason as pillar and tool: one response cannot hold the 1,800-word floor
        // in this JSON, so asking for the whole post in one call capped it by arithmetic -- 1,199
        // words and a 0.2% keyword density were the symptom (Jeff, 2026-09-28).
        var blogOutline = blogType.OutlineFor(blogPromptCtx);

        // The blog's FAQ, from the brief's own questions (2026-10-08: the pillar had its People Also
        // Ask and the blog had nothing). Written before the body like the pillar's, in calls of eight,
        // and carried onto the draft after the closing, where the guards check it like the body.
        Section? blogFaq = null;
        // The questions that came back with no answer under them: reported with the draft, never a refusal.
        var blogFaqGaps = new List<string>();
        var blogFaqQuestions = ExtractBriefFields(create.BriefJson).BlogFaqQuestions;
        if (blogFaqQuestions is { Count: > 0 })
        {
            blogFaq = await WriteFaqInBatchesAsync(
                llm,
                batch => _prompts.BuildBlogFaqSectionPrompt(context, metadata, batch),
                blogFaqQuestions,
                "the blog's FAQ",
                blogFaqGaps,
                ct);
        }

        var blogGuardInputs = GuardInputsFor(
            create,
            context,
            ContentTypes.GccLongFormTypes.Blog,
            evidence,
            blogRequiredTools,
            blogEvidence,
            appendedSections: blogFaq is null ? 0 : 1,
            partnerTools: partnerTools);

        // Every check, once -- see GccDraftGuard.
        async Task<GccDraft> WriteBlogDraftAsync()
        {
            var shortfalls = new List<string>();
            var callsUnderFloor = new List<string>();
            var sections = await GenerateSectionsInBatchesAsync(
                llm,
                blogType,
                blogPromptCtx with
                {
                    EvidenceBlock = WithForeignAmountsNamed(blogEvidence, blogForeignAmounts),
                    Lede = blogLede,
                },
                blogOutline,
                "Blog body",
                ct,
                callsUnderFloor);
            // The keyword's shortenings put back on the opening and the body as written, then the links,
            // the closing and the FAQ, none of which is remapped.
            var (blogOpening, blogBody) = await RemapKeywordAsync(
                "the blog", blogLede with { Tag = "h2" }, sections, blogPromptCtx.Context.TargetKeyword);
            sections = blogBody;
            sections = await LinkToolsAsync("the blog", sections, blogPromptCtx.Context.KnownCrawlTools ?? []);
            sections = GccClosing.AppendTo(sections, ClosingFor(create));
            if (blogFaq is not null) sections.Add(blogFaq);
            shortfalls.AddRange(blogFaqGaps);
            var whole = new ContentDocument(blogOpening, sections);
            return new GccDraft(ContentGuardrail.Apply(whole).Document, shortfalls, callsUnderFloor);
        }

        var (document, blogWarnings) = await GuardedDraftAsync(
            "the blog", WriteBlogDraftAsync, doc => Guardrail.GccDraftGuard.Blog(doc, blogGuardInputs),
            blogPromptCtx.Context.TargetKeyword);

        // Image prompts are attached here rather than by the caller, the way the tool page already
        // does it. The caller used to run them on the returned JSON, which only worked while this
        // returned a bare ContentDocument -- it now returns the same envelope Tool does, so the
        // step has to happen before wrapping.
        document = await WithSectionImagePromptsAsync(
            "blog", create.Topic, document, section, provider, blogWarnings, ct);

        var blogNow = DateTime.UtcNow;
        // GccContentPath, for the reason the pillar gives: the export's canonical is built there.
        var blogUrl = GccContentPath.For(_company.BlogBaseUrl, create, Slugify(metadata.Title));
        var blogSchemaMeta = ContentMetadataFactory.For(
            context, metadata.Title, blogMetaDescription, blogUrl, metadata.Keywords, document, blogNow);

        return JsonSerializer.Serialize(new
        {
            title = metadata.Title,
            metaDescription = blogMetaDescription,
            summary = metadata.Summary,
            warnings = blogWarnings,
            body = document,
            // Empty, not the blog's own URL -- passing blogUrl made the BlogPosting cite itself.
            // Pillar and Blog are independent artifacts on this path, so there is no companion
            // article to cite; the builder omits the citation when this is blank.
            jsonLdSchema = _blogSchema.Build(blogSchemaMeta, relatedArticleUrl: string.Empty),
        }, CwDocumentJson);
    }

    /// <summary>
    /// Stage 8a's resolver, called for the first time from generation itself. Returns empty --
    /// never throws -- when the create has no project or the project has no indexed competitor
    /// crawl; a missing competitor analysis is not a reason to refuse pillar/blog generation, only
    /// a reason the "competitor:" provenance tag has nothing to resolve against.
    /// </summary>
    private async Task<IReadOnlyList<GccCompetitorPageAnalysis>> ResolveCompetitorAnalysesAsync(
        GccCreateDto create, CancellationToken ct) =>
        create.ProjectId is { } projectId
            ? await _competitorAnalysis.ResolveAsync(projectId, ct)
            : [];

    /// <summary>
    /// Stage 2: research + competitor evidence, rendered as one block the caller appends directly
    /// into the body prompt's own system text. Not routed through <c>ProjectGenerationContext
    /// .CrawledParagraphs</c> -- <c>ResearchBriefBuilder</c>'s <c>ArticleSection</c> phase (the
    /// pillar body's own phase) never renders that field at all, so evidence merged there would
    /// have reached the same silent dead end this stage exists to close. A direct parameter can't
    /// depend on which phase happens to render it.
    /// </summary>
    /// <param name="unlistedTools">
    /// Tools the publisher's site lists that are not this project's partners. An own-site page about
    /// one of them is left out of the block, and the names are redacted from the rest of it.
    /// </param>
    private static string BuildEvidenceBlock(
        GccCreateDto create,
        IReadOnlyList<GccCompetitorPageAnalysis> competitorAnalyses,
        IReadOnlyList<string> unlistedTools)
    {
        var sb = new StringBuilder();
        var researchBlock = BuildResearchBlock(create);
        if (researchBlock.Length > 0)
            sb.AppendLine(researchBlock);

        // The publisher's own positions, before anything read for difference or for coverage: the
        // method the page describes and the subjects it covers are the publisher's first.
        var positionsBlock = BuildPublisherPositionsBlock(create, unlistedTools);
        if (positionsBlock.Length > 0)
            sb.AppendLine(positionsBlock);

        var competitorBlock = BuildCompetitorHeadingBlock(competitorAnalyses);
        if (competitorBlock.Length > 0)
            sb.AppendLine(competitorBlock);

        var competitorResearch = BuildCompetitorResearchBlock(create);
        if (competitorResearch.Length > 0)
            sb.AppendLine(competitorResearch);

        var ownSite = BuildOwnSiteCoverageBlock(create, unlistedTools);
        if (ownSite.Length > 0)
            sb.AppendLine(ownSite);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// What this publisher has already published on this topic — so the piece does not say it again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third instruction, and the reason these are a third list. Partner evidence is cited,
    /// competitor evidence is never cited, and the publisher's own pages are neither: they are the
    /// ground already covered. Writing the same page twice splits its own ranking and gives a
    /// returning reader nothing.
    /// </para>
    /// <para>
    /// Distinct from <c>BuildPublisherSiteBlock</c>, which carries the home page — who this
    /// publisher is, their framework, their figures, their offer — and is about staying consistent
    /// with them. This is retrieved against the create's own topic and is about not repeating them.
    /// </para>
    /// </remarks>
    /// <summary>
    /// What the publisher states on their own site page, by heading, as the research recorded it for
    /// this run (<see cref="GccPublisherPositionsReader"/>). Empty when the run read none.
    /// </summary>
    internal static string BuildPublisherPositionsBlock(GccCreateDto create, IReadOnlyList<string> unlistedTools)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        return GccPublisherPositions.Block(research?.PublisherPositions, GccTopic.KeywordOf(create.Topic), unlistedTools);
    }

    /// <param name="unlistedTools">
    /// Tools this site lists that are not this project's partners. The site's own page about one of
    /// them -- its tool page for a partner of another project -- is a page about a tool this piece does
    /// not name, so it is left out; where a remaining page mentions one, the name is redacted. On
    /// 2026-10-06 the 8:26 run's pillar named Tipalti, a partner on another Accounts Payable project,
    /// twice while being told not to: the site's Tipalti page was printed here, title and URL, beside
    /// the instruction. An instruction against a name the prompt shows is a weak instruction.
    /// </param>
    internal static string BuildOwnSiteCoverageBlock(GccCreateDto create, IReadOnlyList<string> unlistedTools)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        var all = research?.SiteQuoteables;
        if (all is not { Count: > 0 })
            return string.Empty;

        var names = GccCompetitorNames.Names(unlistedTools);
        var pages = all
            .Where(page => !GccCompetitorNames.Mentions(page.Url, names) && !GccCompetitorNames.Mentions(page.Title, names))
            .ToList();
        if (pages.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== ALREADY PUBLISHED ON THIS SITE (do not write these again) ===");
        sb.AppendLine("Pages this publisher has already published on this topic, retrieved from their");
        sb.AppendLine("own crawl. They are here so this piece adds something rather than repeating it:");
        sb.AppendLine("1. Do not restate what these already cover. Where the subject overlaps, go past");
        sb.AppendLine("   where they stop -- the reader who found this one may have read those.");
        sb.AppendLine("2. Reference them the way a writer references their own publication: name the");
        sb.AppendLine("   thing and carry on. Never reprint a passage.");
        sb.AppendLine("3. Never contradict them. Where they state this publisher's approach, figures or");
        sb.AppendLine("   offer, those are the ones that hold.");
        sb.AppendLine();

        foreach (var page in pages.Take(MaxCompetitorPagesInPrompt))
        {
            sb.AppendLine($"[{page.Title}] ({page.Url})");
            foreach (var h in page.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                sb.AppendLine($"- H{h.Level}: {GccCompetitorNames.Redact(h.Text, names, GccCompetitorNames.AnotherTool)}");
            foreach (var para in page.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                sb.AppendLine($"- {GccCompetitorNames.Redact(para, names, GccCompetitorNames.AnotherTool)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Retrieved competitor prose, under rules that are the opposite of the partner block's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The partner block tells the model to attribute every claim and carry the source URL. Applied
    /// to a rival that is exactly wrong: a page about our partner that links a competitor and cites
    /// them by name has advertised for them. So this block is read-only evidence — know what they
    /// claim, write something they have not, and never quote, cite or link them.
    /// </para>
    /// <para>
    /// Distinct from <see cref="BuildCompetitorHeadingBlock"/>, which carries heading *structure*
    /// for gap awareness. This carries what the rival actually says. Both are useful and neither
    /// substitutes for the other: an outline says a topic is covered, the prose says what the claim
    /// is, and you cannot be differentiated from a claim you were never shown.
    /// </para>
    /// </remarks>
    internal static string BuildCompetitorResearchBlock(GccCreateDto create)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        var pages = research?.CompetitorQuoteables;
        if (pages is not { Count: > 0 })
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== COMPETITOR RESEARCH (read it; never quote, cite or link it) ===");
        sb.AppendLine("Rival pages, retrieved from the crawl index. They are here so this piece can be");
        sb.AppendLine("different from them, and for nothing else. The rules are the opposite of the");
        sb.AppendLine("partner evidence above:");
        sb.AppendLine("1. Never quote a competitor, never name one as a recommended tool, and never");
        sb.AppendLine("   include a rival URL -- not as a citation, not as a link, not as a CTA.");
        sb.AppendLine("2. Use it to find what they have not said, or have said thinly, and say that");
        sb.AppendLine("   better. Covering what they cover, in their order, is the failure mode here.");
        sb.AppendLine("3. Never repeat a competitor's claim as this publisher's own. Their numbers are");
        sb.AppendLine("   theirs and unverified; a figure from here is not a figure you may write.");
        sb.AppendLine("4. These pages were retrieved because they rank, not because they are good.");
        sb.AppendLine("   Read them as what a reader has already seen, never as a standard to match.");
        sb.AppendLine();

        // Their identity stays out of the text the writer reads -- see GccCompetitorNames.
        var shown = pages.Take(MaxCompetitorPagesInPrompt).ToList();
        var names = GccCompetitorNames.FromUrls(shown.Select(p => p.Url));
        var n = 0;
        foreach (var page in shown)
        {
            n++;
            sb.AppendLine($"[Competitor page {n}: {GccCompetitorNames.Redact(page.Title ?? string.Empty, names)}]");
            foreach (var h in page.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                sb.AppendLine($"- H{h.Level}: {GccCompetitorNames.Redact(h.Text, names)}");
            foreach (var para in page.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                sb.AppendLine($"- {GccCompetitorNames.Redact(para, names)}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private const int MaxCompetitorPagesInPrompt = 5;
    private const int MaxCompetitorHeadingsPerPage = 25;

    /// <summary>
    /// Stage 2: competitor headings, resolved but never shown to the model that writes pillar/blog
    /// bodies (<see cref="GccCompetitorAnalysisResolver"/>'s own doc comment named this as its own
    /// deferred consumer). Rendered flat with level markers so the model can draw a genuine
    /// content-gap subsection from one, then tag it "competitor:&lt;exact heading text&gt;".
    /// </summary>
    private static string BuildCompetitorHeadingBlock(IReadOnlyList<GccCompetitorPageAnalysis> analyses)
    {
        if (analyses.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("=== COMPETITOR HEADING STRUCTURE (for content-gap awareness) ===");
        sb.AppendLine("Real heading outlines from indexed competitor pages. They show what a reader expects to");
        sb.AppendLine("find covered -- they do not show what to call it. Draw a subsection from one when it fills");
        sb.AppendLine("a real gap this article should cover, then write your own heading for it and tag it");
        sb.AppendLine("\"competitor:<exact heading text>\", the text only, not the \"(hN)\" level marker");
        sb.AppendLine("(see provenance rules). Reusing the cited heading as your own is rejected outright, and");
        sb.AppendLine("copying competitor prose is never acceptable.");
        sb.AppendLine("These pages were indexed because they rank, not because they are well written. Most are");
        sb.AppendLine("thin local SEO pages. Read them as a checklist of what a reader expects covered -- never");
        sb.AppendLine("as an example of how to cover it, and never as a standard to match.");
        sb.AppendLine();
        // The URL and the competitor's name stay out of the text the writer reads; the heading text is
        // redacted with the same names BuildProvenanceEvidence uses, so a tag written from what is
        // shown here still matches the lookup set. See GccCompetitorNames.
        var shown = analyses.Take(MaxCompetitorPagesInPrompt).ToList();
        var names = CompetitorNamesOf(analyses);
        var n = 0;
        foreach (var page in shown)
        {
            n++;
            sb.AppendLine($"[Competitor page {n}]");
            var flat = new List<(string Text, int Level)>();
            FlattenCompetitorHeadings(page.Headings, flat);
            // The level is a parenthetical, not a prefix -- "competitor:<exact heading text>" must
            // not have to guess whether "H2: " counts as part of the heading it's quoting.
            foreach (var h in flat.Take(MaxCompetitorHeadingsPerPage))
                sb.AppendLine($"- {GccCompetitorNames.Redact(h.Text, names)} (h{h.Level})");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The one set of names both the shown heading block and the provenance lookup redact with.</summary>
    private static IReadOnlyList<string> CompetitorNamesOf(IReadOnlyList<GccCompetitorPageAnalysis> analyses) =>
        GccCompetitorNames.FromUrls(analyses.Select(a => a.Url));

    /// <summary>Flattens a competitor page's heading tree to (text, level) pairs, depth-first. The
    /// one shared traversal for both the rendered prompt block above (which also shows the level)
    /// and <see cref="BuildProvenanceEvidence"/>'s lookup set (which only needs the bare text) --
    /// so the two can never see a different tree.</summary>
    private static void FlattenCompetitorHeadings(
        IReadOnlyList<GeekAPI.Services.GeekCrawler.SiteStructureNode> nodes,
        List<(string Text, int Level)> into)
    {
        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.HeadingText))
                into.Add((node.HeadingText.Trim(), node.Level));
            if (node.Children.Count > 0)
                FlattenCompetitorHeadings(node.Children, into);
        }
    }

    /// <summary>
    /// How many sections one call writes.
    ///
    /// <para>
    /// A single call cannot exceed the model's output ceiling, and prose in this contract costs
    /// roughly twice its own tokens -- every run carries four required fields, every section its
    /// tag, heading, href, children and provenance. So a long page written in one response is
    /// capped by arithmetic rather than by what it has to say: the blog stopped near 1,200 words of
    /// an 1,800 floor, and a tool page's own outline asks for 3,200-4,400 words against a ceiling
    /// that holds about 3,000.
    /// </para>
    ///
    /// <para>
    /// Two sections a call leaves the budget three to four times what a pair of sections needs, so
    /// a batch is never the thing that ends a section early. The cost is one extra call per pair,
    /// against drafts that currently fail their own floor.
    /// </para>
    /// </summary>
    internal const int SectionsPerBatch = 2;

    /// <summary>
    /// What one body call writes, measured: the 21 body calls of the 2026-10-07 run, two sections each, none
    /// cut off and none near its output limit, stopped on their own at 470 to 900 words, median 640.
    /// </summary>
    /// <remarks>
    /// A page's length is its number of body calls times this, whatever its prompts ask for. So a page reaches
    /// its floor by having enough sections, and no call may owe more than this: a floor above it is reported
    /// short on every page however the call is worded (<c>NoCallOwesMoreThanACallWritesTests</c>). Re-measure
    /// from a run's <c>batch</c> events before changing it.
    /// </remarks>
    internal const int MeasuredWordsPerBodyCall = 650;

    /// <summary>
    /// How many People Also Ask questions one FAQ call answers. The FAQ prompt's output budget is
    /// 3,072 tokens (<c>BuildArticleFaqSectionPrompt</c>); an h3 child with a two-to-four-sentence
    /// answer is 120-200 tokens of section JSON, so eight questions use about half of it. On
    /// 2026-10-08 a brief with 37 questions went to one call, the reply stopped at the limit, and
    /// the whole pillar was refused for an appendix.
    /// </summary>
    private const int PaaQuestionsPerFaqCall = 8;

    /// <summary>
    /// An FAQ section written from the operator's questions in calls of
    /// <see cref="PaaQuestionsPerFaqCall"/> and joined into one h2: every call returns the section
    /// with one h3 child per question, and the children are concatenated in the brief's order under
    /// the first call's heading. A call that answers none of its questions refuses the piece naming
    /// the call. The pillar's People Also Ask and the blog's FAQ both come through here;
    /// <paramref name="prompt"/> is the one difference.
    /// </summary>
    /// <remarks>
    /// A call that answers some of its questions ships what it answered, and each question it did not
    /// is named in <paramref name="gaps"/>, which the caller reports with the draft. Until 2026-10-10
    /// that case was silent: this summary said "a missing answer is a refusal, not a shorter FAQ" and
    /// the code refused only a call that answered nothing, so seven answers of eight shipped as an FAQ
    /// with no word that one was missing. An answer under a heading that is none of the call's
    /// questions is named too and stays on the page, as it always has: what ships is unchanged, only
    /// what is said about it.
    /// </remarks>
    private async Task<Section> WriteFaqInBatchesAsync(
        IContentGenerationProvider llm,
        Func<IReadOnlyList<string>, ChatCompletionRequest> prompt,
        IReadOnlyList<string> questions,
        string label,
        List<string> gaps,
        CancellationToken ct)
    {
        Section? head = null;
        var children = new List<Section>();
        for (var start = 0; start < questions.Count; start += PaaQuestionsPerFaqCall)
        {
            var batch = questions.Skip(start).Take(PaaQuestionsPerFaqCall).ToList();
            var call = start / PaaQuestionsPerFaqCall + 1;
            var result = await llm.CompleteAsync(prompt(batch), ct);
            var section = LlmResponseJsonParser.ParseSection(
                result.Content, "h2", $"{label}, questions {start + 1}-{start + batch.Count}");
            if (section.Children.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Refused: {label} call {call} answered none of its {batch.Count} questions.");
            }
            head ??= section;
            children.AddRange(section.Children);

            // Matched the way the tool page's FAQ matches (AnswersQuestion), so one definition decides
            // whether a question was answered on every page that has an FAQ.
            var answering = new HashSet<Section>(ReferenceEqualityComparer.Instance);
            foreach (var question in batch)
            {
                var answer = section.Children.FirstOrDefault(c => AnswersQuestion(c.Heading, question));
                if (answer is null)
                {
                    gaps.Add($"{label}: no answer came back under \"{question}\".");
                    continue;
                }
                answering.Add(answer);
            }
            foreach (var stray in section.Children.Where(c => !answering.Contains(c)))
            {
                gaps.Add($"{label}: the writer answered under \"{stray.Heading}\", which is not a question it was sent.");
            }
        }

        if (head is null)
        {
            throw new InvalidOperationException($"Refused: {label} has no questions to answer.");
        }
        return head with { Children = children };
    }

    /// <summary>
    /// Whether an FAQ child answers one of the operator's questions: the same words ignoring case,
    /// punctuation and a trailing question mark, or one wording containing the other -- the prompt
    /// lets the heading be "lightly tightened".
    /// </summary>
    internal static bool AnswersQuestion(string heading, string question)
    {
        var h = NormalizeQuestion(heading);
        var q = NormalizeQuestion(question);
        if (h.Length == 0 || q.Length == 0) return false;
        return h == q || h.Contains(q, StringComparison.Ordinal) || q.Contains(h, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question as its words: lower case, letters and digits, one space between words.
    /// </summary>
    /// <remarks>
    /// Any run of whitespace is one space. Until 2026-10-10 only the space character was kept and every
    /// other character was deleted, so a tab or a non-breaking space between two words -- what a
    /// question pasted from a web page carries -- joined them into one, and two spaces stayed two. A
    /// heading the writer returned with an ordinary space then matched nothing.
    /// </remarks>
    internal static string NormalizeQuestion(string text)
    {
        var words = new StringBuilder();
        var betweenWords = false;
        foreach (var ch in (text ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch))
            {
                betweenWords = words.Length > 0;
                continue;
            }
            if (!char.IsLetterOrDigit(ch)) continue;
            if (betweenWords)
            {
                words.Append(' ');
                betweenWords = false;
            }
            words.Append(ch);
        }

        return words.ToString();
    }

    /// <summary>
    /// The body, written in batches of <see cref="SectionsPerBatch"/> and concatenated.
    ///
    /// <para>
    /// Each call is told which sections it owns and which the other calls own, so a batch neither
    /// re-covers its neighbours nor writes a conclusion for a page it cannot see continuing. The
    /// outline is the plan either way -- batching changes how many responses build it, never what
    /// it contains.
    /// </para>
    ///
    /// <para>
    /// A batch that returns nothing is a failure, not a short page: continuing would store a draft
    /// missing whole sections of its own plan and call it finished.
    /// </para>
    /// </summary>
    /// <summary>
    /// One written draft, what it is short of, and the calls that came back under their own word floor.
    /// </summary>
    /// <param name="Shortfalls">Reported with the draft whatever else is true of it.</param>
    /// <param name="CallsUnderFloor">Reported only when the finished page is under its floor
    /// (<see cref="Guardrail.GccDraftGuard.PageLengthCheck"/>): a call's floor is its share of the
    /// page's, and a page that reached its floor has no call to answer for.</param>
    internal sealed record GccDraft(
        ContentDocument Document, IReadOnlyList<string> Shortfalls, IReadOnlyList<string> CallsUnderFloor);

    /// <summary>
    /// Write a draft, guard it once, and refuse or ship with its gaps reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A draft that fails a check is refused on that attempt. Nothing is sent to the model a second
    /// time, and there is no second draft to prefer (Jeff, 2026-10-06: <i>"NO RETRIES ... So I paid
    /// twice for nothing"</i>). The verdict and the draft it judged are on the run's record, so the
    /// refusal can be read afterwards.
    /// </para>
    /// <para>
    /// Refusals throw with the "Refused:" prefix GenerateAsync answers as a 400. Gaps -- a partner
    /// never named, a closing never linked, a page short of its floor -- are the draft's warnings:
    /// saved, recorded with the version and pushed to the workspace by name.
    /// </para>
    /// <para>
    /// A call under its own word floor is a warning only when the page is under its floor, and is then
    /// listed straight after the page's line. Every call's words are on the run's record either way
    /// (the <c>batch</c> event, and <c>callsUnderFloor</c> on the verdict).
    /// </para>
    /// </remarks>
    private async Task<(ContentDocument Document, List<string> Warnings)> GuardedDraftAsync(
        string label,
        Func<Task<GccDraft>> write,
        Func<ContentDocument, Guardrail.GccGuardVerdict> guard,
        string? keyword = null)
    {
        var draft = await write();
        var verdict = guard(draft.Document);
        // The page's keyword count as its score will count it, on every page, passed or not.
        var counted = string.IsNullOrWhiteSpace(keyword)
            ? null
            : Gcw.GcwSeoAnalyzer.CountKeyword(JsonSerializer.Serialize(draft.Document, CwDocumentJson), keyword);
        // The verdict and the draft it judged, on the run's record -- a refused draft is otherwise text
        // nobody can read afterwards.
        await GccRunLog.RecordIfAnyAsync("verdict", new
        {
            label,
            clean = verdict.Clean,
            words = ContentDocumentText.CountWords(draft.Document),
            keywordUses = counted?.Uses,
            keywordDensity = counted?.DensityPercent,
            findings = verdict.Findings.Select(f => new { f.Check, f.Detail, f.Refuses }).ToList(),
            shortfalls = draft.Shortfalls,
            callsUnderFloor = draft.CallsUnderFloor,
            document = draft.Document,
        });

        if (verdict.Refusals.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refused: {label}. " + string.Join(" ", verdict.Refusals.Select(f => f.Detail)));
        }

        var warnings = new List<string>();
        foreach (var gap in verdict.Gaps)
        {
            warnings.Add(gap.Detail);
            if (gap.Check == Guardrail.GccDraftGuard.PageLengthCheck) warnings.AddRange(draft.CallsUnderFloor);
        }

        warnings.AddRange(draft.Shortfalls);
        foreach (var warning in warnings)
        {
            _logger.LogWarning("{Label} ships with: {Warning}", label, warning);
        }

        return (draft.Document, warnings);
    }

    /// <summary>
    /// What every guard on this create checks a draft against, built from what the writer was shown.
    /// </summary>
    /// <param name="evidenceText">The evidence block the body was written from. The brief, topic and notes
    /// are added to it for the figure check -- the operator's own statements -- and nothing else: the
    /// generation context and the outline carry instruction numbers (word floors, "600-850 words"), and
    /// licensing a figure because the prompt said it licenses nothing (review, 2026-10-04).</param>
    /// <summary>
    /// The tools a pillar or blog names: which it must name, which it links and where, and which it
    /// must not name at all.
    /// </summary>
    /// <param name="Required">The partner tools the piece is obliged to name.</param>
    /// <param name="Linked">The declared partners, each with its tool page's path -- the only tool
    /// links the piece may carry.</param>
    /// <param name="Unlisted">Tools the publisher's own site lists under this keyword that are not this
    /// project's partners. The writer can see them in the site's own prose; it is told not to use them,
    /// and the guard refuses a draft that does.</param>
    private sealed record PartnerTools(
        IReadOnlyList<string> Required,
        IReadOnlyList<KnownCrawlTool> Linked,
        IReadOnlyList<string> Unlisted)
    {
        /// <summary>What the writer is told: the names it must use, then the names it must not.</summary>
        public string? Instruction
        {
            get
            {
                var blocks = new[]
                {
                    GccRequiredToolMentions.Instruction(Required),
                    GccRequiredToolMentions.UnlistedInstruction(Unlisted),
                }.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
                return blocks.Count == 0 ? null : string.Join(Environment.NewLine, blocks);
            }
        }
    }

    /// <remarks>
    /// The site's own tool list is still read -- not to hand to the writer, which is how Melio and
    /// Plooto reached a pillar whose project did not list them, but to know which names to keep out.
    /// </remarks>
    /// <summary>
    /// The project's declared partners, each with the path its tool page is published at -- the only
    /// tool links a pillar or a blog may carry. For the run to check a piece's links against the tool
    /// pages the project actually has.
    /// </summary>
    public async Task<IReadOnlyList<GccPartnerToolPage>> PartnerToolPagesAsync(GccCreateDto create, CancellationToken ct) =>
        GccPartnerToolPages.For(create, await PartnerUrlsForAsync(create, ct), _company.ToolBaseUrl);

    private async Task<PartnerTools> PartnerToolsAsync(GccCreateDto create, CancellationToken ct)
    {
        var partnerUrls = await PartnerUrlsForAsync(create, ct);
        var required = GccRequiredToolMentions.For(create.BriefJson, partnerUrls);
        var linked = GccPartnerToolPages.For(create, partnerUrls, _company.ToolBaseUrl)
            .Select(page => new KnownCrawlTool(page.ProductName, Href: null, PublicPath: page.Path))
            .ToList();
        var unlisted = (await _knownTools.ResolveAsync(create, ct))
            .Select(tool => tool.Name)
            .Where(name => !required.Any(partner => GccRequiredToolMentions.SameProduct(partner, name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PartnerTools(required, linked, unlisted);
    }

    /// <summary>
    /// A call's evidence with the page's non-dollar amounts named after it, or the evidence as it was
    /// when the page has none.
    /// </summary>
    /// <remarks>
    /// Added to what the writer is shown and never to what the checks read: the note repeats the
    /// amounts, and evidence that repeated them on one line would be read as stating them.
    /// </remarks>
    /// <summary>The research half with the publisher's positions after it, for the opening of a pillar or blog.</summary>
    private static string WithPublisherPositions(string research, GccCreateDto create, IReadOnlyList<string> unlistedTools)
    {
        var positions = BuildPublisherPositionsBlock(create, unlistedTools);
        if (positions.Length == 0) return research;
        return research.Length == 0 ? positions : $"{research}{Environment.NewLine}{positions}";
    }

    private static string? WithForeignAmountsNamed(string? evidence, string? foreignAmountsNote) =>
        foreignAmountsNote is null ? evidence
        : string.IsNullOrWhiteSpace(evidence) ? foreignAmountsNote
        : $"{evidence}{Environment.NewLine}{foreignAmountsNote}";

    /// <summary>
    /// The body with each partner tool's page on the first mention of its name, and what was linked on
    /// the run's record. The writer links nothing (Jeff, 2026-10-10), so there is nothing here to
    /// refuse: a tool the body never names is a reported gap (<c>partner-mentions</c>), not a failure.
    /// Runs before the closing and the FAQ are added, so neither can carry a tool link.
    /// </summary>
    private static async Task<List<Section>> LinkToolsAsync(
        string label, IReadOnlyList<Section> sections, IReadOnlyList<KnownCrawlTool> tools)
    {
        var linked = Guardrail.GccToolLinker.Link(sections, tools);
        await GccRunLog.RecordIfAnyAsync("links", new
        {
            label,
            links = linked.Links.Select(l => new { l.Tool, l.Href, l.Heading, l.Words }).ToList(),
            notLinked = linked.NotLinked,
        });
        return linked.Sections;
    }

    /// <summary>
    /// The opening and the body with the keyword's shortenings put back where the grammar allows it
    /// (<see cref="Guardrail.GccKeywordRemap"/>), and every edit on the run's record.
    /// </summary>
    /// <remarks>
    /// Before the linker, the closing and the FAQ: the text it reads is the writer's, whole paragraphs
    /// in plain runs, and what code builds afterwards is never remapped. Nothing here refuses; a page
    /// still under its score's floor afterwards is reported by the page guard, with the sections that
    /// never use the phrase. The record carries each section's share, uses, places and edits, so a
    /// page that stopped short of its target shows where it ran out of places.
    /// </remarks>
    private static async Task<(Section Lede, List<Section> Sections)> RemapKeywordAsync(
        string label, Section lede, IReadOnlyList<Section> sections, string? keyword)
    {
        var remapped = Guardrail.GccKeywordRemap.Apply(new ContentDocument(lede, sections), keyword);
        await GccRunLog.RecordIfAnyAsync("keyword", new
        {
            label,
            keyword,
            target = remapped.Target,
            before = remapped.Before,
            after = remapped.After,
            edits = remapped.Edits.Select(e => new { e.Heading, e.From, e.To }).ToList(),
            // The opening first. A section with no place is one whose share went to the others; a
            // page still short of its target after this has no place left anywhere.
            sections = remapped.Sections.Select(u => new { u.Heading, u.Share, u.Before, u.Places, u.Edits }).ToList(),
        });
        return (remapped.Document.Lede, [.. remapped.Document.Sections]);
    }

    private Guardrail.GccGuardInputs GuardInputsFor(
        GccCreateDto? create,
        ProjectGenerationContext context,
        string contentType,
        GccHeadingProvenanceEvidence? provenance,
        IReadOnlyList<string> requiredTools,
        string? evidenceText,
        IReadOnlyList<GccQuoteCandidate>? quoteCandidates = null,
        int appendedSections = 0,
        string? extractionJson = null,
        PartnerTools? partnerTools = null)
    {
        var research = create is null ? null : GccResearchFetchService.Deserialize(create.ResearchJson);
        var allowedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in (research?.Quoteables ?? []).Concat(research?.SiteQuoteables ?? []))
        {
            if (!string.IsNullOrWhiteSpace(page.Url)) allowedUrls.Add(page.Url.Trim());
        }

        var publisherHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in new[] { _company.ArticleBaseUrl, _company.BlogBaseUrl, _company.ToolBaseUrl, context.ProjectUrl }
                     .Concat((research?.SiteQuoteables ?? []).Select(p => p.Url)))
        {
            var host = GccRequiredToolMentions.HostKeyOf(url);
            if (host.Length > 0) publisherHosts.Add(host);
        }

        // The extraction and the brief as the text in them, a value to a line -- see GccJsonEvidence
        // for what reading the serialized string did to the currency check.
        var numberEvidence = string.Join(
            Environment.NewLine,
            evidenceText ?? string.Empty,
            Guardrail.GccJsonEvidence.TextOf(extractionJson),
            Guardrail.GccJsonEvidence.TextOf(create?.BriefJson),
            create?.Topic ?? string.Empty,
            create?.Notes ?? string.Empty);

        return new Guardrail.GccGuardInputs(
            provenance,
            requiredTools,
            context.ConsultationAnchorHref,
            allowedUrls,
            publisherHosts,
            numberEvidence,
            quoteCandidates,
            appendedSections,
            // Under the tool base, a link goes to a tool page the writer was handed, or nowhere. A tool
            // page is handed none, so it carries no link to another tool page.
            ToolBasePath: GccContentPath.PathOf(_company.ToolBaseUrl),
            ToolPaths: (partnerTools?.Linked ?? [])
                .Select(tool => tool.PublicPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            UnlistedTools: partnerTools?.Unlisted ?? [],
            // The keyword the page is scored on: the one its prompts name and the SEO report counts.
            Keyword: context.TargetKeyword,
            // The floor the page is scored on: the SEO report's own, for this type.
            PageFloorWords: PageFloorFor(contentType));
    }

    /// <summary>The words the SEO report holds a page of this type to, or null for a type it holds to none.</summary>
    private static int? PageFloorFor(string contentType)
    {
        var (floor, _, applies) = ContentTypes.GccLongFormTypes.GetSeoLengthRules(contentType);
        return applies && floor > 0 ? floor : null;
    }

    /// <summary>
    /// The document with its per-section image prompts, or the document as it was and a warning when
    /// they could not be written.
    /// </summary>
    /// <remarks>
    /// An image-prompt failure discarded a draft that had passed every guard, with the money spent on
    /// it. The prompts are an aid for whoever makes the images, not part of the page; the draft is the
    /// known-good output. Saved and reported, the way the partner gap and the scheduler link already are.
    /// </remarks>
    private async Task<ContentDocument> WithSectionImagePromptsAsync(
        string contentType,
        string title,
        ContentDocument document,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            var withPrompts = await GenerateSectionImagePromptsAsync(
                contentType, title, JsonSerializer.Serialize(document, CwDocumentJson), section, provider, ct);
            return JsonSerializer.Deserialize<ContentDocument>(withPrompts, CwDocumentJson) ?? document;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image prompts failed for {ContentType} '{Title}'; the draft is saved without them.", contentType, title);
            warnings.Add(
                $"Image prompts were not written ({ex.Message}). The draft is saved without them; "
                + "regenerate them before the images are made.");
            return document;
        }
    }

    private async Task<List<Section>> GenerateSectionsInBatchesAsync(
        IContentGenerationProvider llm,
        ContentTypes.IContentTypePrompts type,
        ContentTypes.ContentTypePromptContext promptCtx,
        IReadOnlyList<SectionSlot> outline,
        string label,
        CancellationToken ct,
        List<string>? callsUnderFloor = null)
    {
        var written = new List<Section>();
        // The keyword the page is scored against: the one its prompts name and the SEO report counts.
        var keyword = promptCtx.Context.TargetKeyword;
        for (var i = 0; i < outline.Count; i += SectionsPerBatch)
        {
            var batch = outline.Skip(i).Take(SectionsPerBatch).ToList();
            var batchLabel = $"{label} sections {i + 1}-{i + batch.Count}";
            // What the calls before this one wrote, so this one does not make their points again. Each
            // call sees the outline and the opening; until 2026-10-10 none saw another call's text.
            var batchCtx = promptCtx with
            {
                SectionBatch = batch,
                SectionBatchIndex = i / SectionsPerBatch,
                WrittenSoFar = written.Count == 0 ? null : [.. written],
            };
            var sections = await WriteBatchAsync(llm, type, batchCtx, batchLabel, outline.Count, ct);

            // What the batch owes of its page, measured the moment it comes back: its words.
            //
            // The tool outline sizes every section ("600-850 words"), and a batch under its floor has
            // not written its share -- 2,108 words against 3,000, 2026-10-03. Nothing is written again
            // (Jeff, 2026-10-06: no retries). The shortfall is reported only if the page turns out short
            // (GuardedDraftAsync): a call's floor is its share of the page's, and the run of 2026-10-10
            // listed 17 calls under theirs on seven pages that were all over their own.
            //
            // Neither the keyword's count nor its heading is a batch's to owe. The count: Jeff,
            // 2026-10-10, "Do not rely on the LLM to count its own keyword usage" -- the writer writes,
            // GccKeywordRemap puts the exact phrase back where the writer shortened it, and the page is
            // judged by its own score, once. The heading: one call is asked for it, on the section its
            // outline names (SectionSlot.BatchOwnsKeywordHeading, the rule every body prompt hands
            // SeoBodyInstruction), and whether the page has one is judged on the finished page by
            // GccDraftGuard. Held to the call, the pillar of 2026-10-10 was reported for a first call
            // with no keyword heading while eight of its later headings carried the phrase.
            //
            // What the call used and wrote is on the record here, so the writer's own rate, and whether
            // the call that was asked for the heading wrote it, are readable call by call.
            var underFloor = BatchUnderFloor(sections, batch, batchLabel);
            await GccRunLog.RecordIfAnyAsync("batch", new
            {
                batch = batchLabel,
                // Which call of how many, and what it was held to: a drop in yield is then readable call by call.
                call = i / SectionsPerBatch + 1,
                of = (outline.Count + SectionsPerBatch - 1) / SectionsPerBatch,
                floor = BatchFloorWords(batch),
                words = ContentDocumentText.CountWords(sections),
                keywordUses = KeywordUses(sections, keyword),
                askedForKeywordHeading = SectionSlot.BatchOwnsKeywordHeading(batch, outline, i / SectionsPerBatch),
                headings = sections.Select(x => x.Heading).ToList(),
                shortfalls = underFloor is null ? new List<string>() : [underFloor],
            });
            if (underFloor is not null)
            {
                // Held for the page's own verdict: listed beside the page's length line, or not at all.
                _logger.LogInformation("{Shortfall}.", underFloor);
                callsUnderFloor?.Add($"{underFloor}.");
            }

            written.AddRange(sections);
        }

        return written;
    }

    private static async Task<List<Section>> WriteBatchAsync(
        IContentGenerationProvider llm,
        ContentTypes.IContentTypePrompts type,
        ContentTypes.ContentTypePromptContext batchCtx,
        string batchLabel,
        int outlineCount,
        CancellationToken ct)
    {
        var result = await llm.CompleteAsync(type.Body(batchCtx), ct);
        var sections = LlmResponseJsonParser.ParseSections(result.Content, batchLabel).ToList();

        if (sections.Count == 0)
        {
            throw new InvalidOperationException(
                $"{batchLabel} of {outlineCount} came back empty. "
                + "The draft is not saved -- a page missing part of its own plan is not a short "
                + "page, it is an incomplete one.");
        }

        return sections;
    }

    /// <summary>
    /// A metadata draft with every required field present, or a refusal naming the one that is not.
    /// </summary>
    /// <remarks>
    /// The metadata prompts carry no JSON schema, and System.Text.Json leaves a non-nullable record
    /// member null when the response omits it. So a response without "metaDescription" reached
    /// <c>.MetaDescription.Length</c> and the job failed with "NullReferenceException: Object
    /// reference not set" -- pushed to the operator with no field named. Required means declared
    /// non-nullable on the record; <c>Summary</c> and the like are <c>string?</c> and may be absent.
    /// </remarks>
    internal static T RequireCompleteMetadata<T>(T draft, string label) where T : class
    {
        var nullability = new System.Reflection.NullabilityInfoContext();
        foreach (var property in typeof(T).GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
            if (nullability.Create(property).ReadState != System.Reflection.NullabilityState.NotNull) continue;

            var value = property.GetValue(draft);
            var missing = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
            if (missing)
            {
                throw new InvalidOperationException(
                    $"{label} came back without \"{char.ToLowerInvariant(property.Name[0])}{property.Name[1..]}\". "
                    + "The draft is not saved: a page whose metadata is missing a required field is not finished.");
            }
        }

        return draft;
    }

    /// <summary>The leading figure of a slot's depth ("600-850 words" is 600), or 0 when it has none.</summary>
    private static readonly Regex SlotDepthLowerBound = new(@"^\s*(\d[\d,]*)", RegexOptions.Compiled);

    /// <summary>
    /// The words a batch owes: what each of its slots owes, summed. A slot owes its share of the page's
    /// floor when it carries one (<see cref="SectionSlot.OwedWords"/>), and otherwise its depth's lower
    /// figure. Zero when any slot in the batch carries neither, because a floor derived from half the slots
    /// would be a guess about the other half.
    /// </summary>
    internal static int BatchFloorWords(IReadOnlyList<SectionSlot> batch)
    {
        var total = 0;
        foreach (var slot in batch)
        {
            if (slot.OwedWords is { } owed)
            {
                if (owed <= 0) return 0;
                total += owed;
                continue;
            }

            if (slot.Depth is not { Length: > 0 } depth) return 0;
            var m = SlotDepthLowerBound.Match(depth);
            if (!m.Success || !int.TryParse(m.Groups[1].Value.Replace(",", ""), out var lower) || lower <= 0) return 0;
            total += lower;
        }

        return total;
    }

    /// <summary>
    /// What a call is short of the word floor its slots declare, as the start of a sentence the
    /// operator reads; null when it reached its floor or its slots carry none.
    /// </summary>
    /// <remarks>
    /// The page's to answer for: listed only when the finished page is under its own floor
    /// (<see cref="GuardedDraftAsync"/>). The keyword's count and its heading are not a call's to owe
    /// either; both are judged once, on the finished page, in <see cref="Guardrail.GccDraftGuard"/>.
    /// </remarks>
    internal static string? BatchUnderFloor(
        IReadOnlyList<Section> sections, IReadOnlyList<SectionSlot> batch, string batchLabel)
    {
        var floor = BatchFloorWords(batch);
        var words = ContentDocumentText.CountWords(sections);
        return floor > 0 && words < floor
            ? $"{batchLabel} is {words:N0} words against a {floor:N0}-word floor"
            : null;
    }

    /// <summary>
    /// How many times a batch uses the exact phrase, as the scorer counts it. For the run's record,
    /// where the writer's own rate is read call by call; nothing is owed.
    /// </summary>
    internal static int KeywordUses(IReadOnlyList<Section> sections, string? keyword) =>
        string.IsNullOrWhiteSpace(keyword)
            ? 0
            : Gcw.GcwSeoAnalyzer.CountPhraseOccurrences(ReadAsScorer(sections).PlainText, keyword.Trim());

    /// <summary>The sections as the scorer reads them: the same text, headings and match the SEO report uses.</summary>
    private static Gcw.GcwBodyDocument.Text ReadAsScorer(IReadOnlyList<Section> sections) =>
        Gcw.GcwBodyDocument.Read(JsonSerializer.Serialize(
            new ContentDocument(new Section("h2", string.Empty, [], null, []), sections), CwDocumentJson));

    /// <summary>
    /// Stage 2: the concrete evidence set this specific generation call had available -- the same
    /// research/brief/PAA/competitor material rendered into the prompt, reduced to lookup sets so
    /// <see cref="GccHeadingProvenanceGuard"/> can check the model's tags against exactly what it
    /// was shown, never a broader or narrower set.
    /// </summary>
    private static GccHeadingProvenanceEvidence BuildProvenanceEvidence(
        GccCreateDto create,
        IReadOnlyList<GccCompetitorPageAnalysis> competitorAnalyses,
        string? mustMentionBlock,
        IReadOnlyList<string>? partnerUrls = null)
    {
        var brief = ExtractBriefFields(create.BriefJson);
        var populatedBriefFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddIfPresent(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) populatedBriefFields.Add(name);
        }
        void AddIfAny(string name, IReadOnlyList<string>? values)
        {
            if (values is { Count: > 0 }) populatedBriefFields.Add(name);
        }
        AddIfPresent("segment", brief.Segment);
        AddIfAny("details", brief.Details);
        AddIfPresent("notes", brief.Notes);
        AddIfPresent("angle", brief.Angle);
        AddIfPresent("primaryIntent", brief.PrimaryIntent);
        AddIfPresent("secondaryIntent", brief.SecondaryIntent);
        AddIfPresent("buyingStage", brief.BuyingStage);
        AddIfPresent("toneOfVoice", brief.ToneOfVoice);
        AddIfAny("eeatSignals", brief.EeatSignals);
        AddIfPresent("lengthBand", brief.LengthBand);

        var paaQuestions = new HashSet<string>(
            (brief.PaaQuestions ?? []).Select(q => q.Trim()), StringComparer.OrdinalIgnoreCase);

        // Redacted exactly as BuildCompetitorHeadingBlock shows them, so a "competitor:<heading>" tag
        // written from the shown text matches here.
        var competitorNames = CompetitorNamesOf(competitorAnalyses);
        var competitorHeadings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in competitorAnalyses)
        {
            var flat = new List<(string Text, int Level)>();
            FlattenCompetitorHeadings(page.Headings, flat);
            foreach (var h in flat)
                competitorHeadings.Add(GccCompetitorNames.Redact(h.Text, competitorNames));
        }

        var siteSubtopics = new HashSet<string>(
            GccMustMention.Subtopics(mustMentionBlock), StringComparer.OrdinalIgnoreCase);

        // What the Library actually retrieved. Four identifiers per passage, because a heading may
        // legitimately be about the partner, about the section the passage sat under, or about the
        // page -- and the model should not have to guess which spelling the guard will accept.
        //
        // The partner name comes from GccRequiredToolMentions rather than from the host, because a
        // host cannot know that "zoneandco" is written "Zone & Co". The brief's own tool rows decide
        // the spelling, which is the same precedence the required-mentions block and the retrieved
        // chunk labels already use -- so a heading tagged with the name the prompt asked for is the
        // name the guard licenses.
        var retrievedEvidence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retrievedResearch = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (retrievedResearch?.Quoteables is { Count: > 0 } quoteables)
        {
            // partnerUrls as well as the brief, which GccGroundingResolver:219 already passes when
            // it labels retrieved chunks with the same lookup. Omitting them here licensed a
            // narrower set of partner spellings than the prompt uses: a partner declared on the
            // project but absent from the brief's tool rows was labelled on its chunks and then
            // refused when a heading cited it -- "evidence:Lightyear does not resolve to any
            // available source" (2026-10-01).
            var toolNames = GccRequiredToolMentions.AnchorLookup(create.BriefJson, partnerUrls);
            foreach (var page in quoteables)
            {
                if (!string.IsNullOrWhiteSpace(page.SectionTitle))
                    retrievedEvidence.Add(page.SectionTitle.Trim());
                if (!string.IsNullOrWhiteSpace(page.Title))
                    retrievedEvidence.Add(page.Title.Trim());

                var host = HostOfQuoteable(page.Url);
                if (host.Length == 0) continue;
                retrievedEvidence.Add(host);
                if (toolNames.TryGetValue(host, out var partnerName) && partnerName.Length > 0)
                    retrievedEvidence.Add(partnerName);
            }
        }

        return new GccHeadingProvenanceEvidence(
            populatedBriefFields, paaQuestions, competitorHeadings, siteSubtopics, retrievedEvidence);
    }

    /// <summary>
    /// The registrable host of a quoteable's URL, lowercased and without a leading "www.". Empty
    /// when the value is not an absolute http(s) URL, which is the only form a retrieved or fetched
    /// passage carries.
    /// </summary>
    /// <summary>
    /// The registrable host of a quoteable's URL. Delegates, rather than repeating the rule: this was a
    /// second copy of <see cref="GccRequiredToolMentions.HostKeyOf"/>, and a page bucketed by a key that
    /// drifts from the one the partner lookup was built with belongs to no partner.
    /// </summary>
    private static string HostOfQuoteable(string? url) => GccRequiredToolMentions.HostKeyOf(url);

    private sealed record SectionImagePrompt(string Section, string Prompt);
    private sealed record SectionImagePromptsResponse(List<SectionImagePrompt>? Prompts);

    /// <summary>
    /// Generates one image prompt per top-level section (plus a hero for the lede) and merges
    /// them into the document's own <see cref="Section.ImagePrompt"/> fields, returning the
    /// updated body JSON. Previously computed a real, paid LLM response here and then discarded
    /// it -- every caller took the return value, generated a section-image-prompts call, and
    /// never used the result. Every long-form generation was paying for image prompts nobody
    /// ever saw.
    /// </summary>
    public async Task<string> GenerateSectionImagePromptsAsync(
        string contentType,
        string title,
        string body,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var sections = ExtractSectionHeadings(body);

        if (sections.Count == 0)
            throw new InvalidOperationException("No sections found in content body.");

        var system = new StringBuilder()
            .AppendLine("You write AI image-generation prompts for B2B article figures.")
            .AppendLine("CRITICAL: Return EXACTLY ONE prompt for EACH listed section, in the exact order listed.")
            .AppendLine()
            .AppendLine("VISUAL STYLE:")
            .AppendLine("- Flat vector / infographic, professional B2B tech aesthetic.")
            .AppendLine("- Default size: 1200x630. Style: professional illustration.")
            .AppendLine("- NO readable text, logos, or watermarks in the image.")
            .AppendLine($"- Hero image for '{title}': establishing-shot composition, evokes the title's theme.")
            .AppendLine("- Section images: teaching diagrams appropriate to the section topic.")
            .AppendLine()
            .AppendLine("Respond with ONLY a single valid JSON object:")
            .AppendLine("{\"prompts\": [{\"section\": string, \"prompt\": string}]}")
            .ToString();

        var user = new StringBuilder()
            .AppendLine($"Article title: {title}")
            .AppendLine($"Content type: {contentType}")
            .AppendLine()
            .AppendLine("Sections requiring image prompts:");

        user.AppendLine($"- Hero: {title}");
        for (int i = 0; i < sections.Count; i++)
        {
            user.AppendLine($"- Section {i + 1}: {sections[i]}");
        }

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.7,
            MaxOutputTokens: 2048);

        var result = await llm.CompleteAsync(request, ct);

        var parsed = LlmResponseJsonParser.Parse<SectionImagePromptsResponse>(
            result.Content ?? string.Empty,
            $"the {contentType} page",
            "set of section image prompts",
            static r => r.Prompts is { Count: > 0 } ? null : "the reply carried no prompts");
        var prompts = parsed.Prompts ?? [];

        // One per section is the whole contract -- the system prompt says "EXACTLY ONE prompt for
        // EACH listed section" and they are assigned positionally below. A short list used to be
        // absorbed by the bounds check on that assignment: ask for six, get one, and the hero kept
        // its prompt while five H2s silently kept none, with a paid call behind it and nothing
        // reported. Same shape as the extraction swallow that hid a total outage for two hours.
        //
        // Expected is the listed sections plus the hero at index 0.
        var expectedPrompts = sections.Count + 1;
        if (prompts.Count < expectedPrompts)
            throw LlmResponseJsonParser.UnusableReply(
                $"Image prompts for {contentType}: expected {expectedPrompts} (one hero plus one per "
                + $"H2), received {prompts.Count}. Not attaching a partial set.");

        var document = JsonSerializer.Deserialize<ContentDocument>(body, CwDocumentJson)
            ?? throw new InvalidOperationException("Could not re-read the generated body to attach image prompts.");

        // Positional, matching how the request was built: index 0 is always "Hero" (the lede),
        // index 1.. line up with `sections` (ExtractSectionHeadings' order) one for one -- the
        // system prompt requires "EXACTLY ONE prompt for EACH listed section, in the exact order
        // listed", so trusting position over re-matching by name text is the reliable read.
        var lede = document.Lede;
        if (prompts.Count > 0 && !string.IsNullOrWhiteSpace(prompts[0].Prompt))
            lede = lede with { ImagePrompt = prompts[0].Prompt };

        var updatedSections = new List<Section>(document.Sections.Count);
        for (var i = 0; i < document.Sections.Count; i++)
        {
            var promptIndex = i + 1; // offset by the Hero entry at index 0
            var s = document.Sections[i];
            updatedSections.Add(
                promptIndex < prompts.Count && !string.IsNullOrWhiteSpace(prompts[promptIndex].Prompt)
                    ? s with { ImagePrompt = prompts[promptIndex].Prompt }
                    : s);
        }

        var updated = document with { Lede = lede, Sections = updatedSections };
        return JsonSerializer.Serialize(updated, CwDocumentJson);
    }

    /// <summary>
    /// The H2 headings of a generated body. The body is a <see cref="ContentDocument"/>, so the
    /// headings are read off the tree — no parse, no regex, nothing to guess. This replaced a
    /// string scan that only ever worked while the body happened to be a string.
    /// </summary>
    private static List<string> ExtractSectionHeadings(string bodyJson)
    {
        var document = JsonSerializer.Deserialize<ContentDocument>(bodyJson, CwDocumentJson);
        return document is null ? [] : [.. ContentDocumentText.TopLevelHeadings(document)];
    }
}
