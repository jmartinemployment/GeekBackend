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

using GeekAPI.Services.GeekSeo;
using GeekAPI.Services.ContentCreatorV2;

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
    private readonly IContentProviderFactory _cwProviders;
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccGenerateService> _logger;
    private readonly GccCompetitorAnalysisResolver _competitorAnalysis;
    private readonly GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService _partnerExtraction;

    public GccGenerateService(
        IContentPromptBuilder prompts,
        IContentProviderFactory cwProviders,
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccGenerateService> logger,
        GccCompetitorAnalysisResolver competitorAnalysis,
        GeekAPI.Services.ContentCreatorV2.Partner.GccV2PartnerExtractionService partnerExtraction)
    {
        _prompts = prompts;
        _cwProviders = cwProviders;
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _company = company.Value;
        _logger = logger;
        _competitorAnalysis = competitorAnalysis;
        _partnerExtraction = partnerExtraction;
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
    /// Long-form content types disabled 2026-09-22 (Jeff) pending a written, approved resolve plan.
    /// Tool is the one long-form type that meets the bar (always independently generated via
    /// GenerateStartingContentAsync, never repurposed) and is deliberately excluded from this set.
    /// Everything here either (a) has no dedicated generator at all -- always the generic fallback
    /// inside GenerateStartingContentAsync -- or (b) like Pillar/Blog/TechArticle, CAN still be
    /// produced via the repurpose/rewrite-derivation path when combined with a sibling long-form
    /// type in the same multi-select generate. Short-form types (email, social, ads, image prompt,
    /// linkedin-document) are explicitly out of scope for this disable -- not a current concern.
    /// Normalized by stripping hyphens/spaces and lowercasing, so "tech-article" and "techArticle"
    /// both match without needing two entries.
    /// </summary>
    private static readonly HashSet<string> DisabledLongFormContentTypes = new(StringComparer.Ordinal)
    {
        "pillar", "blog", "techarticle", "comparison", "alternatives", "casestudy",
        "guide", "listicle", "service", "local", "whitepaper",
    };

    public static bool IsContentTypeDisabledPendingImplementation(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var normalized = new string(contentType.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        return DisabledLongFormContentTypes.Contains(normalized);
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
    /// quoteable research (retrieved or operator-uploaded), uploaded Keyword SERP files, and the
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
            sb.AppendLine("Rules for this block, and they are not optional:");
            sb.AppendLine("1. Any claim about a partner, tool or product must come from a passage below.");
            sb.AppendLine("2. Attribute it: name the source and include its URL where the claim appears.");
            sb.AppendLine("3. Quote verbatim or paraphrase closely. Do not extrapolate a capability,");
            sb.AppendLine("   price, integration or limitation that no passage states.");
            sb.AppendLine("4. If the evidence does not cover something, omit it. Do not fill the gap.");
            sb.AppendLine();
            // Uploaded research is unlimited — read every quoteable (per-page heading/paragraph
            // trimming below still bounds prompt size).
            foreach (var q in research.Quoteables)
            {
                // Provenance is stated so the model — and anyone reading the rendered prompt —
                // can tell retrieved evidence from an operator upload.
                var origin = string.Equals(q.RetrievalMode, GccQuoteablePage.RetrievalModeRagChunk, StringComparison.Ordinal)
                    ? "retrieved from the crawl index"
                    : "operator-supplied";
                sb.AppendLine($"[{q.Title}] ({q.Url}) — {origin}");
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

    /// <summary>
    /// Finds the heading node matching <paramref name="topic"/> anywhere in the site's persisted
    /// page-section trees and renders its real sub-topics as a "must mention" prompt block.
    /// Matching is deterministic, not probabilistic "fuzzy": (1) exact normalized-slug match,
    /// (2) one slug containing the other. Neither hit → empty string, no injection — a wrong
    /// match would actively misdirect Generate with confidently-wrong context, which is worse
    /// than no grounding at all.
    /// </summary>
    public static string BuildMustMentionSubtopicsBlock(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string topic)
    {
        if (string.IsNullOrWhiteSpace(topic) || pageTrees.Count == 0)
            return string.Empty;

        var topicSlug = Slugify(topic);
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto? exactMatch = null;
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto? containsMatch = null;

        foreach (var page in pageTrees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null) continue;

            foreach (var node in FlattenSections(roots))
            {
                var nodeSlug = Slugify(node.HeadingText);
                if (string.Equals(nodeSlug, topicSlug, StringComparison.OrdinalIgnoreCase))
                {
                    exactMatch = node;
                    break;
                }

                if (containsMatch is null &&
                    (nodeSlug.Contains(topicSlug, StringComparison.OrdinalIgnoreCase)
                     || topicSlug.Contains(nodeSlug, StringComparison.OrdinalIgnoreCase)))
                {
                    containsMatch = node;
                }
            }

            if (exactMatch is not null) break;
        }

        var matched = exactMatch ?? containsMatch;
        if (matched is null || matched.Children is null || matched.Children.Count == 0)
            return string.Empty;

        var subtopics = matched.Children
            .Select(c => c.HeadingText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (subtopics.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "=== MUST MENTION (real sub-topics from the analyzed site) ===",
            $"This topic corresponds to a real page section (\"{matched.HeadingText}\") with the following real",
            "sub-topics on the analyzed site. The draft must mention each of these:",
        };
        lines.AddRange(subtopics.Select(s => $"- {s}"));
        return string.Join('\n', lines);
    }

    public sealed record HierarchyMatchDto(
        string[] Path,
        string[] ChildHeadings,
        string SourcePageUrl,
        string MatchedHeading,
        string Kind,
        IReadOnlyList<ToolsByHeading> ToolsByHeading);

    /// <summary>
    /// From SQL-filtered tree rows (already scoped to site_analysis_profiles.Id + keyword),
    /// pick the best heading match and return path/children for Workflow.
    /// </summary>
    public static IReadOnlyList<HierarchyMatchDto> BuildHierarchyMatchesFromTrees(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword) || pageTrees.Count == 0)
            return [];

        var topicSlug = Slugify(keyword);
        if (string.IsNullOrEmpty(topicSlug) || topicSlug == "tool")
            return [];

        var matches = new List<HierarchyMatchDto>();
        foreach (var page in pageTrees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null || roots.Count == 0) continue;

            foreach (var (node, path) in WalkSectionsWithPath(roots, []))
            {
                var nodeSlug = Slugify(node.HeadingText);
                string? kind = null;
                if (string.Equals(nodeSlug, topicSlug, StringComparison.OrdinalIgnoreCase))
                    kind = "exact-heading";
                else if (nodeSlug.Contains(topicSlug, StringComparison.OrdinalIgnoreCase)
                         || topicSlug.Contains(nodeSlug, StringComparison.OrdinalIgnoreCase))
                    kind = "contains-heading";
                if (kind is null) continue;

                var children = (node.Children ?? [])
                    .Select(c => c.HeadingText)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToArray();
                matches.Add(new HierarchyMatchDto(
                    path.ToArray(),
                    children,
                    page.PageUrl,
                    node.HeadingText,
                    kind,
                    ToolGroupsUnderNode(node, node.HeadingText ?? keyword)));
            }
        }

        return matches
            .Select(m =>
            {
                // Rank by the largest tool group under this node — the same metric as before,
                // read from the tree's own links rather than recovered from generated text.
                var toolCount = 0;
                foreach (var group in m.ToolsByHeading)
                {
                    if (group.Tools.Count > toolCount) toolCount = group.Tools.Count;
                }
                return (Match: m, Tools: toolCount);
            })
            .OrderByDescending(x => x.Tools)
            .ThenBy(x => x.Match.Kind == "exact-heading" ? 0 : 1)
            .ThenByDescending(x => x.Match.ChildHeadings.Length)
            .ThenBy(x => string.Join(" › ", x.Match.Path), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Match)
            .ToList();
    }

    private static HttpGeekSeoSiteAnalyzerClient.PageSectionDto? FindNodeByPath(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string? pageUrl,
        string[] path)
    {
        var pathWant = string.Join(" › ", path ?? []);
        var pageWant = NormalizePageUrl(pageUrl);
        foreach (var page in pageTrees)
        {
            if (pageWant.Length > 0 && NormalizePageUrl(page.PageUrl) != pageWant)
                continue;
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }

            if (roots is null) continue;
            foreach (var (node, nodePath) in WalkSectionsWithPath(roots, []))
            {
                if (string.Equals(string.Join(" › ", nodePath), pathWant, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
        }

        return null;
    }

    public sealed record CrawlTool(string Name, string? Href);

    /// <summary>
    /// Project the matched section node into the typed assignment the prompt builder renders.
    /// </summary>
    /// <remarks>
    /// The predecessor had the browser compute this and round-trip it back.
    /// The tree already carries the heading depth, the paragraphs and the anchors, so nothing here
    /// needs generating or re-parsing — it is a projection, and <c>Href</c> survives it.
    /// </remarks>
    public static HierarchyAssignment? BuildAssignmentFromTrees(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        var matched = FindMatchedSection(pageTrees, keyword, sourcePageUrl, hierarchyPath);
        return matched is null ? null : ProjectNode(matched);

        static HierarchyAssignment ProjectNode(HttpGeekSeoSiteAnalyzerClient.PageSectionDto node) =>
            new()
            {
                Heading = (node.HeadingText ?? "").Trim(),
                Level = node.Level > 0 ? node.Level : 0,
                Paragraphs = (node.Paragraphs ?? [])
                    .Select(p => (p ?? "").Trim())
                    .Where(p => p.Length > 0)
                    .ToList(),
                Links = UniqueToolLinks(node.Links)
                    .Select(t => new ToolInfo { Name = t.Name, Href = t.Href })
                    .ToList(),
                Children = (node.Children ?? []).Select(ProjectNode).ToList(),
            };
    }

    /// <summary>
    /// Tools from the crawl under the matched use-case heading.
    /// v1-style: each heading node keeps its own links; a tool list is ≥2 anchors that dominate
    /// that node's paragraph text. No /tools/ path preference and no merging every link in the subtree.
    /// </summary>
    public static IReadOnlyList<CrawlTool> ExtractToolsFromTrees(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        // The saved path first; if no node carries it, fall back to matching the keyword alone.
        // No leaf→root widening: a parent's tools are not this section's tools.
        var tools = ExtractToolsUnderMatch(pageTrees, keyword, sourcePageUrl, hierarchyPath);
        if (tools.Count > 0) return tools;

        var pathWasUsed = !string.IsNullOrWhiteSpace(hierarchyPath);
        return pathWasUsed
            ? ExtractToolsUnderMatch(pageTrees, keyword, sourcePageUrl, null)
            : tools;
    }

    private static IReadOnlyList<string?> HierarchyPathAttempts(string? hierarchyPath)
    {
        var path = (hierarchyPath ?? "").Trim();
        if (path.Length == 0) return [null];

        var parts = path.Split('›', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return [path];

        // Leaf → … → root (widen the subtree until a usable tool set appears).
        var attempts = new List<string?>();
        for (var i = parts.Length; i >= 1; i--)
            attempts.Add(string.Join(" › ", parts.Take(i)));
        return attempts;
    }

    private static IReadOnlyList<CrawlTool> ExtractToolsUnderMatch(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        var matched = FindMatchedSection(pageTrees, keyword, sourcePageUrl, hierarchyPath);
        if (matched is null) return [];

        return LargestToolGroup(ToolGroupsUnderNode(matched, matched.HeadingText ?? keyword));
    }

    /// <summary>Flatten grouped tools to the largest single group (≥2), as callers expect today.</summary>
    internal static IReadOnlyList<CrawlTool> LargestToolGroup(
        IReadOnlyList<ToolsByHeading> groups)
    {
        IReadOnlyList<CrawlTool> best = [];
        foreach (var g in groups)
        {
            if (g.Tools.Count > best.Count)
                best = g.Tools;
        }
        return best;
    }

    public sealed record ToolsByHeading(string Heading, IReadOnlyList<CrawlTool> Tools);

    /// <summary>
    /// Tool groups under one section node, one group per heading that carries a tool row.
    /// </summary>
    /// <remarks>
    /// Replaces the round trip this used to make: <c>FormatSectionAssignment</c> flattened the node
    /// into <c>#</c> headings and <c>[Text](Href)</c> bullets, and <c>ToolsInSlice</c> parsed the links
    /// back out with regexes. The tree already holds <see cref="HttpGeekSeoSiteAnalyzerClient.PageSectionDto.Links"/>
    /// as typed <see cref="HttpGeekSeoSiteAnalyzerClient.PageSectionLinkDto"/>, so the text step only
    /// created a place for the two representations to disagree.
    ///
    /// The scoring is unchanged and is the part worth keeping: a tool row is ≥2 anchors that dominate
    /// the node's own paragraph text, per <see cref="ParseHierarchyTools"/>.
    /// </remarks>
    internal static IReadOnlyList<ToolsByHeading> ToolGroupsUnderNode(
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto node,
        string fallbackHeading)
    {
        var result = new List<ToolsByHeading>();
        Walk(node, fallbackHeading ?? "");
        return result;

        void Walk(HttpGeekSeoSiteAnalyzerClient.PageSectionDto n, string inheritedHeading)
        {
            var heading = string.IsNullOrWhiteSpace(n.HeadingText)
                ? inheritedHeading
                : n.HeadingText.Trim();
            var links = n.Links ?? [];
            if (links.Count >= 2 && !string.IsNullOrWhiteSpace(heading))
            {
                var tools = ParseHierarchyTools(n.Paragraphs, links);
                var list = tools.Count > 0 ? tools : UniqueToolLinksLenient(links);
                if (list.Count >= 2)
                    result.Add(new ToolsByHeading(heading, list));
            }
            foreach (var child in n.Children ?? [])
                Walk(child, heading);
        }
    }

    /// <summary>v1 <c>uniqueToolLinks</c> — name length gate only (fallback when ratio test fails).</summary>
    private static IReadOnlyList<CrawlTool> UniqueToolLinksLenient(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionLinkDto> links)
    {
        var unique = new List<CrawlTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var name = (link.Text ?? "").Replace('\n', ' ').Trim();
            if (name.Length == 0 || name.Length >= 80) continue;
            if (LooksLikeSiteChromeName(name)) continue;
            if (Regex.IsMatch(name, @"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}")) continue;
            if (!seen.Add(name)) continue;
            unique.Add(new CrawlTool(
                name,
                string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim()));
        }
        return unique;
    }

    /// <summary>
    /// v1 <c>parseHierarchyTools</c>: ≥2 anchors that dominate paragraph text (not prose links).
    /// Ratio is checked per paragraph so a use-case blurb on the same heading does not kill the tool row.
    /// </summary>
    internal static IReadOnlyList<CrawlTool> ParseHierarchyTools(
        IReadOnlyList<string>? paragraphs,
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionLinkDto>? links)
    {
        if (links is null || links.Count < 2) return [];

        var unique = new List<CrawlTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var name = (link.Text ?? "").Replace('\n', ' ').Trim();
            var href = string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim();
            if (name.Length == 0 || name.Length >= 80) continue;
            if (LooksLikeSiteChromeName(name)) continue;
            if (!seen.Add(name)) continue;
            unique.Add(new CrawlTool(name, href));
        }
        if (unique.Count < 2) return [];

        var linkCompact = CompactAlnum(string.Join(' ', unique.Select(t => t.Name)));
        if (linkCompact.Length == 0) return [];

        var paras = paragraphs ?? [];
        if (paras.Count == 0) return unique;

        // v1 joins all paragraphs; that rejects real tool rows after a long blurb on the same heading.
        // Per-paragraph ratio matches toolsInSlice when the tool line is its own paragraph.
        foreach (var para in paras)
        {
            var pc = CompactAlnum(para);
            if (pc.Length == 0) continue;
            if ((double)linkCompact.Length / pc.Length >= 0.6) return unique;
        }

        if (paras.Any(p => Regex.IsMatch(
                p, @"\btop\b.*\btools?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            return unique;

        return [];
    }

    private static string CompactAlnum(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static bool LooksLikePartnerProductName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48) return false;
        if (Regex.IsMatch(name, @"^\s*top\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        if (name.Contains("tool", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(name, @"\btools?\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length is >= 1 and <= 6;
    }

    public sealed record ToolExtractDiag(
        string? MatchedHeading,
        string? PageUrl,
        int LinkCount,
        int HeadingCount,
        int DirectChildCount,
        int DeeperHeadingCount,
        IReadOnlyList<string> Headings);

    /// <summary>Diagnostics for Generate Tools empty-extract debugging (matched node + link density).</summary>
    public static ToolExtractDiag DiagnoseToolExtraction(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        var hit = FindMatchedSectionHit(pageTrees, keyword, sourcePageUrl, hierarchyPath);
        if (hit is null)
            return new ToolExtractDiag(null, null, 0, 0, 0, 0, []);

        var matched = hit.Value.Node;
        var headingCount = 0;
        var linkCount = 0;
        var deeper = 0;
        var matchLevel = matched.Level > 0 ? matched.Level : 4;
        var headings = new List<string>();
        foreach (var node in FlattenSections([matched]))
        {
            headingCount++;
            linkCount += UniqueToolLinks(node.Links).Count;
            var text = (node.HeadingText ?? "").Trim();
            if (text.Length > 0) headings.Add(text);
            if (node.Level > matchLevel)
                deeper++;
        }

        return new ToolExtractDiag(
            matched.HeadingText,
            hit.Value.PageUrl,
            linkCount,
            headingCount,
            matched.Children?.Count ?? 0,
            deeper,
            headings);
    }

    public static IReadOnlyList<string> ListHeadingsUnderMatch(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        var matched = FindMatchedSection(pageTrees, keyword, sourcePageUrl, hierarchyPath);
        if (matched is null) return [];
        return FlattenSections([matched])
            .Select(n => n.HeadingText ?? "")
            .Where(h => h.Length > 0)
            .ToList();
    }

    public static int CountAllToolLinks(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees)
    {
        var total = 0;
        foreach (var page in pageTrees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null) continue;
            foreach (var node in FlattenSections(roots))
                total += UniqueToolLinks(node.Links).Count;
        }
        return total;
    }

    public static IReadOnlyList<(string Heading, int LinkCount)> ListHeadingsWithToolLinks(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        int max = 12)
    {
        var rows = new List<(string Heading, int LinkCount)>();
        foreach (var page in pageTrees)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null) continue;
            foreach (var node in FlattenSections(roots))
            {
                var n = UniqueToolLinks(node.Links).Count;
                if (n < 2) continue;
                rows.Add((node.HeadingText ?? "", n));
                if (rows.Count >= max) return rows;
            }
        }
        return rows;
    }

    private static HttpGeekSeoSiteAnalyzerClient.PageSectionDto? FindMatchedSection(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath) =>
        FindMatchedSectionHit(pageTrees, keyword, sourcePageUrl, hierarchyPath)?.Node;

    private static (HttpGeekSeoSiteAnalyzerClient.PageSectionDto Node, string PageUrl)? FindMatchedSectionHit(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionTreeDto> pageTrees,
        string keyword,
        string? sourcePageUrl,
        string? hierarchyPath)
    {
        var pathWant = (hierarchyPath ?? "").Trim();
        var topicSlug = Slugify(keyword);
        var sourceWant = NormalizePageUrl(sourcePageUrl);

        // Path match: among identical hierarchy paths, prefer the richest subtree
        // (deeper headings first, then tool links) — barren copies often win on first-page order.
        if (pathWant.Length > 0)
        {
            var pathCandidates = new List<(HttpGeekSeoSiteAnalyzerClient.PageSectionDto Node, string PageUrl, int DeeperHeadings, int LinkCount)>();
            foreach (var page in pageTrees)
            {
                List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
                try
                {
                    roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                        page.TreeJson, JsonOpts);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (roots is null || roots.Count == 0) continue;

                foreach (var (node, path) in WalkSectionsWithPath(roots, []))
                {
                    if (!string.Equals(string.Join(" › ", path), pathWant, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var (deeper, links) = ScoreSubtree(node);
                    pathCandidates.Add((node, page.PageUrl ?? "", deeper, links));
                }
            }

            if (pathCandidates.Count > 0)
            {
                var best = pathCandidates
                    // Same rule as the slug branch: a barren copy is never the better match, and
                    // the page the operator actually selected outranks a richer one elsewhere.
                    .OrderByDescending(c => c.LinkCount > 0)
                    .ThenByDescending(c => sourceWant.Length > 0 && NormalizePageUrl(c.PageUrl) == sourceWant)
                    .ThenByDescending(c => c.LinkCount)
                    .ThenByDescending(c => c.DeeperHeadings)
                    .First();
                return (best.Node, best.PageUrl);
            }
        }

        var pages = pageTrees;
        if (sourceWant.Length > 0)
        {
            pages = pageTrees.Where(p => NormalizePageUrl(p.PageUrl) == sourceWant).ToList();
            if (pages.Count == 0) pages = pageTrees;
        }

        // Every slug hit, exact or partial, competes in one ranking. Bucketing exact above
        // contains meant a barren "Ad Spend Optimization" (0 links) beat the real
        // "Automated Ad Spend Optimization" (17 links) purely on slug equality.
        var slugCandidates =
            new List<(HttpGeekSeoSiteAnalyzerClient.PageSectionDto Node, string PageUrl, int Deeper, int Links, bool IsExact)>();

        foreach (var page in pages)
        {
            List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>? roots;
            try
            {
                roots = JsonSerializer.Deserialize<List<HttpGeekSeoSiteAnalyzerClient.PageSectionDto>>(
                    page.TreeJson, JsonOpts);
            }
            catch (JsonException)
            {
                continue;
            }
            if (roots is null || roots.Count == 0) continue;

            foreach (var (node, _) in WalkSectionsWithPath(roots, []))
            {
                var nodeSlug = Slugify(node.HeadingText);
                if (string.IsNullOrEmpty(topicSlug) || topicSlug == "tool") continue;

                var (deeper, links) = ScoreSubtree(node);
                var pageUrl = page.PageUrl ?? "";

                var isExact = string.Equals(nodeSlug, topicSlug, StringComparison.OrdinalIgnoreCase);
                var isPartial = !isExact
                    && (nodeSlug.Contains(topicSlug, StringComparison.OrdinalIgnoreCase)
                        || topicSlug.Contains(nodeSlug, StringComparison.OrdinalIgnoreCase));

                if (isExact || isPartial)
                    slugCandidates.Add((node, pageUrl, deeper, links, isExact));
            }
        }

        if (slugCandidates.Count == 0)
            return null;

        var winner = slugCandidates
            // A section with no links cannot be a tool list, however well its slug matches.
            .OrderByDescending(c => c.Links > 0)
            .ThenByDescending(c => sourceWant.Length > 0 && NormalizePageUrl(c.PageUrl) == sourceWant)
            .ThenByDescending(c => c.IsExact)
            .ThenByDescending(c => c.Links)
            .ThenByDescending(c => c.Deeper)
            .First();

        return (winner.Node, winner.PageUrl);
    }

    private static (int DeeperHeadings, int LinkCount) ScoreSubtree(
        HttpGeekSeoSiteAnalyzerClient.PageSectionDto node)
    {
        var matchLevel = node.Level > 0 ? node.Level : 4;
        var deeper = FlattenSections([node]).Count(n => n.Level > matchLevel);

        // Same metric as BuildHierarchyMatchesFromTrees: largest tool group under the node.
        var tools = LargestToolGroup(ToolGroupsUnderNode(node, node.HeadingText ?? ""));
        return (deeper, tools.Count);
    }

    private static string NormalizePageUrl(string? url) =>
        (url ?? "").Trim().TrimEnd('/').ToLowerInvariant();

    private static IReadOnlyList<CrawlTool> UniqueToolLinks(
        IReadOnlyList<HttpGeekSeoSiteAnalyzerClient.PageSectionLinkDto>? links)
    {
        if (links is null || links.Count == 0) return [];
        var unique = new List<CrawlTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var name = (link.Text ?? "").Replace('\n', ' ').Trim();
            var href = string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim();
            if (!IsLikelyPartnerToolLink(name, href)) continue;
            if (!seen.Add(name)) continue;
            unique.Add(new CrawlTool(name, href));
        }
        return unique;
    }

    /// <summary>
    /// Drop site chrome / CTAs / legal / phone that sit under the same matched heading as real partners.
    /// Runtime evidence (preflight): Privacy Policy, Call Us, Free Assessment were returned as "tools".
    /// </summary>
    internal static bool IsLikelyPartnerToolLink(string name, string? href)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Replace('\n', ' ').Trim();
        if (name.Length == 0 || name.Length >= 80) return false;

        if (LooksLikeSiteChromeName(name)) return false;
        if (Regex.IsMatch(name, @"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}")) return false;

        if (!string.IsNullOrWhiteSpace(href))
        {
            var h = href.Trim();
            if (h.StartsWith('#')
                || h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || h.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (LooksLikeSiteChromeHref(h)) return false;
        }

        // Product-ish short labels — reject sentence-like CTAs. No special-case for /tools/ paths.
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is < 1 or > 5) return false;
        if (name.Contains('"') || name.Contains('?')) return false;
        return true;
    }

    internal static bool HrefLooksLikeOnSiteToolPage(string? href) =>
        GccV2SiteSection.HrefLooksLikeOnSiteToolPage(href);

    private static bool LooksLikeSiteChromeName(string name)
    {
        ReadOnlySpan<string> needles =
        [
            "privacy policy", "terms of", "terms &", "cookie policy", "cookie settings",
            "call us", "contact us", "headquarters", "get your free",
            "free assessment", "read our", "learn more", "sign up", "log in",
            "subscribe", "book a", "schedule a", "click here", "about us", "careers",
            "sitemap", "follow us", "all rights reserved",
        ];
        foreach (var n in needles)
        {
            if (name.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool LooksLikeSiteChromeHref(string href)
    {
        string path;
        try
        {
            path = Uri.TryCreate(href, UriKind.Absolute, out var abs)
                ? abs.AbsolutePath
                : href.Split('?', 2)[0];
        }
        catch (UriFormatException)
        {
            path = href;
        }

        ReadOnlySpan<string> needles =
        [
            "/privacy", "/terms", "/cookie", "/contact", "/about", "/login",
            "/signup", "/sign-up", "/careers", "/sitemap", "/assessment",
        ];
        foreach (var n in needles)
        {
            if (path.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IEnumerable<(HttpGeekSeoSiteAnalyzerClient.PageSectionDto Node, List<string> Path)> WalkSectionsWithPath(
        IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> nodes,
        List<string> parentPath)
    {
        foreach (var node in nodes)
        {
            var path = new List<string>(parentPath) { node.HeadingText ?? "" };
            yield return (node, path);
            if (node.Children is null) continue;
            foreach (var child in WalkSectionsWithPath(node.Children, path))
                yield return child;
        }
    }

    internal static IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> FlattenSections(
        IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> nodes) =>
        GccV2SiteSection.FlattenSections(nodes);

    public async Task<string> GenerateStartingContentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? mustMentionBlock = null)
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
                toolName: create.Topic,
                brief: create.Notes,
                sourceContext: $"{briefBlock}\n\n{BuildAudience(create, section)}",
                department: string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department,
                relatedArticleUrl: null,
                provider: provider,
                ct: ct,
                create: create);
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
        var metadata = new BlogMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: ["Overview", "Key considerations", "Next steps"]);
        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes: null),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "standalone blog body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 blog body returned no sections.");
        var lede = sections[0] with { Tag = "h2" };
        var blogDocument = new ContentDocument(lede, sections.Skip(1).ToList());
        blogDocument = ContentGuardrail.Apply(blogDocument).Document;
        return JsonSerializer.Serialize(blogDocument, CwDocumentJson);
    }

    /// <summary>
    /// Legacy GCC artifact revise. Prefer project revise via CWV2 orchestrator.
    /// Uses CWV2 section JSON + revision notes — not CWV3 ReviseStructuredDraftAsync.
    /// </summary>
    public async Task<string> ReviseAsync(
        string currentJson,
        string feedback,
        string scope,
        string? sectionPath,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var fb = feedback.Trim();
        if (string.Equals(scope, "section", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(sectionPath))
                throw new InvalidOperationException("sectionPath is required when scope is section.");
            fb = $"Revise ONLY the section at path “{sectionPath}”. Leave all other sections unchanged.\n\n{fb}";
        }

        var document = JsonSerializer.Deserialize<ContentDocument>(currentJson, CwDocumentJson)
            ?? throw new InvalidOperationException("Current body is not a CWV2 ContentDocument.");

        var llm = GetLlm(provider);
        var context = BuildMinimalContext(document.Lede.Heading, ContentDocumentText.Flatten(document), ToLlm(provider));
        var metadata = new BlogMetadataDraft(
            Title: document.Lede.Heading,
            MetaDescription: Truncate(document.Lede.Heading, 160),
            Keywords: [document.Lede.Heading],
            SectionOutline: document.Sections.Select(s => s.Heading).Where(h => !string.IsNullOrWhiteSpace(h)).ToList());
        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(context, metadata, revisionNotes: fb),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "revised blog body");
        if (sections.Count == 0)
            throw new InvalidOperationException("CWV2 revise returned no sections.");
        var lede = sections[0] with { Tag = "h2" };
        var revised = new ContentDocument(lede, sections.Skip(1).ToList());
        revised = ContentGuardrail.Apply(revised).Document;
        return JsonSerializer.Serialize(revised, CwDocumentJson);
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
        GccCreateDto? create = null)
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
        var partnerExtraction = partnerPages.Count == 0
            ? null
            : await _partnerExtraction.ExtractFromPagesAsync(partnerPages, [name], ct);
        var groundedExtraction = partnerExtraction is not null && HasAnyPartnerData(partnerExtraction)
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
        if (create is not null && groundedExtraction is null)
        {
            throw new InvalidOperationException(
                $"Partner grounding required for '{name}': indexed partner crawl data exists for "
                + "this project, but nothing extractable was found for this tool. Not generating "
                + "an ungrounded page.");
        }

        var extractedToolResearchJson = groundedExtraction is null
            ? null
            : JsonSerializer.Serialize(groundedExtraction, PartnerExtractionJsonOpts);
        var pillarMeta = new ArticleMetadataDraft(
            Title: name,
            MetaDescription: Truncate((brief ?? name).Trim(), 160),
            Keywords: [name],
            // Equal to Pillar's six-section outline in count and per-section depth (Jeff,
            // 2026-09-22: Tool must be equal in word count to Pillar if not longer) -- must stay in
            // sync with BuildToolBodyPrompt's own "Required top-level (h2) sections" line.
            SectionOutline:
            [
                "Overview",
                "Key Capabilities",
                "How It Works",
                "Implementation Considerations",
                "Evaluation Criteria",
                "When to Use",
            ]);

        var paragraphs = new List<string>();
        if (!string.IsNullOrWhiteSpace(sourceContext))
            paragraphs.Add(sourceContext.Trim());
        if (!string.IsNullOrWhiteSpace(brief))
            paragraphs.Add(brief.Trim());

        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        var context = new ProjectGenerationContext(
            ProjectName: name,
            ProjectUrl: _company.ArticleBaseUrl,
            TargetKeyword: name,
            Department: dept,
            SiteName: _company.PublisherName,
            DetectedTone: "Professional, consultative",
            DetectedFocus: name,
            CrawledHeadings: [],
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
            Provider: llmType,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null);

        // `brief` used to be passed positionally here, landing in the revisionNotes slot -- every
        // first-time generation had its own brief framed to the model as "REVISION REQUIRED --
        // address the reviewer's feedback," phantom feedback on a draft that never existed. It
        // already reaches the model correctly via app.Description ("Tool summary: ..." below), so
        // dropping it here removes a misleading duplicate, not the only copy.
        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildToolBodyPrompt(
                context, pillarMeta, app, slug,
                revisionNotes: null,
                extractedToolResearchJson: extractedToolResearchJson),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, $"tool page '{name}'").ToList();
        if (sections.Count == 0)
            throw new InvalidOperationException($"CWV2 tool body returned no sections for '{name}'.");

        // FAQ, additional to the body's own word-count target, not part of it (Jeff, 2026-09-22).
        // Sourced only from real, already-verified partner FAQ pairs -- never invented and never
        // re-derived the way Pillar's PAA-driven FAQ section has to answer from scratch.
        if (groundedExtraction is not null && groundedExtraction.FaqBank.Count > 0)
        {
            var faqResult = await llm.CompleteAsync(
                _prompts.BuildToolFaqSectionPrompt(context, pillarMeta, app, groundedExtraction.FaqBank),
                ct);
            sections.Add(LlmResponseJsonParser.ParseSection(faqResult.Content, "h2", $"tool page '{name}' FAQ section"));
        }

        var lede = sections[0] with { Tag = "h2" };
        var document = new ContentDocument(lede, sections.Skip(1).ToList());

        // Per-H2 image prompts. Tool pages are long-form (a six-heading outline, equal to Pillar,
        // plus an optional FAQ section) and this is the revenue-critical content type -- the one
        // place this couldn't be left as a follow-up the way it briefly was. `section` is accepted
        // but genuinely unused inside GenerateSectionImagePromptsAsync (checked directly), so null
        // is correct here, not a gap.
        var documentWithImagePrompts = await GenerateSectionImagePromptsAsync(
            "tool", name, JsonSerializer.Serialize(document, CwDocumentJson), null, provider, ct);
        document = JsonSerializer.Deserialize<ContentDocument>(documentWithImagePrompts, CwDocumentJson)
            ?? throw new InvalidOperationException($"Could not re-read '{name}' after attaching image prompts.");

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
        var now = DateTime.UtcNow;
        // AreaServed/PublisherType stay unset here: neither is part of the partner-extraction
        // spec's payloads (plans/partner-extraction-complete.md) and both describe the operator's
        // own site, not a partner's -- site-hierarchy grounding is a separate concern from partner
        // grounding, not a gap this method's `create` parameter should also close.
        // Faq is independent of site data: it reads the tool page's own generated document.
        var schemaMeta = new ContentMetadata(
            name,
            metaDescription,
            context.AuthorName,
            context.PublisherName,
            context.PublisherLogoUrl,
            toolUrl,
            context.PublisherLogoUrl,
            now,
            now,
            pillarMeta.Keywords,
            wordCount,
            Faq: ContentDocumentText.ExtractFaqPairs(document));

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
    private static bool HasAnyPartnerData(GccPartnerExtractionDocument extraction) =>
        extraction.Citables.Count > 0
        || extraction.Advertisements.Count > 0
        || extraction.Comparisons.Count > 0
        || extraction.Alternatives.Count > 0
        || extraction.PricingCatalog.Count > 0
        || extraction.Icp.Count > 0
        || extraction.Integrations.Count > 0
        || extraction.FaqBank.Count > 0
        || extraction.CaseStudies.Count > 0
        || extraction.Testimonials.Count > 0
        || extraction.Awards.Count > 0
        || extraction.FeatureInventory.Count > 0
        || extraction.TechnicalConstraints.Count > 0
        || extraction.OfferCtas.Count > 0
        || extraction.Disqualifiers.Count > 0
        || extraction.UseCasePlaybooks.Count > 0
        || extraction.Categories.Count > 0
        || extraction.FreshnessLog.Count > 0
        || extraction.BattlecardSlices.Count > 0
        || extraction.DemoBeats.Count > 0
        || extraction.ComplianceSnippets.Count > 0
        || extraction.AffiliateDisclosures.Count > 0;

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
        string? writingNotes = null)
    {
        var paragraphs = string.IsNullOrWhiteSpace(notes)
            ? new List<string>()
            : new List<string> { notes };
        var dept = string.IsNullOrWhiteSpace(department) ? "marketing" : department.Trim();
        return new ProjectGenerationContext(
            ProjectName: topic,
            ProjectUrl: _company.ArticleBaseUrl,
            TargetKeyword: topic,
            Department: dept,
            SiteName: _company.PublisherName,
            DetectedTone: "Professional, consultative",
            DetectedFocus: topic,
            CrawledHeadings: [],
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
            WritingNotes: writingNotes);
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
        var context = BuildPillarContext(create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(create, competitorAnalyses);
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses);
        var metadata = new ArticleMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: [.. PillarOutline]);

        var ledeResult = await llm.CompleteAsync(
            _prompts.BuildPillarLedePrompt(
                context,
                metadata,
                ledeHeading: PillarOutline[0],
                ledeIndex: 0,
                totalSections: PillarOutline.Count,
                fullOutline: PillarOutline,
                isRegeneration: false),
            ct);
        var ledeSections = LlmResponseJsonParser.ParseSections(ledeResult.Content, "pillar lede");
        if (ledeSections.Count == 0)
            throw new InvalidOperationException("Pillar lede returned no sections.");

        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildArticleSectionBatchPrompt(
                context,
                metadata,
                headings: [.. PillarOutline.Skip(1)],
                fullOutline: PillarOutline,
                isRegeneration: false,
                revisionNotes: null,
                requireHeadingProvenance: true,
                evidenceBlock: evidenceBlock),
            ct);
        var bodySections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "pillar body").ToList();
        if (bodySections.Count == 0)
            throw new InvalidOperationException("Pillar body returned no sections.");

        // Stage 2: every heading the model invented beyond the assigned outline must be licensed
        // by real material shown to it -- retrieval, the brief, curated PAA, or a competitor
        // heading -- never invented from nothing. Fail closed, same as everywhere else in this
        // codebase: a draft with an unlicensed heading is not persisted, not trimmed to the
        // licensed subset.
        var provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);
        if (provenanceViolations.Count > 0)
            throw new InvalidOperationException(
                $"Pillar body contains unlicensed headings: {string.Join("; ", provenanceViolations)}");

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

        var document = new ContentDocument(ledeSections[0] with { Tag = "h2" }, bodySections);
        document = ContentGuardrail.Apply(document).Document;
        return JsonSerializer.Serialize(document, CwDocumentJson);
    }

    /// <summary>The pillar's standing section plan. Headings the writer must fill, not invent.</summary>
    private static readonly IReadOnlyList<string> PillarOutline =
    [
        "Overview",
        "Why it matters now",
        "How it works",
        "What to evaluate",
        "Implementation path",
        "Next steps",
    ];

    private ProjectGenerationContext BuildPillarContext(
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
            brief.WritingNotes);
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
        var context = BuildPillarContext(create, section, mustMentionBlock, provider);
        var evidence = BuildProvenanceEvidence(create, competitorAnalyses);
        var evidenceBlock = BuildEvidenceBlock(create, competitorAnalyses);
        var metadata = new BlogMetadataDraft(
            Title: create.Topic.Trim(),
            MetaDescription: Truncate((create.Notes ?? create.Topic).Trim(), 160),
            Keywords: [create.Topic.Trim()],
            SectionOutline: ["Overview", "Key considerations", "Next steps"]);

        var ledeResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogLedePrompt(context, metadata), ct);
        var ledeSections = LlmResponseJsonParser.ParseSections(ledeResult.Content, "blog lede");
        if (ledeSections.Count == 0)
            throw new InvalidOperationException("Blog lede returned no sections.");

        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildStandaloneBlogBodyPrompt(
                context, metadata, revisionNotes: null, requireHeadingProvenance: true,
                evidenceBlock: evidenceBlock),
            ct);
        var bodySections = LlmResponseJsonParser.ParseSections(bodyResult.Content, "blog body");
        if (bodySections.Count == 0)
            throw new InvalidOperationException("Blog body returned no sections.");

        var provenanceViolations = GccHeadingProvenanceGuard.FindUnlicensedHeadings(bodySections, evidence);
        if (provenanceViolations.Count > 0)
            throw new InvalidOperationException(
                $"Blog body contains unlicensed headings: {string.Join("; ", provenanceViolations)}");

        var document = new ContentDocument(ledeSections[0] with { Tag = "h2" }, bodySections);
        document = ContentGuardrail.Apply(document).Document;
        return JsonSerializer.Serialize(document, CwDocumentJson);
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
        sb.AppendLine("Real heading outlines from indexed competitor pages. You may draw a subsection from one");
        sb.AppendLine("of these headings when it fills a real gap this article should cover -- tag it");
        sb.AppendLine("\"competitor:<exact heading text>\", the text only, not the \"(hN)\" level marker");
        sb.AppendLine("(see provenance rules). Do not copy competitor prose.");
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
        IReadOnlyList<GeekAPI.Services.ContentCreatorV2.Hierarchy.GccV2HeadingNode> nodes,
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
    /// Stage 2: the concrete evidence set this specific generation call had available -- the same
    /// research/brief/PAA/competitor material rendered into the prompt, reduced to lookup sets so
    /// <see cref="GccHeadingProvenanceGuard"/> can check the model's tags against exactly what it
    /// was shown, never a broader or narrower set.
    /// </summary>
    private static GccHeadingProvenanceEvidence BuildProvenanceEvidence(
        GccCreateDto create, IReadOnlyList<GccCompetitorPageAnalysis> competitorAnalyses)
    {
        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        var retrievalUrls = new HashSet<string>(
            (research?.Quoteables ?? []).Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

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

        return new GccHeadingProvenanceEvidence(retrievalUrls, populatedBriefFields, paaQuestions, competitorHeadings);
    }

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
