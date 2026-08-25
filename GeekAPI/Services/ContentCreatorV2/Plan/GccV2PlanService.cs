using System.Text.Json;
using GeekAPI.HttpClients;

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

    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2PlanService> _logger;

    public GccV2PlanService(HttpGccV2Repository repo, ILogger<GccV2PlanService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GccV2PlanOutline> BuildOutlineAsync(GccV2JobDto job, GccV2BriefDto brief, CancellationToken ct)
    {
        var childHeadings = ExtractHierarchyChildHeadings(brief.RawBriefJson);
        var keyword = ResolveKeyword(brief);
        var contentType = string.IsNullOrWhiteSpace(job.ContentType)
            ? (string.IsNullOrWhiteSpace(brief.ContentType) ? "blog" : brief.ContentType)
            : job.ContentType;
        contentType = contentType.Trim().ToLowerInvariant();

        var (sectionDefs, headingsFromHierarchy) = BuildSectionDefinitions(contentType, keyword, childHeadings);
        var perSectionChildren = PartitionChildHeadings(childHeadings, sectionDefs.Count, headingsFromHierarchy);

        var sections = sectionDefs
            .Select((def, i) => new GccV2PlanOutlineSection(
                def.Key,
                def.Heading,
                i == 0 ? "problem" : "advance",
                perSectionChildren[i]))
            .ToList();

        var outline = new GccV2PlanOutline(sections, childHeadings);

        if (brief.Id != Guid.Empty)
        {
            try
            {
                await _repo.CreateOutlineAsync(
                    new CreateGccV2OutlineCommand(
                        brief.Id,
                        OutlineJson: JsonSerializer.Serialize(outline, JsonOpts),
                        HierarchyChildHeadingsJson: JsonSerializer.Serialize(childHeadings, JsonOpts)),
                    ct);
            }
            catch (Exception ex)
            {
                // PLAN's return value is what actually drives OutlineReady/WRITE — a failed
                // secondary persist of the versioned GccV2Outline row must never fail the stage.
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
    /// Content-type-aware default section headings. Pillar is the one type that will use the real
    /// hierarchy children as its H2 headings directly (grounding the piece in the site's own
    /// structure) when at least two are available; every other type keeps its generic template
    /// headings and instead gets the hierarchy children partitioned across them as must-mention
    /// subtopics (see <see cref="PartitionChildHeadings"/>).
    /// </summary>
    private static (List<(string Key, string Heading)> Defs, bool HeadingsFromHierarchy) BuildSectionDefinitions(
        string contentType, string keyword, List<string> childHeadings)
    {
        var title = Capitalize(keyword);

        switch (contentType)
        {
            case "pillar":
            {
                if (childHeadings.Count >= 2)
                {
                    var picked = childHeadings.Take(5).ToList();
                    var defs = new List<(string, string)> { ("opening", "Opening") };
                    defs.AddRange(MakeUniqueKeys(picked));
                    return (defs, true);
                }

                var defsGeneric = new List<(string, string)> { ("opening", "Opening") };
                defsGeneric.AddRange(BuildDeclarativeHeadings(title).Select((h, i) => ($"section-{i + 1}", h)));
                return (defsGeneric, false);
            }
            case "blog":
                return (new List<(string, string)>
                {
                    ("opening", "Opening"),
                    ("key-considerations", $"Key Considerations for {title}"),
                    ("next-steps", $"Next Steps for {title}"),
                }, false);
            case "tool":
                return (new List<(string, string)>
                {
                    ("overview", "Overview"),
                    ("key-capabilities", "Key Capabilities"),
                    ("implementation-considerations", "Implementation Considerations"),
                    ("when-to-use", "When to Use"),
                }, false);
            case "email":
            case "social":
            case "ads":
            case "image-prompt":
                return (new List<(string, string)> { ("body", "Body") }, false);
            default:
                return (new List<(string, string)> { ("overview", "Overview") }, false);
        }
    }

    private static List<string> BuildDeclarativeHeadings(string title) =>
    [
        $"Understanding {title}",
        $"Implementing {title}",
        $"Common Challenges with {title}",
        $"Best Practices for {title}",
    ];

    /// <summary>
    /// Distributes real hierarchy child headings round-robin across body sections as each
    /// section's must-mention subset — never repeated ("bag-dumped") on every section. Skips the
    /// lede-style first slot (Opening/Overview/Body) when there is more than one section, so
    /// must-mention subtopics land on the sections that actually advance the piece. No-op when the
    /// section headings were themselves already derived from these same children (pillar case) —
    /// there is nothing left one level deeper to attach.
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

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "This Topic";
        var trimmed = value.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }
}
