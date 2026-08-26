using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreator;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Domain.Enums;
using GeekAPI.Services.Workflow.Services;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Adapters;

/// <summary>
/// Translates a v2 <see cref="GccV2BriefDto"/> (+ optional <see cref="GccV2BrandKitContent"/>) into
/// the shared Workflow <see cref="ProjectGenerationContext"/> that <c>IContentPromptBuilder</c> /
/// <c>IEditorialReviewService</c> expect. Shape copied from
/// <c>GccGenerateService.BuildMinimalContext</c> (~line 1228) into this new v2-only file — v1 is
/// never edited or referenced here.
/// </summary>
public sealed class GccV2ContextAdapter
{
    private static readonly JsonSerializerOptions BriefJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2ContextAdapter> _logger;

    public GccV2ContextAdapter(IOptions<CompanyProfileOptions> company, ILogger<GccV2ContextAdapter> logger)
    {
        _company = company.Value;
        _logger = logger;
    }

    /// Builds the base per-job context. Brand kit company/website are required — never fall back
    /// to appsettings PublisherName (Geek At Your Spot) as the writer identity.
    /// </summary>
    public ProjectGenerationContext BuildContext(
        GccV2BriefDto brief,
        GccV2BrandKitContent brandKit,
        LlmProviderType provider,
        SiteSectionContextDto? siteSection = null)
    {
        var fields = ParseBriefFields(brief.RawBriefJson);
        var targetKeyword = string.IsNullOrWhiteSpace(brief.TargetKeyword) ? "this topic" : brief.TargetKeyword;

        if (string.IsNullOrWhiteSpace(brandKit.CompanyName))
            throw new InvalidOperationException("Brand kit is missing companyName — cannot write without site identity.");
        if (string.IsNullOrWhiteSpace(brandKit.Website))
            throw new InvalidOperationException("Brand kit is missing website — cannot write without project site URL.");

        var paragraphs = BuildNotesParagraphs(fields, brandKit, siteSection);

        return new ProjectGenerationContext(
            ProjectName: targetKeyword,
            ProjectUrl: brandKit.Website!,
            TargetKeyword: targetKeyword,
            Department: "marketing",
            SiteName: brandKit.CompanyName!,
            DetectedTone: "Professional, consultative",
            DetectedFocus: targetKeyword,
            CrawledHeadings: [],
            CrawledParagraphs: paragraphs,
            JsonLdStructuredSummary: null,
            KeywordSources: [],
            PeopleAlsoAskQuestions: SplitLines(fields.PaaQuestions).ToList(),
            PublisherName: brandKit.CompanyName!,
            PublisherLogoUrl: _company.PublisherLogoUrl,
            AuthorName: _company.AuthorName,
            ArticleBaseUrl: brandKit.Website!,
            BlogBaseUrl: _company.BlogBaseUrl,
            ToolBaseUrl: _company.ToolBaseUrl,
            ImplementerPositioning: FirstNonEmpty(brandKit.PositioningOneLiner, _company.ImplementerPositioning)!,
            Provider: provider,
            UseExactKeywordAsTitle: false,
            DesiredHeadings: null,
            MatchedUseCase: null,
            AudienceSegment: NullIfEmpty(fields.AudienceSegment),
            AudienceDetails: fields.AudienceDetails.Count == 0 ? null : fields.AudienceDetails,
            AudienceNotes: NullIfEmpty(fields.AudienceNotes),
            ContentAngle: NullIfEmpty(fields.Angle),
            PrimaryIntent: NullIfEmpty(fields.PrimaryIntent),
            SecondaryIntent: NullIfEmpty(fields.SecondaryIntent),
            BuyingStage: NullIfEmpty(fields.BuyingStage),
            ToneOfVoice: NullIfEmpty(fields.ToneOfVoice),
            EeatSignals: fields.EeatSignals.Count == 0 ? null : fields.EeatSignals,
            CtaType: NullIfEmpty(fields.CtaType),
            CtaLabel: NullIfEmpty(fields.CtaLabel),
            LengthBand: NullIfEmpty(fields.LengthBand),
            WritingNotes: NullIfEmpty(fields.WritingNotes));
    }

