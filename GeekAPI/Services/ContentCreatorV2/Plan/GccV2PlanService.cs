using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.ContentCreatorV2.Partner;

namespace GeekAPI.Services.ContentCreatorV2.Plan;

/// <summary>One PLAN-stage body section — the contract WRITE's <c>GccV2WriteService.LoadOutlineAsync</c>
/// parses (<c>key</c>/<c>heading</c>/<c>job</c>/<c>hierarchyChildHeadings</c>). Do not rename these
/// properties without updating that parser.</summary>
public sealed record GccV2PlanOutlineSection(string Key, string Heading, string Job, List<string> HierarchyChildHeadings);

/// <summary>PLAN-stage outline payload — persisted as the "plan" stage result's <c>OutputJson</c>,
/// mirrored onto the brief's <see cref="GccV2OutlineDto"/>, and emitted verbatim as the
/// <c>OutlineReady</c> job event.</summary>
public sealed record GccV2PlanOutline(List<GccV2PlanOutlineSection> Sections, List<string> HierarchyChildHeadings);

/// <summary>
/// Builds the real PLAN-stage outline, replacing the old hardcoded 3-section stub. Grounds body
/// sections in the site's real page-section hierarchy when <c>GccV2Controller.Generate</c> already
/// prefetched a hierarchy match onto the brief (<c>hierarchyPlan.childHeadings</c> in
/// <see cref="GccV2BriefDto.RawBriefJson"/> — see Generate's <c>TryMergeHierarchyPlanAsync</c>);
/// otherwise falls back to content-type-aware generic section templates. The job worker has no
/// user bearer, so PLAN itself never calls Site Analyzer — all hierarchy data it can use was
/// already fetched (with the caller's bearer) and persisted onto the brief at Generate time.
/// </summary>
public sealed class GccV2PlanService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Homepage H6 roundup labels (e.g. "Top AI Chatbot Tools:") — never promote to outline H2s.</summary>
    private static readonly Regex ToolRoundupHeading = new(
        @"^\s*top\b|\btools?\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2PlanService> _logger;

    public GccV2PlanService(HttpGccV2Repository repo, ILogger<GccV2PlanService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GccV2PlanOutline> BuildOutlineAsync(
        GccV2JobDto job,
        GccV2BriefDto brief,
        CancellationToken ct,
        IReadOnlyList<string>? childHeadingsOverride = null,
        bool preferSiteStructure = false,
        int regenerateVariant = 0)
    {
        var childHeadings = childHeadingsOverride is { Count: > 0 }
            ? childHeadingsOverride.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ExtractHierarchyChildHeadings(brief.RawBriefJson);
        var partnerToolNames = ExtractPartnerToolNames(brief.RawBriefJson);
        // Partner H6 names (Intercom, …) and "Top … Tools" labels never become outline H2s.
        var topicChildren = FilterOutlineHeadings(childHeadings, partnerToolNames);
        // Stale crawl / missing recommendedTools: short product-like children are still partners, not sections.
        if (partnerToolNames.Count == 0 && LookLikePartnerProductHeadings(topicChildren))
            topicChildren = [];
        var keyword = ResolveKeyword(brief);
        var contentType = string.IsNullOrWhiteSpace(job.ContentType)
            ? (string.IsNullOrWhiteSpace(brief.ContentType) ? "blog" : brief.ContentType)
            : job.ContentType;
        contentType = contentType.Trim().ToLowerInvariant();

        var (sectionDefs, headingsFromHierarchy) = BuildSectionDefinitions(
            contentType, keyword, topicChildren, preferSiteStructure, regenerateVariant, partnerToolNames,
            brief.RawBriefJson);
        sectionDefs = DedupeByHeading(sectionDefs);
        var perSectionChildren = PartitionChildHeadings(topicChildren, sectionDefs.Count, headingsFromHierarchy);
        // MUST-mention partner tools — distributed across sections (never as H2s).
        var perSectionTools = PartitionChildHeadings(partnerToolNames, sectionDefs.Count, headingsFromHierarchy: false);

        var sections = sectionDefs
            .Select((def, i) =>
            {
                var must = new List<string>(perSectionChildren[i]);
                foreach (var tool in perSectionTools[i])
                {
                    if (!must.Contains(tool, StringComparer.OrdinalIgnoreCase))
                        must.Add(tool);
                }

                return new GccV2PlanOutlineSection(
                    def.Key,
                    def.Heading,
                    i == 0 ? "problem" : "advance",
                    must);
            })
            .ToList();

        if (contentType is "tool")
        {
            var toolsHeading = $"Tools for {keyword}";
            if (!sections.Any(s => s.Heading.Contains("Tools for", StringComparison.OrdinalIgnoreCase)))
            {
                sections.Add(new GccV2PlanOutlineSection(
                    "tools-index",
                    toolsHeading,
                    "advance",
                    partnerToolNames.ToList()));
            }
        }

        if (GccV2LongFormTypes.ExpectsFaqSection(contentType))
        {
            var paaQuestions = ExtractPaaQuestions(brief.RawBriefJson, keyword, partnerToolNames);
            sections.Add(new GccV2PlanOutlineSection(
                "people-also-ask",
                "People Also Ask",
                "faq",
                paaQuestions));
        }

        var outline = new GccV2PlanOutline(sections, topicChildren);

        if (brief.Id != Guid.Empty)
        {
            try
            {
                await _repo.CreateOutlineAsync(
                    new CreateGccV2OutlineCommand(
                        brief.Id,
                        OutlineJson: JsonSerializer.Serialize(outline, JsonOpts),
                        HierarchyChildHeadingsJson: JsonSerializer.Serialize(topicChildren, JsonOpts)),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist GccV2Outline for brief {BriefId}; PLAN continues with the in-memory outline.", brief.Id);
            }
        }

        return outline;
    }

    /// <summary>Real sub-topics from the site's hierarchy match, prefetched at Generate time
    /// (Generate has the user's bearer; the worker does not). Absent/unparsable → empty, and PLAN
    /// falls back to content-type templates below.</summary>
    private static List<string> ExtractHierarchyChildHeadings(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("hierarchyPlan", out var plan) || plan.ValueKind != JsonValueKind.Object)
                return [];
            if (!plan.TryGetProperty("childHeadings", out var children) || children.ValueKind != JsonValueKind.Array)
                return [];

            return children.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static List<string> ExtractPartnerToolNames(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddName(string? name)
            {
                var n = (name ?? "").Trim();
                if (n.Length == 0 || !seen.Add(n)) return;
                names.Add(n);
            }

            if (doc.RootElement.TryGetProperty("hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object
                && plan.TryGetProperty("recommendedTools", out var tools)
                && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    if (t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    {
                        var name = n.GetString();
                        if (IsRejectedOutlineName(name)) continue;
                        AddName(name);
                    }
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsRejectedOutlineName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return true;
        return n.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || n.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExtractPaaQuestions(
        string? rawBriefJson,
        string keyword,
        IReadOnlyList<string> partnerToolNames)
    {
        var fromBrief = new List<string>();
        if (!string.IsNullOrWhiteSpace(rawBriefJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBriefJson);
                if (doc.RootElement.TryGetProperty("paaQuestions", out var paa)
                    && paa.ValueKind == JsonValueKind.String)
                {
                    foreach (var line in paa.GetString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (line.Length > 0) fromBrief.Add(line);
                    }
                }
            }
            catch (JsonException)
            {
                // fall through to fallback
            }
        }

        if (fromBrief.Count > 0)
            return fromBrief.Take(12).ToList();

        var title = Capitalize(keyword);
        var fallback = new List<string> { $"What is {title}?" };
        foreach (var tool in partnerToolNames.Take(3))
            fallback.Add($"How does {tool} help with {title}?");
        fallback.Add($"What should teams prioritize for {title}?");
        return fallback.Take(5).ToList();
    }

    private static List<string> ExtractRecommendedToolNames(string? rawBriefJson) =>
        ExtractPartnerToolNames(rawBriefJson);

    private static string ResolveKeyword(GccV2BriefDto brief)
    {
        if (!string.IsNullOrWhiteSpace(brief.TargetKeyword)) return brief.TargetKeyword.Trim();

        if (!string.IsNullOrWhiteSpace(brief.RawBriefJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(brief.RawBriefJson);
                if (doc.RootElement.TryGetProperty("title", out var t)
                    && t.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(t.GetString()))
                {
                    return t.GetString()!.Trim();
                }
            }
            catch (JsonException)
            {
                // fall through to default below
            }
        }

        return "this topic";
    }

    /// <summary>
    /// Content-type-aware section headings. When site hierarchy children are available (or
    /// <paramref name="preferSiteStructure"/> on regenerate), blog/pillar use those as H2s
    /// (Writesonic-style site structure). Otherwise fall back to narrative templates. No literal
    /// "Opening" H2 — lede is WRITE's document lede, not an outline slot.
    /// </summary>
    private static (List<(string Key, string Heading)> Defs, bool HeadingsFromHierarchy) BuildSectionDefinitions(
        string contentType,
        string keyword,
        List<string> childHeadings,
        bool preferSiteStructure,
        int regenerateVariant,
        IReadOnlyList<string> partnerToolNames,
        string? rawBriefJson)
    {
        var title = Capitalize(keyword);
        var useHierarchy = childHeadings.Count >= 2
            && (preferSiteStructure || contentType is "pillar" or "blog" or "tool"
                or GccV2LongFormTypes.Guide or GccV2LongFormTypes.Comparison);

        if (useHierarchy && contentType is "pillar" or "blog" or "tool"
            or GccV2LongFormTypes.Guide or GccV2LongFormTypes.Comparison)
        {
            var max = contentType switch
            {
                "tool" => 4,
                GccV2LongFormTypes.Guide => 8,
                _ => 5,
            };
            var picked = childHeadings.Take(max).ToList();
            return (MakeUniqueKeys(picked).ToList(), true);
        }

        switch (contentType)
        {
            case GccV2LongFormTypes.Comparison:
            {
                var options = ExtractComparisonOptionNames(keyword, childHeadings, partnerToolNames, rawBriefJson);
                return (DedupeByHeading(
                [
                    ("criteria", $"How We Evaluated {title} Options"),
                    ..options.Select((o, i) => ($"option-{i + 1}", o)),
                    ("verdict", $"Which {title} Option Fits Best"),
                ]), false);
            }
            case GccV2LongFormTypes.CaseStudy:
                return (DedupeByHeading(
                [
                    ("context", "Client Context"),
                    ("challenge", "Challenge"),
                    ("approach", "Approach"),
                    ("implementation", "Implementation"),
                    ("results", "Results"),
                    ("lessons", "Lessons Learned"),
                ]), false);
            case GccV2LongFormTypes.Guide:
            {
                var steps = childHeadings.Count >= 2
                    ? childHeadings.Take(8).Select((h, i) => ($"step-{i + 1}", h)).ToList()
                    : Enumerable.Range(1, 5).Select(i => ($"step-{i}", $"Step {i}: {title}")).ToList();
                return (DedupeByHeading(
                [
                    ("prerequisites", "Prerequisites and What You'll Learn"),
                    ..steps,
                ]), childHeadings.Count >= 2);
            }
            case GccV2LongFormTypes.Alternatives:
            {
                var alts = partnerToolNames.Count > 0
                    ? partnerToolNames.Take(6).ToList()
                    : ["Alternative 1", "Alternative 2", "Alternative 3"];
                return (DedupeByHeading(
                [
                    ("why-alternatives", $"Why Teams Seek Alternatives to {title}"),
                    ..alts.Select((a, i) => ($"alt-{i + 1}", a)),
                ]), false);
            }
            case GccV2LongFormTypes.TechArticle:
                return (DedupeByHeading(
                [
                    ("architecture", $"Architecture Overview for {title}"),
                    ("components", "Core Components"),
                    ("integration", "Integration Patterns"),
                    ("tradeoffs", "Tradeoffs and Decisions"),
                    ("operations", "Operations and Maintenance"),
                ]), false);
            case GccV2LongFormTypes.Listicle:
                return (DedupeByHeading(
                [
                    ("intro", $"Top Picks for {title}"),
                    ("item-1", $"#1 — {title} Option One"),
                    ("item-2", $"#2 — {title} Option Two"),
                    ("item-3", $"#3 — {title} Option Three"),
                    ("item-4", $"#4 — {title} Option Four"),
                    ("item-5", $"#5 — {title} Option Five"),
                    ("verdict", "Final Verdict"),
                ]), false);
            case GccV2LongFormTypes.Service:
                return (DedupeByHeading(
                [
                    ("who-for", "Who This Service Is For"),
                    ("deliverables", "What You Get"),
                    ("process", "Our Process"),
                    ("proof", "Proof and Outcomes"),
                    ("next-step", "Next Step"),
                ]), false);
            case GccV2LongFormTypes.Local:
                return (DedupeByHeading(
                [
                    ("area", "Service Area"),
                    ("offer", "What We Offer Locally"),
                    ("proof", "Local Proof"),
                    ("faq-local", "Local FAQ"),
                ]), false);
            case GccV2LongFormTypes.Whitepaper:
                return (DedupeByHeading(
                [
                    ("executive-summary", "Executive Summary"),
                    ("problem-space", "Problem Space"),
                    ("methodology", "Methodology"),
                    ("findings", "Findings"),
                    ("recommendations", "Recommendations"),
                    ("cta", "Get the Full Report"),
                ]), false);
            case "pillar":
            {
                var defsGeneric = BuildDeclarativeHeadings(title, regenerateVariant)
                    .Select((h, i) => ($"section-{i + 1}", h))
                    .ToList();
                return (defsGeneric, false);
            }
            case "blog":
                if (regenerateVariant % 2 == 1)
                {
                    return (DedupeByHeading(
                    [
                        ("situation", $"The Situation Around {title}"),
                        ("what-changed", $"What Changed for {title}"),
                        ("how-to-respond", $"How to Respond on {title}"),
                        ("checklist", $"Practical Checklist for {title}"),
                    ]), false);
                }

                return (DedupeByHeading(
                [
                    ("situation", $"Why {title} Matters Now"),
                    ("key-considerations", $"How Teams Approach {title}"),
                    ("next-steps", $"Next Steps for {title}"),
                ]), false);
            case "tool":
                return (DedupeByHeading(
                [
                    ("overview", "Overview"),
                    ("capabilities", "Capabilities"),
                    ("implementation", "Implementation"),
                    ("when-to-use", "When to Use"),
                ]), false);
            case GccV2ChannelTypes.LinkedInCarousel:
                return (DedupeByHeading(
                [
                    ("cover", $"Hook — {title}"),
                    ("problem", "Why This Matters Now"),
                    ("teach-1", "Insight 1"),
                    ("teach-2", "Insight 2"),
                    ("teach-3", "Insight 3"),
                    ("teach-4", "Insight 4"),
                    ("framework", "Mini-Playbook Summary"),
                    ("cta", "Your Next Step"),
                ]), false);
            case "email":
            case "social":
            case "ads":
            case "image-prompt":
                return ([("body", "Body")], false);
            default:
                return ([("overview", "Overview")], false);
        }
    }

    private static List<string> FilterOutlineHeadings(
        IEnumerable<string> headings,
        IReadOnlyList<string> partnerToolNames)
    {
        var partners = new HashSet<string>(
            partnerToolNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return headings
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Where(h => !IsToolRoundupHeading(h))
            .Where(h => !partners.Contains(h.TrimEnd(':')))
            .Where(h => !partners.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool IsToolRoundupHeading(string heading)
    {
        var t = heading.Trim().TrimEnd(':').Trim();
        if (t.Length == 0) return false;
        if (ToolRoundupHeading.IsMatch(t)) return true;
        // "Top 5 Automated Data Entry Processing Tools" style
        return Regex.IsMatch(t, @"^\s*top\s+\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               && t.Contains("tool", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when every heading looks like a product/partner name (1–3 words), not a topic sentence.</summary>
    private static bool LookLikePartnerProductHeadings(IReadOnlyList<string> headings)
    {
        if (headings.Count < 2) return false;
        foreach (var h in headings)
        {
            var t = h.Trim().TrimEnd(':').Trim();
            if (t.Length is 0 or > 36) return false;
            var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length is < 1 or > 3) return false;
            if (IsToolRoundupHeading(t)) return false;
        }

        return true;
    }

    private static List<(string Key, string Heading)> DedupeByHeading(
        IEnumerable<(string Key, string Heading)> defs)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, string)>();
        foreach (var def in defs)
        {
            var heading = (def.Heading ?? "").Trim();
            if (heading.Length == 0) continue;
            if (!seen.Add(heading)) continue;
            list.Add((def.Key, heading));
        }

        return list;
    }

    /// <summary>Five body H2s — row 0 becomes <c>problem</c>, rows 1–4 become <c>advance</c> (see PLAN loop).</summary>
    private static List<string> BuildDeclarativeHeadings(string title, int regenerateVariant = 0) =>
        regenerateVariant % 2 == 1
            ?
            [
                $"Why {title} Matters Now",
                $"How Teams Approach {title}",
                $"Risks and Tradeoffs with {title}",
                $"A Practical Path for {title}",
                $"Next Steps for {title}",
            ]
            :
            [
                $"Understanding {title}",
                $"Implementing {title}",
                $"Common Challenges with {title}",
                $"Best Practices for {title}",
                $"Measuring Success with {title}",
            ];

    /// <summary>
    /// Distributes real hierarchy child headings round-robin across body sections as each
    /// section's must-mention subset — never repeated ("bag-dumped") on every section. When
    /// section headings were themselves derived from these children, skip partitioning.
    /// </summary>
    private static List<List<string>> PartitionChildHeadings(
        List<string> childHeadings, int sectionCount, bool headingsFromHierarchy)
    {
        var buckets = Enumerable.Range(0, Math.Max(sectionCount, 0)).Select(_ => new List<string>()).ToList();
        if (childHeadings.Count == 0 || headingsFromHierarchy || sectionCount == 0)
            return buckets;

        var startIndex = sectionCount > 1 ? 1 : 0;
        var span = sectionCount - startIndex;
        for (var i = 0; i < childHeadings.Count; i++)
        {
            var bucket = span <= 0 ? 0 : startIndex + (i % span);
            buckets[bucket].Add(childHeadings[i]);
        }

        return buckets;
    }

    private static IEnumerable<(string Key, string Heading)> MakeUniqueKeys(IReadOnlyList<string> headings)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headings.Count; i++)
        {
            var key = Slugify(headings[i], i + 1);
            var unique = key;
            var suffix = 2;
            while (!seen.Add(unique))
                unique = $"{key}-{suffix++}";
            yield return (unique, headings[i]);
        }
    }

    private static string Slugify(string heading, int fallbackIndex)
    {
        var chars = (heading ?? "").ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-')
            .ToArray();
        var slug = new string(chars).Replace(' ', '-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        slug = slug.Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? $"section-{fallbackIndex}" : slug;
    }

    private static List<string> ExtractComparisonOptionNames(
        string keyword,
        IReadOnlyList<string> childHeadings,
        IReadOnlyList<string> partnerToolNames,
        string? rawBriefJson)
    {
        var options = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in partnerToolNames)
        {
            var n = name.Trim();
            if (n.Length == 0 || !seen.Add(n)) continue;
            options.Add(n);
        }

        foreach (var href in GccV2PartnerUrlResearchService.CollectCompetitorHrefs(rawBriefJson))
        {
            var label = CompetitorLabelFromHref(href);
            if (label.Length == 0 || !seen.Add(label)) continue;
            options.Add(label);
        }

        foreach (var h in childHeadings)
        {
            var n = h.Trim();
            if (n.Length == 0 || !seen.Add(n)) continue;
            options.Add(n);
        }

        if (options.Count >= 2) return options.Take(6).ToList();

        var title = Capitalize(keyword);
        return [$"{title} Option A", $"{title} Option B", $"{title} Option C"];
    }

    private static string CompetitorLabelFromHref(string href)
    {
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return href.Trim();
        var host = uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
        var segment = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(segment) && segment.Length > 2)
            return char.ToUpperInvariant(segment[0]) + segment[1..].Replace('-', ' ');
        return char.ToUpperInvariant(host[0]) + host[1..].Split('.')[0];
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "This Topic";
        var trimmed = value.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
