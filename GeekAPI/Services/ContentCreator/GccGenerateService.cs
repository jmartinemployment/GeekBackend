using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

using GeekAPI.Services.ContentCreatorV2;

using GeekAPI.HttpClients;
namespace GeekAPI.Services.ContentCreator;

using RelatedPageDto = GeekAPI.Services.ContentCreatorV2.RelatedPageDto;
using SiteSectionContextDto = GeekAPI.Services.ContentCreatorV2.SiteSectionContextDto;
using ContentGapDto = GeekAPI.Services.ContentCreatorV2.ContentGapDto;
using SiteAnalysisStoredPayload = GeekAPI.Services.ContentCreatorV2.SiteAnalysisStoredPayload;

public sealed record SiteAnalysisDto(Guid Id, string Domain, string Status);

/// <summary>
/// Content Creator generation helpers. Source of truth = Content Writer v2 only
/// (prompt builders + LLM providers). Do not call Content Writer v3 generators.
/// </summary>
public class GccGenerateService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>CWV2 ContentDocument wire format (Paragraph discriminator uses "type").</summary>
    private static readonly JsonSerializerOptions CwDocumentJson = CreateCwDocumentJson();

    private static JsonSerializerOptions CreateCwDocumentJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly IContentPromptBuilder _prompts;
    private readonly GeekAPI.Services.ContentCreator.ContentTypes.IContentTypePromptRegistry _types;
    private readonly IContentProviderFactory _cwProviders;
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly IBlogPostingSchemaBuilder _blogSchema;
    private readonly IArticleSchemaBuilder _articleSchema;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccGenerateService> _logger;
    private readonly GccCompetitorAnalysisResolver _competitorAnalysis;
    private readonly GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService _partnerExtraction;
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
        GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService partnerExtraction,
        IGccProjectReader projects,
        GccPublisherProfileResolver publisherProfile,
        GccKnownToolsResolver knownTools)
    {
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
        GccV2SiteSection.ParseSiteSection(json);

    /// <summary>
    /// Required gate: every Generate must have a crawl id (site_analysis_profiles.Id).
    /// Domain-only grounding (crawl id with no section) is allowed — Generate uses
    /// page-section trees for "must mention" injection. Handoff-created sections must have
    /// non-empty relatedPages. Applies to all types including imagePrompt/aiTool (no exemption);
    /// per-H2 image prompts must include siteSection+tree with at least one top-level section.
    /// </summary>
    public static void ValidateSiteSectionGate(Guid? projectSiteRunId, SiteSectionContextDto? section) =>
        GccV2SiteSection.ValidateSiteSectionGate(projectSiteRunId, section);

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
        if (string.IsNullOrWhiteSpace(S(root, "ctaType"))) missing.Add("ctaType");
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
        if (string.IsNullOrWhiteSpace(S(root, "lengthBand"))) missing.Add("lengthBand");
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
        if (!string.IsNullOrWhiteSpace(brief.CtaType))
        {
            var line = $"CTA: {brief.CtaType}";
            if (!string.IsNullOrWhiteSpace(brief.CtaLabel))
                line += $" ({brief.CtaLabel})";
            sb.AppendLine(line + " — weave naturally into closing, not forced.");
        }
        if (!string.IsNullOrWhiteSpace(brief.LengthBand))
            sb.AppendLine($"Length band: {brief.LengthBand} — respect target length.");
        if (!string.IsNullOrWhiteSpace(brief.WritingNotes))
            sb.AppendLine($"Writing notes: {brief.WritingNotes}");
        return sb.ToString();
    }

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
            sb.AppendLine("2. Attribute it: name the source and include its URL where the claim appears.");
            sb.AppendLine("   Both are on the bracketed line above the passage -- the page title first,");
            sb.AppendLine("   then its URL in parentheses -- and every passage indented beneath that line");
            sb.AppendLine("   belongs to it. Never attribute a claim to a URL you did not read it under.");
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
            foreach (var q in research.Quoteables)
            {
                // No origin condition. There used to be one, testing RetrievalMode == "rag_chunk"
                // and labelling everything else "operator-supplied" -- but the only producer of an
                // operator-supplied quoteable was the Wiki/.edu/.gov upload path, removed
                // 2026-09-29 with the UI control that fed it. What the condition actually caught
                // was a null RetrievalMode, which is what GccPartnerUrlResearchService leaves on a
                // partner page it fetched and extracted. So real partner evidence was announced to
                // the model as an operator upload -- which the lines above define as plain prose
                // carrying none of the structure labels. Evidence was being discredited by a test
                // for a case that no longer exists.
                sb.AppendLine($"[{q.Title}] ({q.Url})");
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
            // surfaced in the UI only; the operator adds it to writing notes themselves).
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
    /// Part 4 — Consultant / four-phase methodology system-appendix, injected at the
    /// GeekAPI call site (NOT by editing the external content-writer-v2 prompt builder).
    /// Applied when toneOfVoice == consultant_professional, or the angle is the
    /// comprehensive ultimate-guide. Returns "" when it should not apply.
    /// </summary>
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
            "automation. Voice: objective, authoritative, technical, analytical (newspaper-style). Use first-person",
            "plural or objective third-person advisor. Assume peer-level technical knowledge; high scannability.",
            "Weave these four phases into the narrative (do not label them mechanically):",
            "1. Business Objectives Alignment — the measurable goal / pain point (ROI, bottlenecks, cost of inaction).",
            "2. Data Quality Assessment — integrity, schema, storage (pooling, JSONB, validation).",
            "3. Tech Selection & Architecture — specific tools over generics (decoupled services, routing, benchmarks).",
            "4. Pilot Implementation Strategy — execution, smoke tests, validation (local integration, TDD, sandboxed rollout).",
            "Constraints: ban AI filler / clichés. Emit no markup of any kind — structure is carried",
            "by the section contract, not by characters in the text. Close with an FAQ drawn from the",
            "People Also Ask / related searches in the brief. Keep temperature low.",
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
    public sealed record ToolPageOutcome(string ProductName, string? BodyJson, string? Refusal)
    {
        public bool Written => BodyJson is not null;
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
    /// own page and that refusal is returned, not thrown — the others still ship. A deliberate exception
    /// to "one failure fails all", scoped to this fan-out: otherwise one thin partner means a project can
    /// never produce any tool page. The caller decides what to do when <i>every</i> partner refuses.
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
        string? onlyProduct = null)
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

        var outcomes = new List<ToolPageOutcome>(slices.Count);
        foreach (var slice in slices)
        {
            try
            {
                var body = await GenerateStartingContentAsync(
                    slice.Narrow(create) with { StartingContentType = "tool" },
                    section,
                    provider,
                    ct,
                    mustMentionBlock,
                    slice.Passages,
                    toolName: slice.ProductName);
                outcomes.Add(new ToolPageOutcome(slice.ProductName, body, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(
                    "Tool page for {Product} ({Host}) was not written: {Reason}",
                    slice.ProductName, slice.Host, ex.Message);
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
        string? toolName = null)
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
            if (string.IsNullOrWhiteSpace(create.Topic) || string.IsNullOrWhiteSpace(create.Notes))
                throw new InvalidOperationException("Standalone image prompt requires topic and notes.");
            return await GenerateImagePromptJsonAsync(
                create.Topic,
                $"{briefBlock}\n\n{create.Notes}",
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
            var tool = await GenerateToolPageAsync(
                toolName: string.IsNullOrWhiteSpace(toolName) ? create.Topic : toolName.Trim(),
                brief: create.Notes,
                sourceContext: $"{briefBlock}\n\n{BuildAudience(create, section)}",
                department: string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department,
                relatedArticleUrl: null,
                provider: provider,
                ct: ct,
                create: create,
                passages: passages);
            return JsonSerializer.Serialize(new
            {
                title = tool.Name,
                metaDescription = tool.Metadata.MetaDescription,
                summary = tool.Metadata.Summary,
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
            brief.CtaType,
            brief.CtaLabel,
            brief.LengthBand,
            brief.WritingNotes);
        // The outline is planned for this post, not taken from a constant. The three headings that
        // used to sit here -- "Overview", "Key considerations", "Next steps" -- shipped on every
        // blog this path produced, and a section called "Key considerations" has nothing in
        // particular to say, which is the whole of why these came back short (Jeff, 2026-09-23:
        // "The headings reflect why content word count is so drastically low").
        var metaResult = await llm.CompleteAsync(_prompts.BuildStandaloneBlogMetadataPrompt(context), ct);
        var planned = LlmResponseJsonParser.Parse<BlogMetadataDraft>(metaResult.Content, "standalone blog metadata");
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

    /// <summary>
    /// Legacy GCC artifact revise. Prefer project revise via CWV2 orchestrator.
    /// Uses CWV2 section JSON + revision notes — not CWV3 ReviseStructuredDraftAsync.
    /// </summary>
    /// <summary>
    /// A new version of a draft, revised against feedback.
    ///
    /// <para>
    /// Three things were wrong here and all three shortened the piece on every press, which is what
    /// made "Fix these and revise" reliably make a draft worse (Jeff, 2026-09-28: clicked it three
    /// times, lost word count each time).
    /// </para>
    ///
    /// <list type="number">
    /// <item>It wrote every type with the standalone blog prompt. Blog targets 2,000-2,700 words;
    /// pillar and tool target 3,500-5,000. A tool page revised once was handed a target a third
    /// smaller than the draft it was revising.</item>
    /// <item>It promoted the first returned section into the lede and dropped it from the body, so
    /// the body lost a section per press -- the pattern every generation path stopped using on
    /// 2026-09-23, kept here because revise has no lede call of its own. The document already has a
    /// lede; it is now kept, and passed to the body prompt for continuity.</item>
    /// <item>It still regenerates rather than edits -- the current draft reaches the model as
    /// flattened prose, so each pass is a rewrite from a summary. That is a larger change and is
    /// not fixed here.</item>
    /// </list>
    /// </summary>
    public async Task<string> ReviseAsync(
        string currentJson,
        string feedback,
        string scope,
        string? sectionPath,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? contentType = null)
    {
        var fb = feedback.Trim();
        if (string.Equals(scope, "section", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
                throw new InvalidOperationException("sectionPath is required when scope is section.");
            fb = $"Revise ONLY the section at path “{sectionPath}”. Leave all other sections unchanged.\n\n{fb}";
        }

        // The stored body is an envelope for every long-form type -- { title, metaDescription,
        // summary, body, jsonLdSchema } -- and deserializing that straight into a ContentDocument
        // returns a shell with a null Lede rather than null, so the `?? throw` here never fired and
        // the next line dereferenced it. That NullReferenceException is the 500 Revise returned on
        // every long-form draft.
        var envelope = GccBodyEnvelope.Read(currentJson, CwDocumentJson);
        var document = envelope.Document
            ?? throw new InvalidOperationException(
                "This draft cannot be revised: its stored body is not a content document.");

        // The draft used to be flattened into `notes`, which BuildMinimalContext puts in
        // CrawledParagraphs -- rendered by the research brief under "Representative site copy:". So
        // the model was handed the previous draft labelled as background from the publisher's
        // website, with nothing saying it was the thing being revised. It rewrote, correctly, from
        // what it had been told it was looking at. The draft now arrives as the draft.
        var llm = GetLlm(provider);
        var context = BuildMinimalContext(document.Lede.Heading, notes: null, ToLlm(provider));
        fb = $"{CurrentDraftBlock(document)}{Environment.NewLine}{Environment.NewLine}{fb}";
        var metadata = new ArticleMetadataDraft(
            Title: document.Lede.Heading,
            MetaDescription: Truncate(document.Lede.Heading, 160),
            Keywords: [document.Lede.Heading],
            SectionOutline: document.Sections.Select(s => s.Heading).Where(h => !string.IsNullOrWhiteSpace(h)).ToList());

        // The type's own prompt set, so a pillar is revised as a pillar. Falls back to the blog
        // prompt only for a type with no set registered, which is what every type used to get.
        var typeSet = _types.Find(contentType);
        // Both metadata shapes: Blog's prompts take BlogMetadataDraft and refuse the article shape,
        // which is a real per-type difference rather than something to convert away. Supplying only
        // one means revising that type throws instead of revising.
        var promptCtx = new ContentTypes.ContentTypePromptContext(
            context,
            Metadata: metadata,
            BlogMetadata: new BlogMetadataDraft(
                metadata.Title, metadata.MetaDescription, metadata.Keywords, metadata.SectionOutline),
            Lede: document.Lede);
        var request = typeSet is not null
            ? typeSet.Body(promptCtx with { RevisionNotes = fb })
            : _prompts.BuildStandaloneBlogBodyPrompt(
                context,
                new BlogMetadataDraft(metadata.Title, metadata.MetaDescription, metadata.Keywords, metadata.SectionOutline),
                revisionNotes: fb,
                lede: document.Lede);

        var bodyResult = await llm.CompleteAsync(request, ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "revised body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 revise returned no sections.");
        // Every returned section is body. The lede is the one the piece was written with.
        var revised = new ContentDocument(document.Lede, [.. sections]);

        // A revision that comes back a quarter shorter has not revised the draft, it has replaced
        // it with a summary -- which is what three presses of "Fix these and revise" did, each one
        // storing the loss as a new version. Refused rather than repaired: the previous version is
        // intact and still the latest, and a draft the model shortened cannot be lengthened back by
        // this code without inventing the missing words.
        var beforeWords = ContentDocumentText.CountWords(document);
        var afterWords = ContentDocumentText.CountWords(revised);
        if (beforeWords > 0 && afterWords < beforeWords * 0.75)
        {
            throw new InvalidOperationException(
                $"Refused: the revision came back at {afterWords:N0} words from {beforeWords:N0} -- "
                + "it rewrote the piece rather than revising it. The current version is unchanged. "
                + "Narrow the feedback to the sections that need work and try again.");
        }
        revised = ContentGuardrail.Apply(revised).Document;
        // Back into the envelope it came from. Revise used to store the bare document, so a revised
        // blog lost its title, meta description, summary and JSON-LD -- the envelope was not only
        // unread, it was dropped.
        return GccBodyEnvelope.Write(envelope, revised, CwDocumentJson);
    }

    /// <summary>
    /// The draft being revised, as the draft being revised.
    ///
    /// <para>
    /// Revise regenerates rather than edits -- the type's body prompt returns a fresh sections
    /// array -- so the only way to keep what the feedback did not ask to change is to put it in
    /// front of the model and say so. Headings and prose, in order, with the instruction that
    /// everything not named by the feedback comes back as it was.
    /// </para>
    /// </summary>
    private static string CurrentDraftBlock(ContentDocument document)
    {
        var sb = new StringBuilder()
            .AppendLine("=== THE DRAFT YOU ARE REVISING ===")
            .AppendLine(
                "This is the current piece, in full. Return it revised -- not rewritten. Every "
                + "section below comes back, in this order, with its substance intact, unless the "
                + "feedback asks for that section to change. Keep the examples, the figures, the "
                + "named products and the length. A revision that returns less than it was given "
                + "has lost the reader something nobody asked to remove.")
            .AppendLine();

        foreach (var section in document.Sections)
        {
            AppendDraftSection(sb, section, depth: 0);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendDraftSection(StringBuilder sb, Section section, int depth)
    {
        var indent = new string(' ', depth * 2);
        // Not "## heading": Markdown is banned end to end here, and a prompt that shows the model
        // Markdown is a prompt that gets Markdown back.
        if (!string.IsNullOrWhiteSpace(section.Heading))
        {
            sb.AppendLine($"{indent}[{(depth == 0 ? "H2" : "H" + (depth + 2))}] {section.Heading}");
        }

        foreach (var text in ContentDocumentText.ParagraphTexts(section))
        {
            if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine($"{indent}{text}");
        }

        sb.AppendLine();
        foreach (var child in section.Children)
        {
            AppendDraftSection(sb, child, depth + 1);
        }
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
        var raw = result.Content?.Trim() ?? string.Empty;

        try
        {
            using var _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                prompt = raw,
                style = "Illustration",
                negativePrompt = "readable text, logos, watermarks",
                aspectRatio = "16:9",
            }, JsonOpts);
        }
    }

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
        var raw = result.Content?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Social/ads pack LLM returned empty content.");

        // Strip code fences if the model wraps JSON.
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("Social/ads pack LLM returned non-JSON content.");
            raw = raw[start..(end + 1)];
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("variants", out var variants)
                || variants.ValueKind != JsonValueKind.Array
                || variants.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("Social/ads pack JSON missing non-empty variants array.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Social/ads pack LLM returned invalid JSON.", ex);
        }

        return raw;
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
        int WordCount);

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
        IReadOnlyList<GccGroundedPassage>? passages = null)
    {
        var llmType = ToLlm(provider);
        var llm = _cwProviders.Get(llmType);

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
        var partnerPages = create is null
            ? []
            : GccResearchFetchService.Deserialize(create.ResearchJson)?.Quoteables ?? [];
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

        var partnerExtraction = partnerPages.Count == 0
            ? null
            : await _partnerExtraction.ExtractFromPagesAsync(partnerPages, [name], ct);
        var groundedExtraction = partnerExtraction is not null && HasSufficientPartnerData(partnerExtraction)
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
        var context = BuildMinimalContext(
            name,
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
            toolBrief.CtaType,
            toolBrief.CtaLabel,
            toolBrief.LengthBand,
            toolBrief.WritingNotes);

        // Equal to Pillar's outline in count and per-section depth (Jeff, 2026-09-22: Tool must be
        // equal in word count to Pillar if not longer). It is read from ToolPrompts rather than
        // written out again here: this literal was the third copy of that list, sitting under a
        // comment saying it had to be kept in sync with a fourth copy inside BuildToolBodyPrompt.
        // EvidenceBlock is set here, and that is new as of 2026-09-29. It was never assigned --
        // App, ToolSlug and ExtractedResearchJson only -- and nothing assigned it afterwards, so
        // `WriteToolBodyAsync(toolOutlineCtx.EvidenceBlock)` below passed null on every tool page
        // ever generated. BuildToolBodyPrompt appends this block unconditionally (no provenance
        // gate, because Tool runs GccToolQuoteGuard rather than the heading guard), so the
        // QUOTEABLE RESEARCH block simply never reached the one content type where a citeable
        // blockquote is required.
        //
        // Tool was not ungrounded -- ExtractedResearchJson carries the partner extraction, and
        // HasSufficientPartnerData refuses the page without it. What was missing is the retrieved
        // half: the passages GccGroundingResolver merged into ResearchJson, which pillar and blog
        // have had all along. The research half only, for the reason BuildPillarLedePrompt gives;
        // Tool resolves no competitor analyses at all, so there is no competitor block here to
        // exclude.
        //
        // Empty research renders an empty string and the append is skipped, so a create with no
        // retrieved passages builds the same prompt it built yesterday.
        var toolOutlineCtx = new ContentTypes.ContentTypePromptContext(
            context, App: app, ToolSlug: slug, ExtractedResearchJson: extractedToolResearchJson,
            EvidenceBlock: create is null ? null : BuildResearchBlock(create));
        var pillarMeta = new ArticleMetadataDraft(
            Title: name,
            MetaDescription: Truncate((brief ?? name).Trim(), 160),
            Keywords: [name],
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
        var ledeResult = await llm.CompleteAsync(
            toolType.Lede(toolOutlineCtx with { Metadata = pillarMeta }), ct);
        var (toolLede, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, $"tool page '{name}' lede");

        // `brief` used to be passed positionally here, landing in the revisionNotes slot -- every
        // first-time generation had its own brief framed to the model as "REVISION REQUIRED --
        // address the reviewer's feedback," phantom feedback on a draft that never existed. It
        // already reaches the model correctly via app.Description ("Tool summary: ..." below), so
        // dropping it here removes a misleading duplicate, not the only copy.
        // In batches, same reason as the pillar: this page's own outline asks for 3,200-4,400 words
        // and a single response holds about 3,000 in this JSON.
        // The CTA retry below re-writes the body, so it batches too: a retry that asks for the whole
        // page in one response is the arithmetic cap batching removed, put back on the draft that
        // ships.
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

        Task<List<Section>> WriteToolBodyAsync(string? evidenceBlock) =>
            GenerateSectionsInBatchesAsync(
                llm,
                toolType,
                toolOutlineCtx with
                {
                    Metadata = pillarMeta,
                    Lede = toolLede,
                    EvidenceBlock = evidenceBlock,
                    QuoteCandidates = quoteCandidates,
                },
                toolType.OutlineFor(toolOutlineCtx),
                $"Tool page '{name}'",
                ct);

        // The tool page had no competitor evidence at all, while one of its six sections is
        // "how a buyer should judge this product -- fit, pricing, and the adjacent approaches they
        // are also weighing". It was writing that section with no idea what the alternatives say.
        var toolCompetitorBlock = create is null
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                new[] { BuildCompetitorResearchBlock(create), BuildOwnSiteCoverageBlock(create) }
                    .Where(b => b.Length > 0));
        var sections = await WriteToolBodyAsync(toolCompetitorBlock);

        // Every tool page carries a block quotation of the partner, in their own published words
        // (Jeff, 2026-09-26: "I want a blockquote in each tool"). The prompt asks for it; this is
        // what makes it true. Without a check the model could return the page with no quote at all,
        // or with one it wrote itself carrying a real company's URL on its cite -- ContentGuardrail
        // passes quotes through untouched by design, and the renderer writes the cite straight onto
        // the tag, so nothing further down would have looked.
        //
        // Refuse, never repair: a rewritten quote is still a quote nobody verified, and trimming one
        // out would ship the page missing an element of the type. Same "Refused:" prefix the partner
        // grounding gate uses, so GenerateAsync answers 400 rather than a 503 reading as an outage.
        //
        // Scoped to `create is not null`, the same boundary the partner-grounding refusal above
        // draws. The legacy no-create path is already exempt from grounding entirely; it has no
        // partner evidence at all, so requiring a partner quote there would be requiring an
        // invented one. Where the page is grounded, it carries the quote.
        // Snap first, judge second. The writer copies a candidate's words and copying drifts -- a live run
        // lost AvidXchange's page to a shortened span with an ellipsis added. Snapping restores the
        // system's own string for anything that matches a candidate; the guard below still refuses
        // anything that matches none.
        if (create is not null)
        {
            sections = [.. Guardrail.GccToolQuoteGuard.SnapQuotesToCandidates(sections, quoteCandidates)];
        }

        var quoteViolations = create is null
            ? []
            : Guardrail.GccToolQuoteGuard.FindViolations(sections, quoteCandidates);
        if (quoteViolations.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refused: the tool page '{name}' does not carry a verifiable block quotation. "
                + string.Join(" ", quoteViolations));
        }

        // FAQ, additional to the body's own word-count target, not part of it (Jeff, 2026-09-22).
        // Sourced only from real, already-verified partner FAQ pairs -- never invented and never
        // re-derived the way Pillar's PAA-driven FAQ section has to answer from scratch.
        //
        // Held separately as well as appended, because the CTA retry below regenerates the body and
        // would otherwise drop it -- it is answered from verified partner data, not written, so
        // re-running the body has no bearing on it.
        Section? toolFaqSection = null;
        if (groundedExtraction is not null && groundedExtraction.FaqBank.Count > 0)
        {
            var faqResult = await llm.CompleteAsync(
                _prompts.BuildToolFaqSectionPrompt(context, pillarMeta, app, groundedExtraction.FaqBank),
                ct);
            toolFaqSection = LlmResponseJsonParser.ParseSection(faqResult.Content, "h2", $"tool page '{name}' FAQ section");
            sections.Add(toolFaqSection);
        }

        var document = new ContentDocument(toolLede with { Tag = "h2" }, sections);

        // The scheduler is on every page, so every page links it (Jeff, 2026-09-27: "CTA is on every
        // page and should be referenced"). The prompt asks; this is what makes it true. Without it a
        // draft closing on "book a consultation with our team" as plain text ships, because a run
        // with no href is ordinary prose and the renderer is right to draw it that way.
        //
        // One retry naming the omission before the refusal stands, matching pillar and blog. The
        // retry regenerates the body only, so the FAQ section is put back onto it.
        var toolCtaViolations = Guardrail.GccClosingCtaGuard.FindViolations(document, context.ConsultationAnchorHref);
        if (toolCtaViolations.Count > 0)
        {
            _logger.LogInformation("Tool closing did not link the scheduler; retrying once with the omission named.");
            var toolCtaSections = await WriteToolBodyAsync(
                string.IsNullOrEmpty(toolCompetitorBlock)
                    ? Guardrail.GccClosingCtaGuard.RetryInstruction(context.ConsultationAnchorHref!)
                    : $"{toolCompetitorBlock}{Environment.NewLine}"
                      + Guardrail.GccClosingCtaGuard.RetryInstruction(context.ConsultationAnchorHref!));
            if (toolFaqSection is not null) toolCtaSections.Add(toolFaqSection);
            var retried = new ContentDocument(toolLede with { Tag = "h2" }, toolCtaSections);
            var retriedViolations = Guardrail.GccClosingCtaGuard.FindViolations(
                retried, context.ConsultationAnchorHref);
            // A tool page carries a block quotation and nothing else does, so a retry that fixes
            // the link and loses the quote is not a draft worth keeping. This check belongs to
            // this method only.
            if (retriedViolations.Count == 0
                && Guardrail.GccToolQuoteGuard.FindViolations(toolCtaSections, quoteCandidates).Count == 0)
            {
                document = retried;
                sections = toolCtaSections;
                toolCtaViolations = retriedViolations;
            }
        }

        // Reported, not refused. The scheduler href is a known constant that did not get attached to
        // a sentence -- nothing is invented either way -- and a draft whose closing is unlinked is
        // one the operator can see and fix, where a refused generate is nothing at all
        // (Jeff, 2026-09-27: "It's a CTA? WTF?").
        if (toolCtaViolations.Count > 0)
            _logger.LogWarning(
                "The tool page {Name} ships without a scheduler link, after a retry naming the omission. {Detail}",
                name, string.Join(" ", toolCtaViolations));

        // Per-H2 image prompts. Tool pages are long-form (a six-heading outline, equal to Pillar,
        // plus an optional FAQ section) and this is the revenue-critical content type -- the one
        // place this couldn't be left as a follow-up the way it briefly was. `section` is accepted
        // but genuinely unused inside GenerateSectionImagePromptsAsync (checked directly), so null
        // is correct here, not a gap.
        var documentWithImagePrompts = await GenerateSectionImagePromptsAsync(
            "tool", name, JsonSerializer.Serialize(document, CwDocumentJson), null, provider, ct);
        document = JsonSerializer.Deserialize<ContentDocument>(documentWithImagePrompts, CwDocumentJson)
            ?? throw new InvalidOperationException($"Could not re-read '{name}' after attaching image prompts.");

        // The page's own subject has to appear in it. The prompt says "Name {app.Name} throughout,
        // in every section" and nothing checked -- the same asymmetry that let a blog name two
        // partners out of five while looking finished. One product here rather than five, but the
        // failure is worse: a tool page that never names its tool is not a thin page, it is a
        // category explainer wearing a product's title.
        var toolMissing = GccRequiredToolMentions.Missing(document, [name]);
        if (toolMissing.Count > 0)
            throw new InvalidOperationException(
                $"Tool page for '{name}' never names it. A page about a product must name the product.");

        var wordCount = ContentDocumentText.CountWords(document);

        var metaResult = await llm.CompleteAsync(
            _prompts.BuildToolMetadataPrompt(context, pillarMeta, app, document),
            ct);
        var metadata = LlmResponseJsonParser.Parse<ToolMetadataDraft>(metaResult.Content, "tool metadata");

        var metaDescription = metadata.MetaDescription.Length > 160
            ? metadata.MetaDescription[..160]
            : metadata.MetaDescription;
        metadata = metadata with { MetaDescription = metaDescription };

        var toolUrl = $"{_company.ToolBaseUrl.TrimEnd('/')}/{dept}/{slug}";
        // Our page about the product. Distinct from app.Url, which is the product's own home.
        app = app with { PageUrl = toolUrl };
        var now = DateTime.UtcNow;
        // AreaServed/PublisherType come through like everywhere else: they describe the publisher
        // node, and this page's publisher is the operator, same as the pillar's.
        // Faq is independent of site data: it reads the tool page's own generated document.
        var schemaMeta = ContentMetadataFactory.For(
            context, name, metaDescription, toolUrl, pillarMeta.Keywords, document, now);

        var pillarUrl = string.IsNullOrWhiteSpace(relatedArticleUrl)
            ? $"{_company.ArticleBaseUrl.TrimEnd('/')}/{dept}"
            : relatedArticleUrl;

        // Partner grounding: when extraction actually yielded data, emit the real partner-extraction
        // §9 JSON-LD (fail-closed on price/review assertions with no library evidence) instead of the
        // generic single-description builder, which has no concept of pricing, offers, or reviews at
        // all. Falls back to the generic builder when ungrounded, so non-partner tool pages are
        // unaffected.
        string jsonLd;
        if (groundedExtraction is not null)
        {
            var partnerNode = GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerSoftwareApplicationJsonLd
                .TryBuild(groundedExtraction, partnerPages);
            if (partnerNode is not null)
            {
                GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerSoftwareApplicationJsonLd
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

        return new ToolPageResult(name, slug, document, metadata, jsonLd, pillarUrl, wordCount);
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
    /// isolated factual claim via Citables -- AND breadth across at least 3 of the 22 payload
    /// categories, so sections beyond Key Capabilities have something real to draw from too.</summary>
    private static bool HasSufficientPartnerData(GccPartnerExtractionDocument extraction) =>
        (extraction.FeatureInventory.Count > 0 || extraction.Citables.Count > 0)
        && CountPopulatedPartnerDataCategories(extraction) >= 3;

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
        + (extraction.FreshnessLog.Count > 0 ? 1 : 0)
        + (extraction.BattlecardSlices.Count > 0 ? 1 : 0)
        + (extraction.DemoBeats.Count > 0 ? 1 : 0)
        + (extraction.ComplianceSnippets.Count > 0 ? 1 : 0)
        + (extraction.AffiliateDisclosures.Count > 0 ? 1 : 0);

    /// <summary>Diagnostic for the refusal message -- names what was and wasn't found, so "reported
    /// failure" means an operator can see why, not just that grounding failed.</summary>
    /// <summary>
    /// Re-plans the outline once when it carries a tools-listing heading, then refuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Jeff, 2026-10-02: a pillar and a blog do not contain a tools section, period — the tools are
    /// named in the solution prose, saying how each helps solve the problem the Angle identifies.
    /// </para>
    /// <para>
    /// This had to move to plan time to stop recurring. The ban is in the body prompt and in the outline
    /// prompts, and the body writer still produced the section, because it was handed the heading as its
    /// assignment: the retry then re-wrote against the same outline and could not succeed. A writer
    /// obeying a bad plan is not a prompt problem.
    /// </para>
    /// <para>
    /// Refusing here costs one metadata call. Refusing after the body costs the whole page, and after a
    /// body retry, twice that.
    /// </para>
    /// </remarks>
    private async Task<List<string>> ReplanOutlineWithoutToolsSectionsAsync(
        IContentGenerationProvider llm,
        ChatCompletionRequest metadataPrompt,
        IReadOnlyList<string> plannedOutline,
        string label,
        Func<string, IReadOnlyList<string>> outlineOfRetry,
        CancellationToken ct)
    {
        var offending = Guardrail.GccToolsSectionGuard.FindToolsHeadings(plannedOutline);
        if (offending.Count == 0) return [.. plannedOutline];

        _logger.LogInformation(
            "{Label}: the planned outline carried {Count} tools-listing heading(s) ({Headings}); re-planning once.",
            label, offending.Count, string.Join(", ", offending));

        var retryPrompt = metadataPrompt with
        {
            Messages = [
                .. metadataPrompt.Messages,
                new ChatMessage(
                    ChatRole.User,
                    Guardrail.GccToolsSectionGuard.OutlineRetryInstruction(offending)),
            ],
        };

        var retried = await llm.CompleteAsync(retryPrompt, ct);
        var retriedOutline = outlineOfRetry(retried.Content);
        var stillOffending = Guardrail.GccToolsSectionGuard.FindToolsHeadings(retriedOutline);
        if (stillOffending.Count == 0 && retriedOutline.Count > 0)
        {
            return [.. retriedOutline];
        }

        throw new InvalidOperationException(
            $"Refused: the planned outline for the {label} carries a section whose job is to list tools — "
            + string.Join(", ", (stillOffending.Count > 0 ? stillOffending : offending).Select(h => $"\"{h}\""))
            + " — after a re-plan naming it. Tools are named in the prose of the sections they serve, "
            + "never in a heading.");
    }

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
            + $"{populated} of 22 payload categories populated (need at least 3), "
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

    private IContentGenerationProvider GetLlm(ContentGeneratorProvider provider) =>
        _cwProviders.Get(ToLlm(provider));

    private static LlmProviderType ToLlm(ContentGeneratorProvider provider) =>
        provider == ContentGeneratorProvider.Anthropic ? LlmProviderType.Anthropic : LlmProviderType.OpenAi;

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
        string? ctaType = null,
        string? ctaLabel = null,
        string? lengthBand = null,
        string? writingNotes = null,
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
            TargetKeyword: topic,
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
            CtaType: ctaType,
            CtaLabel: ctaLabel,
            LengthBand: lengthBand,
            WritingNotes: writingNotes,
            // Empty on every Create-path generate until 2026-09-27, which is why
            // AppendKnownToolsBrief never rendered and no draft ever linked a tool.
            KnownCrawlTools: knownTools);
    }

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
    private static IReadOnlyList<string>? ParsePaaQuestions(JsonElement root)
    {
        if (!root.TryGetProperty("paaQuestions", out var prop))
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
            var ctaType = S("ctaType");
            if (ctaType is not null && !new[] { "sign_up", "contact_us", "book_now", "download", "learn_more", "apply_now", "get_quote" }.Contains(ctaType))
            {
                var legacyCta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["start_trial"] = "sign_up", ["subscribe"] = "sign_up", ["book_demo"] = "book_now", ["read_related"] = "learn_more", ["contact_quote"] = "get_quote", ["buy"] = "get_quote" };
                if (!legacyCta.TryGetValue(ctaType, out var mapped)) ctaType = null;
                else ctaType = mapped;
            }
            var ctaLabel = S("ctaLabel");
            var lengthBand = S("lengthBand");
            var writingNotes = S("writingNotes");
            IReadOnlyList<string>? paaQuestions = ParsePaaQuestions(root);
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
                CtaType = string.IsNullOrWhiteSpace(ctaType) ? null : ctaType,
                CtaLabel = string.IsNullOrWhiteSpace(ctaLabel) ? null : ctaLabel.Trim(),
                LengthBand = string.IsNullOrWhiteSpace(lengthBand) ? null : lengthBand.Trim(),
                WritingNotes = string.IsNullOrWhiteSpace(writingNotes) ? null : writingNotes.Trim(),
                PaaQuestions = paaQuestions,
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
        public string? CtaType { get; init; }
        public string? CtaLabel { get; init; }
        public string? LengthBand { get; init; }
        public string? WritingNotes { get; init; }
        public IReadOnlyList<string>? PaaQuestions { get; init; }
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
        GccV2SiteSection.TryBuildSectionContext(analysisId, payload, gapTopic);

    public static GcwSeoAnalyzer.SeoReport AnalyzeSeo(string bodyJson, string keyword) =>
        GcwSeoAnalyzer.Analyze(bodyJson, keyword);

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
        if (!string.IsNullOrWhiteSpace(brief.CtaType))
            sb.AppendLine($"Call to action: {brief.CtaType}"
                + (string.IsNullOrWhiteSpace(brief.CtaLabel) ? "" : $" — worded as \"{brief.CtaLabel}\""));
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
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: 1024);

        var result = await llm.CompleteAsync(request, ct);
        var raw = result.Content?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Email generation returned empty content.");

        if (raw.StartsWith("```"))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("Email generation returned non-JSON content.");
            raw = raw[start..(end + 1)];
        }

        return raw;
    }

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
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.65,
            MaxOutputTokens: maxTokens);

        var result = await llm.CompleteAsync(request, ct);
        var raw = result.Content?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"{platform} generation returned empty content.");

        if (raw.StartsWith("```"))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException($"{platform} generation returned non-JSON content.");
            raw = raw[start..(end + 1)];
        }

        return raw;
    }

    /// <summary>
    /// A pillar body as a <see cref="ContentDocument"/>, serialized. Never markup: the model
    /// returns typed sections, the document holds the structure, and tag characters are produced
    /// only by <c>SectionHtmlRenderer</c> at export.
    /// </summary>
    /// <remarks>
    /// This replaced a prompt that asked for a prose body and a caller that read structure back out
    /// of the string. The lede goes through <c>BuildPillarLedePrompt</c> so lede-type guidance
    /// applies here as it does on the orchestrator path.
    /// </remarks>
    public async Task<string> GeneratePillarBodyAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        string? mustMentionBlock,
        CancellationToken ct)
    {
        var llm = GetLlm(provider);
        var competitorAnalyses = await ResolveCompetitorAnalysesAsync(create, ct);
        var context = BuildPillarContext(
            await _publisherProfile.ResolveAsync(create.ProjectId, ct),
            await _knownTools.ResolveAsync(create, ct),
            create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(
            create, competitorAnalyses, mustMentionBlock, await PartnerUrlsForAsync(create, ct));
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses);
        // Prompts come from the type's own set, not from a switch over a flat builder -- see
        // content-creator-v2/plans/prompts-per-content-type.md.
        var pillarType = RequireType("pillar");
        var outlineCtx = new ContentTypes.ContentTypePromptContext(context);
        var metadata = new ArticleMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: [.. pillarType.OutlineFor(outlineCtx).Select(sl => sl.Label)]);
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
        var ledeEvidence = BuildResearchBlock(create);
        var pillarPromptCtx = outlineCtx with { Metadata = metadata, EvidenceBlock = ledeEvidence };
        var ledeResult = await llm.CompleteAsync(pillarType.Lede(pillarPromptCtx), ct);
        // BuildPillarLedePrompt asks for LedeAndIntroductionJsonContract -- {"lede": {...},
        // "introduction": {...}} -- so it must be read with ParseLedeAndIntroduction, the way
        // ContentGenerationOrchestrator reads the same prompt. Reading it as a sections array threw
        // "Model did not return a valid sections array for pillar lede" on every single pillar
        // generation, while the model was in fact complying exactly (Jeff, 2026-09-23, whose error
        // carried a perfectly good directAddress hook that this then discarded).
        var (pillarLede, _, pillarIntroduction) =
            LlmResponseJsonParser.ParseLedeAndIntroduction(ledeResult.Content, "pillar lede");

        // The partner tools this page is obliged to name, stated to the model and checked against
        // the result below -- one list, so the instruction and the check cannot disagree.
        var requiredTools = GccRequiredToolMentions.For(
            create.BriefJson, await PartnerUrlsForAsync(create, ct));
        var toolInstruction = GccRequiredToolMentions.Instruction(requiredTools);
        var pillarEvidence = string.IsNullOrWhiteSpace(toolInstruction)
            ? evidenceBlock
            : $"{evidenceBlock}{Environment.NewLine}{toolInstruction}";

        // In batches. One response cannot hold a 3,000-word floor in this JSON -- see
        // SectionsPerBatch -- so asking for the whole page in one call capped it by arithmetic.
        var pillarOutline = pillarType.OutlineFor(outlineCtx);

        // Every retry below re-writes the body, so every retry batches too. A retry that asks for
        // the whole page in one response is exactly the arithmetic cap batching removed, put back
        // at the point the page can least afford it -- the draft that ships.
        Task<List<Section>> WritePillarBodyAsync(string evidenceBlock) =>
            GenerateSectionsInBatchesAsync(
                llm,
                pillarType,
                pillarPromptCtx with { EvidenceBlock = evidenceBlock, Lede = pillarLede },
                [.. pillarOutline.Skip(1)],
                "Pillar body",
                ct);

        var bodySections = await WritePillarBodyAsync(pillarEvidence);

        // Stage 2: every heading the model invented beyond the assigned outline must be licensed
        // by real material shown to it -- retrieval, the brief, curated PAA, or a competitor
        // heading -- never invented from nothing. Fail closed, same as everywhere else in this
        // codebase: a draft with an unlicensed heading is not persisted, not trimmed to the
        // licensed subset.
        var provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);

        // Before provenance: a tools section fails that check for the wrong-looking reason, because
        // the model licenses it against a real "Top 5 ... Tools" heading on the site.
        var pillarToolsSections = Guardrail.GccToolsSectionGuard.FindToolsSections(bodySections);
        if (pillarToolsSections.Count > 0)
        {
            _logger.LogInformation(
                "Pillar wrote a tools section ({Headings}); retrying once with it named.",
                string.Join(", ", pillarToolsSections));
            var retried = await WritePillarBodyAsync(
                $"{pillarEvidence}{Environment.NewLine}"
                + Guardrail.GccToolsSectionGuard.RetryInstruction(pillarToolsSections));
            if (Guardrail.GccToolsSectionGuard.FindToolsSections(retried).Count == 0)
            {
                bodySections = retried;
                provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);
                pillarToolsSections = [];
            }
        }

        if (pillarToolsSections.Count > 0)
            throw new InvalidOperationException(
                "Refused: the pillar carries a section whose job is to list tools — "
                + string.Join(", ", pillarToolsSections.Select(h => $"\"{h}\""))
                + " — after a retry naming it. Tools belong in the prose of the sections they serve.");

        // One retry, the same courtesy every other guard on this path gets. Re-tagging a heading or
        // rewriting it is mechanical once the writer is told which values actually license one, and
        // refusing on first sight threw away a whole generate over a tag.
        if (provenanceViolations.Count > 0)
        {
            _logger.LogInformation(
                "Pillar wrote {Count} unlicensed heading(s); retrying once with the licensable values named.",
                provenanceViolations.Count);
            var provenanceRetry = await WritePillarBodyAsync(
                $"{pillarEvidence}{Environment.NewLine}"
                + GccHeadingProvenanceGuard.RetryInstruction(provenanceViolations, evidence));
            // A body with no sections has no unlicensed headings either, so "zero violations" is not
            // on its own evidence that the retry worked -- it is also what an empty response looks
            // like. Requiring sections stops an empty retry silently replacing a real body, which is
            // the success-shaped empty result this codebase refuses everywhere else.
            var retryViolations = provenanceRetry.Count == 0
                ? provenanceViolations
                : GccHeadingProvenanceGuard.FindUnlicensedHeadings(provenanceRetry, evidence);
            if (provenanceRetry.Count > 0 && retryViolations.Count == 0)
            {
                bodySections = provenanceRetry;
                provenanceViolations = [];
            }
            else
            {
                provenanceViolations = retryViolations;
            }
        }

        if (provenanceViolations.Count > 0)
            throw new InvalidOperationException(
                "Pillar body contains unlicensed headings after a retry naming the licensable values: "
                + string.Join("; ", provenanceViolations));

        // Stage 8c: the brief's PAA questions were parsed (ExtractBriefFields) and then silently
        // dropped -- never fed to an FAQ section anywhere on this path. Not "cluster PAA again at
        // generation time" (the questions are already operator-curated, by SerpIngestPanel's own
        // selection UI, before they ever reach BriefJson); just stop discarding them.
        var paaQuestions = ExtractBriefFields(create.BriefJson).PaaQuestions;
        if (paaQuestions is { Count: > 0 })
        {
            var faqResult = await llm.CompleteAsync(
                _prompts.BuildArticleFaqSectionPrompt(context, metadata, paaQuestions, isRegeneration: false),
                ct);
            bodySections.Add(LlmResponseJsonParser.ParseSection(faqResult.Content, "h2", "pillar FAQ section"));
        }

        // The lede IS the first H2. When the model gives the lede and the introduction the same
        // heading, they are one section and storing both duplicates it -- same merge the
        // orchestrator does for this prompt. Otherwise the introduction is a real section and leads
        // the body, so the outline's first entry is not lost.
        var lede = pillarLede with { Tag = "h2" };
        // Always merged, never conditional. This used to compare the two headings and insert the
        // introduction as a separate first section when they differed -- so whether a reader got one
        // opening or two came down to whether the model happened to return matching strings. Jeff,
        // 2026-09-23: "This just feels wrong". The introduction carries no heading, so there is
        // nothing to compare and nothing to decide: it is the lede continuing, under the lede's own
        // heading.
        lede = lede with
        {
            Paragraphs = [.. lede.Paragraphs, .. pillarIntroduction.Paragraphs],
            Children = [.. lede.Children, .. pillarIntroduction.Children],
        };

        var document = new ContentDocument(lede, bodySections);
        document = ContentGuardrail.Apply(document).Document;

        // One retry naming what was left out, rather than discarding a finished draft over an
        // omission the model would fix if told. Then the refusal stands -- asking repeatedly until
        // the answer comes back right is how unsupported claims get written.
        var pillarMissing = GccRequiredToolMentions.Missing(document, requiredTools);
        if (pillarMissing.Count > 0)
        {
            _logger.LogInformation(
                "Pillar omitted {Missing}; retrying once with the omission named.", string.Join(", ", pillarMissing));
            var retrySections = await WritePillarBodyAsync(
                $"{pillarEvidence}{Environment.NewLine}{GccRequiredToolMentions.RetryInstruction(pillarMissing)}");
            if (GccHeadingProvenanceGuard.FindUnlicensedHeadings(retrySections, evidence).Count == 0)
            {
                document = ContentGuardrail.Apply(new ContentDocument(lede, retrySections)).Document;
                pillarMissing = GccRequiredToolMentions.Missing(document, requiredTools);
            }
        }

        if (pillarMissing.Count > 0)
            throw new InvalidOperationException(
                $"Pillar names {requiredTools.Count - pillarMissing.Count} of {requiredTools.Count} partner tools, "
                + $"after a retry naming the omission. Missing: {string.Join(", ", pillarMissing)}. "
                + "Every declared partner must be named -- check that each has an indexed crawl, since a partner "
                + "with no evidence gives the writer nothing to say about it.");

        // The scheduler is on every page, so every page links it (Jeff, 2026-09-27: "CTA is on every
        // page and should be referenced"). The prompt asks; this is what makes it true. Without it a
        // draft closing on "book a consultation with our team" as plain text ships, because a run
        // with no href is ordinary prose and the renderer is right to draw it that way.
        //
        // One retry naming the omission before the refusal stands, the same treatment the required
        // partner mentions above get.
        var pillarCtaViolations = Guardrail.GccClosingCtaGuard.FindViolations(document, context.ConsultationAnchorHref);
        if (pillarCtaViolations.Count > 0)
        {
            _logger.LogInformation("Pillar closing did not link the scheduler; retrying once with the omission named.");
            var pillarCtaSections = await WritePillarBodyAsync(
                $"{pillarEvidence}{Environment.NewLine}"
                + Guardrail.GccClosingCtaGuard.RetryInstruction(context.ConsultationAnchorHref!));
            if (GccHeadingProvenanceGuard.FindUnlicensedHeadings(pillarCtaSections, evidence).Count == 0)
            {
                var retried = ContentGuardrail.Apply(new ContentDocument(lede, pillarCtaSections)).Document;
                var retriedViolations = Guardrail.GccClosingCtaGuard.FindViolations(
                    retried, context.ConsultationAnchorHref);
                // Only take the retry when it fixed the thing it was asked to fix, and when it did
                // not drop a partner the first draft had named -- trading one refusal for another is
                // not progress.
                if (retriedViolations.Count == 0
                    && GccRequiredToolMentions.Missing(retried, requiredTools).Count == 0)
                {
                    document = retried;
                    pillarCtaViolations = retriedViolations;
                }
            }
        }

        // Reported, not refused. The scheduler href is a known constant that did not get attached to
        // a sentence -- nothing is invented either way -- and a draft whose closing is unlinked is
        // one the operator can see and fix, where a refused generate is nothing at all
        // (Jeff, 2026-09-27: "It's a CTA? WTF?").
        if (pillarCtaViolations.Count > 0)
            _logger.LogWarning(
                "The pillar {Name} ships without a scheduler link, after a retry naming the omission. {Detail}",
                create.Topic, string.Join(" ", pillarCtaViolations));

        // Image prompts attach here rather than in the caller, matching Tool and Blog -- the caller
        // ran them over the returned JSON, which only worked while this returned a bare document.
        var pillarWithPrompts = await GenerateSectionImagePromptsAsync(
            "pillar", create.Topic, JsonSerializer.Serialize(document, CwDocumentJson), section, provider, ct);
        document = JsonSerializer.Deserialize<ContentDocument>(pillarWithPrompts, CwDocumentJson) ?? document;

        // Title, standfirst, meta description and TechArticle JSON-LD. v1's orchestrator produced
        // all of it for a pillar; the Create reimplementation returned a bare document, leaving
        // BuildArticleMetadataPrompt and ArticleSchemaBuilder sitting here with no caller.
        var pillarMetaPrompt = _prompts.BuildArticleMetadataPrompt(context);
        var pillarMetaResult = await llm.CompleteAsync(pillarMetaPrompt, ct);
        var pillarMeta = LlmResponseJsonParser.Parse<ArticleMetadataDraft>(pillarMetaResult.Content, "pillar metadata");

        // Before the body is written. Same reason as the blog: a planned heading is an assignment the
        // writer cannot decline, so the ban has to apply to the plan.
        pillarMeta = pillarMeta with
        {
            SectionOutline = await ReplanOutlineWithoutToolsSectionsAsync(
                llm,
                pillarMetaPrompt,
                pillarMeta.SectionOutline ?? [],
                "pillar",
                content => LlmResponseJsonParser.Parse<ArticleMetadataDraft>(content, "pillar metadata re-plan")
                    .SectionOutline ?? [],
                ct),
        };
        var pillarMetaDescription = pillarMeta.MetaDescription.Length > 160
            ? pillarMeta.MetaDescription[..160]
            : pillarMeta.MetaDescription;

        // {base}/{department}/{slug} -- the scheme the live site and v1's export both use
        // (geekatyourspot.com/use-cases/marketing/<slug>). Written without the department first,
        // which would have produced a canonical URL pointing at a page that does not exist.
        var pillarDept = string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department.Trim();
        var pillarUrl = $"{_company.ArticleBaseUrl.TrimEnd('/')}/{pillarDept}/{Slugify(pillarMeta.Title)}";
        var pillarSchemaMeta = ContentMetadataFactory.For(
            context, pillarMeta.Title, pillarMetaDescription, pillarUrl, pillarMeta.Keywords, document);

        return JsonSerializer.Serialize(new
        {
            title = pillarMeta.Title,
            metaDescription = pillarMetaDescription,
            summary = pillarMeta.Summary,
            body = document,
            // No companion blog exists on this path, so there is nothing to cite as related -- an
            // invented URL would be a claim about a page that does not exist.
            jsonLdSchema = _articleSchema.Build(pillarSchemaMeta, relatedBlogPostUrl: string.Empty),
        }, CwDocumentJson);
    }

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
            brief.CtaType,
            brief.CtaLabel,
            brief.LengthBand,
            brief.WritingNotes,
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
        var context = BuildPillarContext(
            await _publisherProfile.ResolveAsync(create.ProjectId, ct),
            await _knownTools.ResolveAsync(create, ct),
            create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(
            create, competitorAnalyses, mustMentionBlock, await PartnerUrlsForAsync(create, ct));
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses);
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
        var blogMetaPrompt = _prompts.BuildStandaloneBlogMetadataPrompt(context);
        var blogMetaResult = await llm.CompleteAsync(blogMetaPrompt, ct);
        var blogMeta = LlmResponseJsonParser.Parse<BlogMetadataDraft>(blogMetaResult.Content, "blog metadata");

        // Before the body is written, not after: the writer is handed these headings as its assignment.
        blogMeta = blogMeta with
        {
            SectionOutline = await ReplanOutlineWithoutToolsSectionsAsync(
                llm,
                blogMetaPrompt,
                blogMeta.SectionOutline ?? [],
                "blog",
                content => LlmResponseJsonParser.Parse<BlogMetadataDraft>(content, "blog metadata re-plan")
                    .SectionOutline ?? [],
                ct),
        };
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
        var ledeEvidence = BuildResearchBlock(create);
        var blogPromptCtx = new ContentTypes.ContentTypePromptContext(
            context, BlogMetadata: metadata, EvidenceBlock: ledeEvidence);
        var ledeResult = await llm.CompleteAsync(blogType.Lede(blogPromptCtx), ct);
        // Same mismatch as pillar above: this prompt asks for LedeJsonContract, so it is read with
        // ParseLede. Reading it as a sections array failed every blog generation.
        var (blogLede, _) = LlmResponseJsonParser.ParseLede(ledeResult.Content, "blog lede");

        var blogRequiredTools = GccRequiredToolMentions.For(
            create.BriefJson, await PartnerUrlsForAsync(create, ct));
        var blogToolInstruction = GccRequiredToolMentions.Instruction(blogRequiredTools);
        var blogEvidence = string.IsNullOrWhiteSpace(blogToolInstruction)
            ? evidenceBlock
            : $"{evidenceBlock}{Environment.NewLine}{blogToolInstruction}";

        // In batches, same reason as pillar and tool: one response cannot hold the 1,800-word floor
        // in this JSON, so asking for the whole post in one call capped it by arithmetic -- 1,199
        // words and a 0.2% keyword density were the symptom (Jeff, 2026-09-28).
        //
        // Every retry below re-writes the body, so every retry batches too.
        var blogOutline = blogType.OutlineFor(blogPromptCtx);
        Task<List<Section>> WriteBlogBodyAsync(string evidenceBlock) =>
            GenerateSectionsInBatchesAsync(
                llm,
                blogType,
                blogPromptCtx with { EvidenceBlock = evidenceBlock, Lede = blogLede },
                blogOutline,
                "Blog body",
                ct);

        List<Section> bodySections = await WriteBlogBodyAsync(blogEvidence);

        var provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);
        // Before provenance, because a tools section fails provenance for the wrong-looking reason:
        // the model tries to license it against a real "Top 5 ... Tools" heading on the site, and
        // the refusal then talks about tags when the problem is the section. One retry naming it,
        // then the refusal stands.
        var blogToolsSections = Guardrail.GccToolsSectionGuard.FindToolsSections(bodySections);
        if (blogToolsSections.Count > 0)
        {
            _logger.LogInformation(
                "Blog wrote a tools section ({Headings}); retrying once with it named.",
                string.Join(", ", blogToolsSections));
            var retried = await WriteBlogBodyAsync(
                $"{blogEvidence}{Environment.NewLine}"
                + Guardrail.GccToolsSectionGuard.RetryInstruction(blogToolsSections));
            if (Guardrail.GccToolsSectionGuard.FindToolsSections(retried).Count == 0)
            {
                bodySections = retried;
                provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);
                blogToolsSections = [];
            }
        }

        if (blogToolsSections.Count > 0)
            throw new InvalidOperationException(
                "Refused: the blog carries a section whose job is to list tools — "
                + string.Join(", ", blogToolsSections.Select(h => $"\"{h}\""))
                + " — after a retry naming it. Tools belong in the prose of the sections they serve.");

        // Same one retry as the pillar, and for the same reason: a model that tagged a heading
        // "paa:How to implement AI in accounts payable?" -- a question that reads exactly like a real
        // one and is not in this brief -- can fix that when told which values exist.
        if (provenanceViolations.Count > 0)
        {
            _logger.LogInformation(
                "Blog wrote {Count} unlicensed heading(s); retrying once with the licensable values named.",
                provenanceViolations.Count);
            var provenanceRetry = await WriteBlogBodyAsync(
                $"{blogEvidence}{Environment.NewLine}"
                + GccHeadingProvenanceGuard.RetryInstruction(provenanceViolations, evidence));
            // A body with no sections has no unlicensed headings either, so "zero violations" is not
            // on its own evidence that the retry worked -- it is also what an empty response looks
            // like. Requiring sections stops an empty retry silently replacing a real body, which is
            // the success-shaped empty result this codebase refuses everywhere else.
            var retryViolations = provenanceRetry.Count == 0
                ? provenanceViolations
                : GccHeadingProvenanceGuard.FindUnlicensedHeadings(provenanceRetry, evidence);
            if (provenanceRetry.Count > 0 && retryViolations.Count == 0)
            {
                bodySections = provenanceRetry;
                provenanceViolations = [];
            }
            else
            {
                provenanceViolations = retryViolations;
            }
        }

        if (provenanceViolations.Count > 0)
            throw new InvalidOperationException(
                "Blog body contains unlicensed headings after a retry naming the licensable values: "
                + string.Join("; ", provenanceViolations));

        var document = new ContentDocument(blogLede with { Tag = "h2" }, bodySections);
        document = ContentGuardrail.Apply(document).Document;

        var blogMissing = GccRequiredToolMentions.Missing(document, blogRequiredTools);
        if (blogMissing.Count > 0)
        {
            _logger.LogInformation(
                "Blog omitted {Missing}; retrying once with the omission named.", string.Join(", ", blogMissing));
            var blogRetrySections = await WriteBlogBodyAsync(
                $"{blogEvidence}{Environment.NewLine}{GccRequiredToolMentions.RetryInstruction(blogMissing)}");
            if (GccHeadingProvenanceGuard.FindUnlicensedHeadings(blogRetrySections, evidence).Count == 0)
            {
                document = ContentGuardrail.Apply(
                    new ContentDocument(blogLede with { Tag = "h2" }, blogRetrySections)).Document;
                blogMissing = GccRequiredToolMentions.Missing(document, blogRequiredTools);
            }
        }

        if (blogMissing.Count > 0)
            throw new InvalidOperationException(
                $"Blog names {blogRequiredTools.Count - blogMissing.Count} of {blogRequiredTools.Count} partner tools, "
                + $"after a retry naming the omission. Missing: {string.Join(", ", blogMissing)}. "
                + "Every declared partner must be named -- check that each has an indexed crawl, since a partner "
                + "with no evidence gives the writer nothing to say about it.");

        // The scheduler is on every page, so every page links it (Jeff, 2026-09-27: "CTA is on every
        // page and should be referenced"). The prompt asks; this is what makes it true. Without it a
        // draft closing on "book a consultation with our team" as plain text ships, because a run
        // with no href is ordinary prose and the renderer is right to draw it that way.
        //
        // One retry naming the omission before the refusal stands, the same treatment the required
        // partner mentions above get: the model does emit the run when told plainly, and discarding
        // two thousand words over a missing href is waste.
        var blogCtaViolations = Guardrail.GccClosingCtaGuard.FindViolations(document, context.ConsultationAnchorHref);
        if (blogCtaViolations.Count > 0)
        {
            _logger.LogInformation("Blog closing did not link the scheduler; retrying once with the omission named.");
            var ctaRetrySections = await WriteBlogBodyAsync(
                $"{blogEvidence}{Environment.NewLine}"
                + Guardrail.GccClosingCtaGuard.RetryInstruction(context.ConsultationAnchorHref!));
            if (GccHeadingProvenanceGuard.FindUnlicensedHeadings(ctaRetrySections, evidence).Count == 0)
            {
                var retried = ContentGuardrail.Apply(
                    new ContentDocument(blogLede with { Tag = "h2" }, ctaRetrySections)).Document;
                var retriedViolations = Guardrail.GccClosingCtaGuard.FindViolations(
                    retried, context.ConsultationAnchorHref);
                // Only take the retry when it actually fixed the thing it was asked to fix -- a
                // second draft that still has no link is not an improvement worth keeping, and it
                // would also discard the required-tool mentions the first one had satisfied.
                if (retriedViolations.Count == 0)
                {
                    document = retried;
                    blogCtaViolations = retriedViolations;
                }
            }
        }

        // Reported, not refused. The scheduler href is a known constant that did not get attached to
        // a sentence -- nothing is invented either way -- and a draft whose closing is unlinked is
        // one the operator can see and fix, where a refused generate is nothing at all
        // (Jeff, 2026-09-27: "It's a CTA? WTF?").
        if (blogCtaViolations.Count > 0)
            _logger.LogWarning(
                "The blog {Name} ships without a scheduler link, after a retry naming the omission. {Detail}",
                create.Topic, string.Join(" ", blogCtaViolations));

        // Image prompts are attached here rather than by the caller, the way the tool page already
        // does it. The caller used to run them on the returned JSON, which only worked while this
        // returned a bare ContentDocument -- it now returns the same envelope Tool does, so the
        // step has to happen before wrapping.
        var withPrompts = await GenerateSectionImagePromptsAsync(
            "blog", create.Topic, JsonSerializer.Serialize(document, CwDocumentJson), section, provider, ct);
        document = JsonSerializer.Deserialize<ContentDocument>(withPrompts, CwDocumentJson) ?? document;

        var blogNow = DateTime.UtcNow;
        var blogDept = string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department.Trim();
        var blogUrl = $"{_company.BlogBaseUrl.TrimEnd('/')}/{blogDept}/{Slugify(metadata.Title)}";
        var blogSchemaMeta = ContentMetadataFactory.For(
            context, metadata.Title, blogMetaDescription, blogUrl, metadata.Keywords, document, blogNow);

        return JsonSerializer.Serialize(new
        {
            title = metadata.Title,
            metaDescription = blogMetaDescription,
            summary = metadata.Summary,
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
    private static string BuildEvidenceBlock(
        GccCreateDto create, IReadOnlyList<GccCompetitorPageAnalysis> competitorAnalyses)
    {
        var sb = new StringBuilder();
        var researchBlock = BuildResearchBlock(create);
        if (researchBlock.Length > 0)
            sb.AppendLine(researchBlock);

        var competitorBlock = BuildCompetitorHeadingBlock(competitorAnalyses);
        if (competitorBlock.Length > 0)
            sb.AppendLine(competitorBlock);

        var competitorResearch = BuildCompetitorResearchBlock(create);
        if (competitorResearch.Length > 0)
            sb.AppendLine(competitorResearch);

        var ownSite = BuildOwnSiteCoverageBlock(create);
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
    internal static string BuildOwnSiteCoverageBlock(GccCreateDto create)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        var pages = research?.SiteQuoteables;
        if (pages is not { Count: > 0 })
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
                sb.AppendLine($"- H{h.Level}: {h.Text}");
            foreach (var para in page.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                sb.AppendLine($"- {para}");
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
    /// Wording follows <c>GccV2ContextAdapter</c>'s competitor branch, which had the fullest
    /// version of these rules already written and never reached the live path.
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

        foreach (var page in pages.Take(MaxCompetitorPagesInPrompt))
        {
            sb.AppendLine($"[{page.Title}]");
            foreach (var h in page.Headings.Take(GccResearchCaps.MaxHeadingsPerPage))
                sb.AppendLine($"- H{h.Level}: {h.Text}");
            foreach (var para in page.Paragraphs.Take(GccResearchCaps.MaxParagraphsPerPage))
                sb.AppendLine($"- {para}");
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
        foreach (var page in analyses.Take(MaxCompetitorPagesInPrompt))
        {
            sb.AppendLine($"[{page.Url}]");
            var flat = new List<(string Text, int Level)>();
            FlattenCompetitorHeadings(page.Headings, flat);
            // The level is a parenthetical, not a prefix -- "competitor:<exact heading text>" must
            // not have to guess whether "H2: " counts as part of the heading it's quoting.
            foreach (var h in flat.Take(MaxCompetitorHeadingsPerPage))
                sb.AppendLine($"- {h.Text} (h{h.Level})");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

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
    private const int SectionsPerBatch = 2;

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
    private static async Task<List<Section>> GenerateSectionsInBatchesAsync(
        IContentGenerationProvider llm,
        ContentTypes.IContentTypePrompts type,
        ContentTypes.ContentTypePromptContext promptCtx,
        IReadOnlyList<SectionSlot> outline,
        string label,
        CancellationToken ct)
    {
        var written = new List<Section>();
        for (var i = 0; i < outline.Count; i += SectionsPerBatch)
        {
            var batch = outline.Skip(i).Take(SectionsPerBatch).ToList();
            var result = await llm.CompleteAsync(
                type.Body(promptCtx with { SectionBatch = batch, SectionBatchIndex = i / SectionsPerBatch }), ct);
            var sections = LlmResponseJsonParser.ParseSections(
                result.Content, $"{label} sections {i + 1}-{i + batch.Count}").ToList();

            if (sections.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{label}: sections {i + 1}-{i + batch.Count} of {outline.Count} came back empty. "
                    + "The draft is not saved -- a page missing part of its own plan is not a short "
                    + "page, it is an incomplete one.");
            }

            written.AddRange(sections);
        }

        return written;
    }

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
        AddIfPresent("ctaType", brief.CtaType);
        AddIfPresent("ctaLabel", brief.CtaLabel);
        AddIfPresent("lengthBand", brief.LengthBand);
        AddIfPresent("writingNotes", brief.WritingNotes);

        var paaQuestions = new HashSet<string>(
            (brief.PaaQuestions ?? []).Select(q => q.Trim()), StringComparer.OrdinalIgnoreCase);

        var competitorHeadings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in competitorAnalyses)
        {
            var flat = new List<(string Text, int Level)>();
            FlattenCompetitorHeadings(page.Headings, flat);
            foreach (var h in flat)
                competitorHeadings.Add(h.Text);
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
        var raw = result.Content?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Image prompts generation returned empty content.");

        if (raw.StartsWith("```"))
        {
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start)
                throw new InvalidOperationException("Image prompts generation returned non-JSON content.");
            raw = raw[start..(end + 1)];
        }

        var parsed = JsonSerializer.Deserialize<SectionImagePromptsResponse>(raw, JsonOpts);
        var prompts = parsed?.Prompts ?? [];
        if (prompts.Count == 0)
            throw new InvalidOperationException("Image prompts generation returned no prompts.");

        // One per section is the whole contract -- the system prompt says "EXACTLY ONE prompt for
        // EACH listed section" and they are assigned positionally below. A short list used to be
        // absorbed by the bounds check on that assignment: ask for six, get one, and the hero kept
        // its prompt while five H2s silently kept none, with a paid call behind it and nothing
        // reported. Same shape as the extraction swallow that hid a total outage for two hours.
        //
        // Expected is the listed sections plus the hero at index 0.
        var expectedPrompts = sections.Count + 1;
        if (prompts.Count < expectedPrompts)
            throw new InvalidOperationException(
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
