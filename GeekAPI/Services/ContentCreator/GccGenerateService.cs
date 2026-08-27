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

namespace GeekAPI.Services.ContentCreator;

public sealed record RelatedPageDto(string Url, string Title, HeadingDto[] Headings, string Excerpt);

public sealed record SiteSectionContextDto(
    [property: JsonPropertyName("siteAnalysisProfileId")] Guid SiteAnalysisId,
    string GapTopic,
    string? GapSectionPath,
    IReadOnlyList<RelatedPageDto> RelatedPages,
    IReadOnlyList<string> TopicalNeighbors,
    InformationGainNote? InformationGain = null);

public sealed record ContentGapDto(
    string Id,
    string Topic,
    string? SectionPath,
    string Reason,
    IReadOnlyList<string>? Hierarchy = null,
    string? SourcePageUrl = null);

public sealed record SiteAnalysisDto(Guid Id, string Domain, string Status);

public sealed record SiteAnalysisStoredPayload(
    IReadOnlyList<ContentGapDto> Gaps,
    IReadOnlyList<RelatedPageDto> SitePages,
    IReadOnlyList<string> TopicalNeighbors,
    Guid? SeoProfileId = null,
    Guid? SeoProjectId = null);

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

    public GccGenerateService(
        IContentPromptBuilder prompts,
        IContentProviderFactory cwProviders,
        ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccGenerateService> logger)
    {
        _prompts = prompts;
        _cwProviders = cwProviders;
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
        _company = company.Value;
        _logger = logger;
    }

    public static SiteSectionContextDto? ParseSiteSection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SiteSectionContextDto>(json, JsonOpts)
                ?? throw new InvalidOperationException("Site section JSON deserialized to null.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Site section JSON could not be parsed.", ex);
        }
    }

    /// <summary>
    /// Required gate: every Generate must have a crawl id (site_analysis_profiles.Id).
    /// Domain-only grounding (crawl id with no section) is allowed — Generate uses
    /// page-section trees for "must mention" injection. Handoff-created sections must have
    /// non-empty relatedPages. Applies to all types including imagePrompt/aiTool (no exemption);
    /// per-H2 image prompts must include siteSection+tree with at least one top-level section.
    /// </summary>
    public static void ValidateSiteSectionGate(Guid? siteAnalysisProfileId, SiteSectionContextDto? section)
    {
        if (siteAnalysisProfileId is null || siteAnalysisProfileId == Guid.Empty)
            throw new InvalidOperationException(
                "site analysis required — run or reuse an analysis for this domain");

        if (section is null) return;
        if (section.RelatedPages is null || section.RelatedPages.Count == 0)
            throw new InvalidOperationException(
                "Site Analyzer–started Generate requires non-empty relatedPages in site section context.");
    }

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

    public static string BuildBriefAndResearchBlock(GccCreateDto create)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== BRIEF ===");
        sb.AppendLine("(Persisted Content Brief — follow these controls; if audience detail conflicts with primary, follow detail.)");
        sb.AppendLine(create.BriefJson!.Trim());
        sb.AppendLine();

        var research = GccResearchFetchService.Deserialize(create.ResearchJson);
        if (research?.Quoteables is { Count: > 0 })
        {
            sb.AppendLine("=== QUOTEABLE RESEARCH (destination pages — quote/paraphrase; do not invent) ===");
            // Uploaded research is unlimited — read every quoteable (per-page heading/paragraph
            // trimming below still bounds prompt size).
            foreach (var q in research.Quoteables)
            {
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
            "Constraints: ban AI filler / clichés; Markdown ##/### outline; close with an FAQ drawn from the",
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
        string AssignmentMarkdown);

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
                var assignment = FormatSectionAssignment(node);
                matches.Add(new HierarchyMatchDto(
                    path.ToArray(),
                    children,
                    page.PageUrl,
                    node.HeadingText,
                    kind,
                    assignment));
            }
        }

        return matches
            .Select(m =>
            {
                var node = FindNodeByPath(pageTrees, m.SourcePageUrl, m.Path);
                var links = node is null ? 0 : CountSubtreeToolLinks(node);
                return (Match: m, Links: links);
            })
            .OrderBy(x => x.Match.Kind == "exact-heading" ? 0 : 1)
            .ThenByDescending(x => x.Links)
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

    private static int CountSubtreeToolLinks(HttpGeekSeoSiteAnalyzerClient.PageSectionDto node)
    {
        var n = 0;
        foreach (var s in FlattenSections([node]))
            n += UniqueToolLinks(s.Links).Count;
        return n;
    }

    public sealed record CrawlTool(string Name, string? Href);

    /// <summary>
    /// Tools from the crawl's page-section trees under the matched heading (typically an h4 keyword).
    /// Collects every unique tool link under that node and all descendant h5/h6 sections — no per-heading
    /// 2+ gate and no count cap (sibling h5s with a single link must not be dropped).
    /// When the selected hierarchy leaf has no links, walk ancestor path segments so a barren leaf
    /// does not hide tools on the parent topic. Does not fall back to unrelated pages.
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

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tools = new List<CrawlTool>();

        // Collect candidate partner links under the match (all descendant subtrees).
        // Prefer /tools/… hrefs when present — homepage use-case blocks list partners that way,
        // and chrome (Privacy, Call Us, CTAs) otherwise floods the allowlist.
        foreach (var node in FlattenSections([matched]))
        {
            foreach (var tool in UniqueToolLinks(node.Links))
            {
                if (!seen.Add(tool.Name)) continue;
                tools.Add(tool);
            }
        }

        var toolPageLinks = tools.Where(t => HrefLooksLikeOnSiteToolPage(t.Href)).ToList();
        if (toolPageLinks.Count > 0) return toolPageLinks;

        if (tools.Count > 0) return tools;

        // Fallback: deeper headings that look like product names (not "Top N Tools:" labels).
        // Covers crawls where partner names are H6 text without harvested Links.
        var matchLevel = matched.Level > 0 ? matched.Level : 4;
        foreach (var node in FlattenSections([matched]))
        {
            if (node.Level <= matchLevel) continue;
            var name = (node.HeadingText ?? "").Trim().TrimEnd(':').Trim();
            if (!LooksLikePartnerProductName(name)) continue;
            if (!seen.Add(name)) continue;
            tools.Add(new CrawlTool(name, null));
        }

        return tools;
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
        var deeper = 0;
        var links = 0;
        foreach (var n in FlattenSections([node]))
        {
            links += UniqueToolLinks(n.Links).Count;
            if (n.Level > matchLevel)
                deeper++;
        }
        return (deeper, links);
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

            if (HrefLooksLikeOnSiteToolPage(h)) return true;

            if (LooksLikeSiteChromeHref(h)) return false;
        }

        // Product-ish short labels (Intercom, HubSpot AI) — reject sentence-like CTAs.
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is < 1 or > 5) return false;
        if (name.Contains('"') || name.Contains('?')) return false;
        return true;
    }

    internal static bool HrefLooksLikeOnSiteToolPage(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        try
        {
            if (Uri.TryCreate(href, UriKind.Absolute, out var abs))
                return abs.AbsolutePath.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
            return href.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return href.Contains("/tools/", StringComparison.OrdinalIgnoreCase);
        }
    }

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

    private static string FormatSectionAssignment(HttpGeekSeoSiteAnalyzerClient.PageSectionDto node)
    {
        var sb = new StringBuilder();
        void Write(HttpGeekSeoSiteAnalyzerClient.PageSectionDto n, int depth)
        {
            var hashes = new string('#', Math.Clamp(n.Level > 0 ? n.Level : depth + 1, 1, 6));
            sb.Append(hashes).Append(' ').AppendLine(n.HeadingText ?? "");
            sb.AppendLine();
            foreach (var p in n.Paragraphs ?? [])
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                sb.AppendLine(p.Trim());
                sb.AppendLine();
            }
            foreach (var link in n.Links ?? [])
            {
                if (string.IsNullOrWhiteSpace(link.Text) || string.IsNullOrWhiteSpace(link.Href)) continue;
                sb.Append("- [").Append(link.Text.Trim()).Append("](").Append(link.Href.Trim()).AppendLine(")");
            }
            if ((n.Links?.Count ?? 0) > 0) sb.AppendLine();
            foreach (var c in n.Children ?? [])
                Write(c, depth + 1);
        }
        Write(node, 0);
        return sb.ToString().TrimEnd();
    }

    internal static IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> FlattenSections(
        IEnumerable<HttpGeekSeoSiteAnalyzerClient.PageSectionDto> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.Children is null) continue;
            foreach (var child in FlattenSections(node.Children))
                yield return child;
        }
    }

    public async Task<string> GenerateStartingContentAsync(
        GccCreateDto create,
        SiteSectionContextDto? section,
        ContentGeneratorProvider provider,
        CancellationToken ct,
        string? mustMentionBlock = null)
    {
        ValidateSiteSectionGate(create.SiteAnalysisId, section);
        ValidateBriefRequired(create);
        var briefBlock = BuildBriefAndResearchBlock(create);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            briefBlock = $"{briefBlock}\n\n{mustMentionBlock}";

        if (string.Equals(create.StartingContentType, "imagePrompt", StringComparison.OrdinalIgnoreCase))
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

        if (string.Equals(create.StartingContentType, "aiTool", StringComparison.OrdinalIgnoreCase))
        {
            var tool = await GenerateToolPageAsync(
                toolName: create.Topic,
                brief: create.Notes,
                sourceContext: $"{briefBlock}\n\n{BuildAudience(create, section)}",
                department: string.IsNullOrWhiteSpace(create.Department) ? "marketing" : create.Department,
                relatedArticleUrl: null,
                provider: provider,
                ct: ct);
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
        var context = BuildMinimalContext(document.Lede.Heading, FlattenDocument(document), ToLlm(provider));
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

        // Strip markdown fences if the model wraps JSON.
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
        string? preferredSlug = null)
    {
        var llmType = ToLlm(provider);
        var llm = _cwProviders.Get(llmType);

        var name = toolName.Trim();
        var slug = string.IsNullOrWhiteSpace(preferredSlug) ? Slugify(name) : preferredSlug.Trim();
        var description = string.Join(
            "\n",
            new[] { brief, sourceContext }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var app = new SoftwareApplicationDescriptor(name, string.IsNullOrWhiteSpace(description) ? null : description);
        var pillarMeta = new ArticleMetadataDraft(
            Title: name,
            MetaDescription: Truncate((brief ?? name).Trim(), 160),
            Keywords: [name],
            SectionOutline:
            [
                "Overview",
                "Key Capabilities",
                "Implementation Considerations",
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

        var bodyResult = await llm.CompleteAsync(
            _prompts.BuildToolBodyPrompt(context, pillarMeta, app, slug, brief),
            ct);
        var sections = LlmResponseJsonParser.ParseSections(bodyResult.Content, $"tool page '{name}'");
        if (sections.Count == 0)
            throw new InvalidOperationException($"CWV2 tool body returned no sections for '{name}'.");

        var lede = sections[0] with { Tag = "h2" };
        var document = new ContentDocument(lede, sections.Skip(1).ToList());
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
            wordCount);

        var pillarUrl = string.IsNullOrWhiteSpace(relatedArticleUrl)
            ? $"{_company.ArticleBaseUrl.TrimEnd('/')}/{dept}"
            : relatedArticleUrl;
        var jsonLd = _softwareApplicationSchemaBuilder.BuildToolPage(schemaMeta, pillarUrl, app);
        if (string.IsNullOrWhiteSpace(jsonLd))
            throw new InvalidOperationException($"CWV2 tool JSON-LD schema builder returned empty for '{name}'.");

        return new ToolPageResult(name, slug, document, metadata, jsonLd, pillarUrl, wordCount);
    }

    /// <summary>Legacy alias — prefer <see cref="GenerateToolPageAsync"/>.</summary>
    public async Task<(string Name, ContentDocument Document, string? MetaDescription, string? Summary)> GenerateToolAsync(
        string toolName,
        string? brief,
        string? sourceContext,
        ContentGeneratorProvider provider,
        CancellationToken ct)
    {
        var tool = await GenerateToolPageAsync(
            toolName, brief, sourceContext, "marketing", null, provider, ct);
        return (tool.Name, tool.Document, tool.Metadata.MetaDescription, tool.Metadata.Summary);
    }

    /// <summary>Serialize a CWV2 ContentDocument for legacy GCC artifact storage.</summary>
    public static string SerializeDocument(ContentDocument document) =>
        JsonSerializer.Serialize(document, CwDocumentJson);

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

    private static string FlattenDocument(ContentDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine(document.Lede.Heading);
        foreach (var p in document.Lede.Paragraphs)
            AppendParagraph(sb, p);
        foreach (var section in document.Sections)
        {
            sb.AppendLine(section.Heading);
            foreach (var p in section.Paragraphs)
                AppendParagraph(sb, p);
        }
        return sb.ToString();
    }

    private static void AppendParagraph(StringBuilder sb, Paragraph paragraph)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                sb.AppendLine(string.Join("", text.Runs.Select(r => r.Text)));
                break;
            case ListParagraph list:
                foreach (var item in list.Items)
                    sb.AppendLine("- " + string.Join("", item.Select(r => r.Text)));
                break;
        }
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
        string gapTopic)
    {
        if (string.IsNullOrWhiteSpace(gapTopic)) return null;

        var gap = payload.Gaps.FirstOrDefault(g =>
            string.Equals(g.Topic, gapTopic, StringComparison.OrdinalIgnoreCase));
        var sectionPath = gap?.SectionPath;

        IEnumerable<RelatedPageDto> candidates = payload.SitePages
            .Where(p => !string.IsNullOrWhiteSpace(p.Url));

        if (!string.IsNullOrWhiteSpace(sectionPath))
        {
            candidates = candidates.Where(p =>
                string.Equals(p.Title, sectionPath, StringComparison.OrdinalIgnoreCase)
                || (p.Excerpt?.Contains(sectionPath, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Headings.Any(h =>
                    h.Text.Contains(sectionPath, StringComparison.OrdinalIgnoreCase)));
        }

        var related = candidates
            .GroupBy(p => p.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(12)
            .ToList();

        if (related.Count == 0) return null;

        if (payload.TopicalNeighbors.Count == 0) return null;
        var neighbors = payload.TopicalNeighbors;

        var informationGain = GccSavedSerpParser.BuildPartialInformationGain(gapTopic, related);

        return new SiteSectionContextDto(
            analysisId,
            gapTopic,
            sectionPath,
            related,
            neighbors,
            informationGain);
    }

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
            .AppendLine("Pitch ONE clear idea. No HTML. No markdown links. Do not invent URLs.")
            .AppendLine("ctaLabel is short button/link text (e.g. \"Read the full guide\"). The destination URL is injected by the app.")
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
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
            .AppendLine("Respond with ONLY a single valid JSON object — no markdown fences:")
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

    public async Task<string> GeneratePillarBodyAsync(
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
            .AppendLine("You write comprehensive B2B pillar articles for an IT consulting firm specializing in AI implementation.")
            .AppendLine("Generate a well-structured markdown body with multiple H2 sections (each 400-600 words).")
            .AppendLine("Start directly with the first ## section — no preamble or introduction.")
            .AppendLine("Each section should be self-contained and detailed, with real examples and insights.")
            .AppendLine("Use clear language suitable for technical and business audiences.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(briefBlock)
            .AppendLine()
            .AppendLine(groundingBlock);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.7,
            MaxOutputTokens: 4096);

        var result = await llm.CompleteAsync(request, ct);
        return result.Content?.Trim() ?? "";
    }

    public async Task<string> GenerateBlogBodyAsync(
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
            .AppendLine("You write accessible B2B blog posts for an IT consulting firm specializing in AI implementation.")
            .AppendLine("Generate a well-structured markdown body with 3-4 H2 sections (each 300-400 words).")
            .AppendLine("Start directly with the first ## section — no preamble or introduction.")
            .AppendLine("Each section should be clear and approachable, with practical examples.")
            .AppendLine("Use conversational language that engages both technical and business readers.")
            .ToString();

        var user = new StringBuilder()
            .AppendLine(briefBlock)
            .AppendLine()
            .AppendLine(groundingBlock);
        if (!string.IsNullOrWhiteSpace(mustMentionBlock))
            user.AppendLine().AppendLine("Must mention:").AppendLine(mustMentionBlock);

        var request = new ChatCompletionRequest(
            Messages: [new ChatMessage(ChatRole.System, system), new ChatMessage(ChatRole.User, user.ToString())],
            Temperature: 0.7,
            MaxOutputTokens: 2048);

        var result = await llm.CompleteAsync(request, ct);
        return result.Content?.Trim() ?? "";
    }

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

        return raw;
    }

    private static List<string> ExtractSectionHeadings(string body)
    {
        var headings = new List<string>();
        var lines = body.Split('\n');
        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                headings.Add(line[3..].Trim());
            }
        }
        return headings;
    }
}