    /// <summary>
    /// Per-section layer: injects this section's assigned <paramref name="job"/> (the PLAN-stage
    /// "problem" | "advance" role, so WRITE cannot let every section restate the same problem/
    /// solution) and its <paramref name="hierarchyChildHeadings"/> subset into the writing notes —
    /// without touching <c>ContentPromptBuilder</c> itself.
    /// </summary>
    public ProjectGenerationContext WithSectionAssignment(
        ProjectGenerationContext context,
        string sectionHeading,
        string? job,
        IReadOnlyList<string>? hierarchyChildHeadings)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.WritingNotes))
        {
            notes.Add(context.WritingNotes);
        }

        if (!string.IsNullOrWhiteSpace(job))
        {
            notes.Add(job.Equals("problem", StringComparison.OrdinalIgnoreCase)
                ? $"This section (\"{sectionHeading}\") is the ONE place in this piece that establishes the core practitioner problem — do not assume any other section has already stated it."
                : $"This section (\"{sectionHeading}\") must ADVANCE the argument past the problem already established elsewhere in this piece — do not re-open with the same pain point or the same fix already covered in earlier sections. Assume the reader already knows the problem; move to new ground (a distinct sub-topic, workflow step, or consideration).");
        }

        if (hierarchyChildHeadings is { Count: > 0 })
        {
            notes.Add("This section only — must-mention subtopics assigned here (do not cover subtopics assigned to other sections): "
                + string.Join(", ", hierarchyChildHeadings));
        }

        var writingNotes = notes.Count == 0 ? context.WritingNotes : string.Join("\n", notes);
        return context with { WritingNotes = writingNotes };
    }

    private static List<string> BuildNotesParagraphs(
        BriefFields fields,
        GccV2BrandKitContent kit,
        SiteSectionContextDto? siteSection)
    {
        var paragraphs = new List<string>();

        var serpTitles = SplitLines(fields.SerpTitles);
        if (serpTitles.Count > 0)
        {
            paragraphs.Add("Curated SERP titles: " + string.Join(" | ", serpTitles));
        }

        var related = SplitLines(fields.RelatedSearches);
        if (related.Count > 0)
        {
            paragraphs.Add("Related searches: " + string.Join(", ", related));
        }

        if (!string.IsNullOrWhiteSpace(kit.CompanyName))
        {
            paragraphs.Add($"Company: {kit.CompanyName}");
        }

        if (!string.IsNullOrWhiteSpace(kit.CompanyDescription))
        {
            paragraphs.Add($"About the company (from its own site): {kit.CompanyDescription}");
        }

        if (!string.IsNullOrWhiteSpace(kit.PositioningOneLiner))
        {
            paragraphs.Add($"Positioning: {kit.PositioningOneLiner}");
        }

        if (kit.Features.Count > 0)
        {
            paragraphs.Add("Services/features this company actually offers: " + string.Join(", ", kit.Features));
        }

        if (kit.KnowsAbout.Count > 0)
        {
            paragraphs.Add("Topics this company is known for: " + string.Join(", ", kit.KnowsAbout));
        }

        if (kit.VoiceGuidance.Count > 0)
        {
            paragraphs.Add("Brand voice guidance (provisional, derived from the site's own copy): "
                + string.Join(" ", kit.VoiceGuidance));
        }

        if (kit.CtaPhrases.Count > 0)
        {
            paragraphs.Add("Preferred CTA phrasing already used on the site: " + string.Join(", ", kit.CtaPhrases));
        }

        paragraphs.Add(
            "Write as a coherent story for this use-case: situation and stakes, how practitioners solve it, "
            + "where partner tools fit in context, then a clear close. Do not write a listicle or a dedicated "
            + "\"Top N tools\" / roundup section — weave tool mentions as inline anchors in prose when natural. "
            + "Never use a partner product name as a section heading; if a section title is a product name, "
            + "treat that as a mistake — write narrative advancing the use-case and mention that product once inline.");

        // Soft site-belonging: matched use-case (e.g. H5 under AI Use Cases) + recommended tools (H6 / links).
        if (!string.IsNullOrWhiteSpace(fields.MatchedHeading))
        {
            var where = fields.HierarchyPath is { Count: > 0 }
                ? " (on-site path: " + string.Join(" › ", fields.HierarchyPath) + ")"
                : "";
            paragraphs.Add(
                $"This piece belongs to the site's use-case heading \"{fields.MatchedHeading}\"{where}. "
                + "Relate the article to that use-case so it feels like part of this site — not a standalone generic essay.");
        }

        var partnerTools = MergePartnerTools(fields.RecommendedTools, fields.OperatorTools);
        if (partnerTools.Count > 0)
        {
            var toolList = string.Join(" | ", partnerTools.Select(t =>
                string.IsNullOrWhiteSpace(t.Href) ? t.Name : $"{t.Name} <{t.Href}>"));
            paragraphs.Add(
                "Partner / recommended tools allowlist (prospective partners from the site use-case and/or "
                + "operator-supplied URLs). When discussing tools or solutions, weave several of these into the "
                + "story with real hrefs; do not invent unrelated tools; do not open a separate tools roundup section: "
                + toolList);
        }

        if (siteSection?.RelatedPages is { Count: > 0 } pages)
        {
            // Prefer use-case / methodology / non-tool pages for internal links. Tool URLs from the
            // crawl-wide bag must not compete with the partner allowlist above.
            var ordered = PreferNonToolInternalPages(pages);
            if (partnerTools.Count > 0)
            {
                paragraphs.Add(
                    "Internal link candidates for non-tool site pages (use-case, methodology, etc.). "
                    + "Do not use these as tool/partner links — those must come from the allowlist above: "
                    + string.Join(" | ", ordered.Select(p => $"{p.Title} <{p.Url}>")));
            }
            else
            {
                paragraphs.Add(
                    "Internal link candidates from this site's related pages: "
                    + string.Join(" | ", ordered.Select(p => $"{p.Title} <{p.Url}>")));
            }

            foreach (var page in ordered.Take(5))
            {
                if (!string.IsNullOrWhiteSpace(page.Excerpt))
                    paragraphs.Add($"From {page.Url}: {page.Excerpt}");
            }
        }

        if (siteSection?.TopicalNeighbors is { Count: > 0 } neighbors)
        {
            paragraphs.Add("Topical neighbors on this site: " + string.Join(", ", neighbors));
        }

        return paragraphs;
    }

    private static IReadOnlyList<RecommendedTool> MergePartnerTools(
        IReadOnlyList<RecommendedTool> recommended,
        IReadOnlyList<RecommendedTool> operatorTools)
    {
        var merged = new List<RecommendedTool>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in recommended.Concat(operatorTools))
        {
            if (string.IsNullOrWhiteSpace(t.Name) && string.IsNullOrWhiteSpace(t.Href)) continue;
            var key = !string.IsNullOrWhiteSpace(t.Href) ? t.Href! : t.Name;
            if (!seen.Add(key)) continue;
            var name = string.IsNullOrWhiteSpace(t.Name)
                ? (t.Href ?? "tool")
                : t.Name;
            merged.Add(new RecommendedTool(name.Trim(), string.IsNullOrWhiteSpace(t.Href) ? null : t.Href!.Trim()));
        }

        return merged;
    }

    private static IReadOnlyList<RelatedPageDto> PreferNonToolInternalPages(IReadOnlyList<RelatedPageDto> pages) =>
        pages
            .OrderByDescending(p => NonToolInternalScore(p.Url, p.Title))
            .ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static int NonToolInternalScore(string? url, string? title)
    {
        var u = (url ?? "").ToLowerInvariant();
        var t = (title ?? "").ToLowerInvariant();
        var score = 0;
        if (u.Contains("/use-case") || u.Contains("/usecase") || u.Contains("ai-use")) score += 50;
        if (u.Contains("/methodolog") || t.Contains("methodolog")) score += 40;
        if (u.Contains("/integration") || t.Contains("integration")) score += 30;
        if (t.Contains("clone yourself") || t.Contains("consultation")) score += 20;
        // Demote crawl-wide tool pages so they don't steal partner-link slots.
        if (u.Contains("/tool")) score -= 40;
        return score;
    }

    private BriefFields ParseBriefFields(string rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson))
        {
            return new BriefFields();
        }

        try
        {
            var raw = JsonSerializer.Deserialize<RawBrief>(rawBriefJson, BriefJsonOpts);
            if (raw is null)
            {
                return new BriefFields();
            }

            var (matchedHeading, hierarchyPath, recommendedTools) = ParseHierarchyPlan(rawBriefJson);
            var operatorTools = ParseOperatorTools(rawBriefJson);

            return new BriefFields
            {
                PrimaryIntent = raw.PrimaryIntent ?? "",
                SecondaryIntent = raw.SecondaryIntent ?? "",
                BuyingStage = raw.BuyingStage ?? "",
                AudienceSegment = raw.AudienceSegment ?? "",
                AudienceDetails = raw.AudienceDetails ?? [],
                AudienceNotes = raw.AudienceNotes ?? "",
                Angle = raw.Angle ?? "",
                CtaType = raw.CtaType ?? "",
                CtaLabel = raw.CtaLabel ?? "",
                ToneOfVoice = raw.ToneOfVoice ?? "",
                EeatSignals = raw.EeatSignals ?? [],
                LengthBand = raw.LengthBand ?? "",
                WritingNotes = raw.WritingNotes ?? "",
                SerpTitles = raw.SerpTitles ?? "",
                SerpUrls = raw.SerpUrls ?? "",
                PaaQuestions = raw.PaaQuestions ?? "",
                RelatedSearches = raw.RelatedSearches ?? "",
                MatchedHeading = matchedHeading,
                HierarchyPath = hierarchyPath,
                RecommendedTools = recommendedTools,
                OperatorTools = operatorTools,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse GccV2Brief.RawBriefJson; writing with an empty brief.");
            return new BriefFields();
        }
    }

    private static (string? MatchedHeading, IReadOnlyList<string> Path, IReadOnlyList<RecommendedTool> Tools)
        ParseHierarchyPlan(string rawBriefJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("hierarchyPlan", out var plan)
                || plan.ValueKind != JsonValueKind.Object)
                return (null, [], []);

            string? matched = null;
            if (plan.TryGetProperty("matchedHeading", out var mh) && mh.ValueKind == JsonValueKind.String)
                matched = mh.GetString();

            var path = new List<string>();
            if (plan.TryGetProperty("path", out var pathEl) && pathEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in pathEl.EnumerateArray())
                {
                    if (p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                        path.Add(p.GetString()!.Trim());
                }
            }

            var tools = new List<RecommendedTool>();
            if (plan.TryGetProperty("recommendedTools", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in toolsEl.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var name = t.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var href = t.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String
                        ? h.GetString()
                        : null;
                    tools.Add(new RecommendedTool(name.Trim(), string.IsNullOrWhiteSpace(href) ? null : href.Trim()));
                }
            }

            // Do not fall back to "Top … Tools" child heading labels as tool names — those are
            // roundup labels, not partners. Partner names come from link extract (+ operator URLs).

            return (matched, path, tools);
        }
        catch (JsonException)
        {
            return (null, [], []);
        }
    }

    private static IReadOnlyList<RecommendedTool> ParseOperatorTools(string rawBriefJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("operatorTools", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return [];

            var tools = new List<RecommendedTool>();
            foreach (var t in arr.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String)
                {
                    var url = t.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    tools.Add(new RecommendedTool(GuessToolName(url), url));
                    continue;
                }

                if (t.ValueKind != JsonValueKind.Object) continue;
                var href = t.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String
                    ? u.GetString()
                    : t.TryGetProperty("href", out var h) && h.ValueKind == JsonValueKind.String
                        ? h.GetString()
                        : null;
                var name = t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(href) && string.IsNullOrWhiteSpace(name)) continue;
                tools.Add(new RecommendedTool(
                    string.IsNullOrWhiteSpace(name) ? GuessToolName(href!) : name!.Trim(),
                    string.IsNullOrWhiteSpace(href) ? null : href!.Trim()));
            }

            return tools;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GuessToolName(string url)
    {
        try
        {
            var path = new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
                ? new Uri(url).AbsolutePath
                : url;
            var segment = path.Trim('/').Split('/').LastOrDefault() ?? url;
            return string.IsNullOrWhiteSpace(segment) ? url : segment.Replace('-', ' ');
        }
        catch (UriFormatException)
        {
            return url;
        }
    }

    private static IReadOnlyList<string> SplitLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Wire shape of <c>ContentBrief</c> (content-creator-v2 frontend's
    /// <c>brief-catalog.ts</c>) as stored verbatim in <see cref="GccV2BriefDto.RawBriefJson"/>.</summary>
    private sealed record RawBrief(
        int? BriefVersion,
        string? PrimaryIntent,
        string? SecondaryIntent,
        string? BuyingStage,
        string? AudienceSegment,
        List<string>? AudienceDetails,
        string? AudienceNotes,
        string? Angle,
        string? CtaType,
        string? CtaLabel,
        string? ToneOfVoice,
        List<string>? EeatSignals,
        string? LengthBand,
        string? WritingNotes,
        string? SerpTitles,
        string? SerpUrls,
        string? PaaQuestions,
        string? RelatedSearches);

    private sealed class BriefFields
    {
        public string PrimaryIntent { get; init; } = "";
        public string SecondaryIntent { get; init; } = "";
        public string BuyingStage { get; init; } = "";
        public string AudienceSegment { get; init; } = "";
        public List<string> AudienceDetails { get; init; } = [];
        public string AudienceNotes { get; init; } = "";
        public string Angle { get; init; } = "";
        public string CtaType { get; init; } = "";
        public string CtaLabel { get; init; } = "";
        public string ToneOfVoice { get; init; } = "";
        public List<string> EeatSignals { get; init; } = [];
        public string LengthBand { get; init; } = "";
        public string WritingNotes { get; init; } = "";
        public string SerpTitles { get; init; } = "";
        public string SerpUrls { get; init; } = "";
        public string PaaQuestions { get; init; } = "";
        public string RelatedSearches { get; init; } = "";
        public string? MatchedHeading { get; init; }
        public IReadOnlyList<string> HierarchyPath { get; init; } = [];
        public IReadOnlyList<RecommendedTool> RecommendedTools { get; init; } = [];
        public IReadOnlyList<RecommendedTool> OperatorTools { get; init; } = [];
    }

    private sealed record RecommendedTool(string Name, string? Href);
}
