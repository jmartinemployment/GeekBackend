using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreatorV2.Hierarchy;

/// <summary>
/// Tight heading match on CC mobile <see cref="GccV2SiteHierarchy"/> + structured <c>Links</c> harvest.
/// No parent-contains, no most-tools-wins, no markdown round-trip.
/// </summary>
public static class GccV2HierarchyToolMatch
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public sealed record ToolRow(string Name, string? Href);

    public sealed record MatchResult(
        string MatchedHeading,
        string[] Path,
        string Kind,
        string[] ChildHeadings,
        IReadOnlyList<ToolRow> RecommendedTools,
        string MatchTopic,
        string? SourcePageUrl);

    public static GccV2SiteHierarchy? TryParseSiteHierarchy(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("siteHierarchy", out var sh)
                || sh.ValueKind != JsonValueKind.Object)
                return null;
            return JsonSerializer.Deserialize<GccV2SiteHierarchy>(sh.GetRawText(), JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static MatchResult? Match(GccV2SiteHierarchy? hierarchy, IEnumerable<string> seeds)
    {
        if (hierarchy is null || hierarchy.Pages.Count == 0) return null;

        var topics = ExpandSeeds(seeds).ToList();
        if (topics.Count == 0) return null;

        MatchCandidate? best = null;
        string? bestTopic = null;

        foreach (var page in hierarchy.Pages)
        {
            foreach (var (node, path) in Walk(page.Roots, []))
            {
                foreach (var topic in topics)
                {
                    var kind = Score(node.HeadingText, topic);
                    if (kind is null) continue;

                    var tools = HarvestTools(node);
                    var candidate = new MatchCandidate(
                        node.HeadingText,
                        path.ToArray(),
                        kind,
                        ChildHeadings(node),
                        tools,
                        path.Count,
                        TokenCount(Slugify(node.HeadingText)));

                    if (IsBetter(candidate, best))
                    {
                        best = candidate;
                        bestTopic = topic;
                    }
                }
            }
        }

        if (best is null || bestTopic is null) return null;

        return new MatchResult(
            best.Heading,
            best.Path,
            best.Kind,
            best.ChildHeadings,
            best.Tools,
            bestTopic,
            hierarchy.Pages.FirstOrDefault()?.PageUrl ?? hierarchy.HomepageUrl);
    }

    /// <summary>Full phrase + one strip of " for …" / dash. Never peels to a lone vertical word.</summary>
    public static IEnumerable<string> ExpandSeeds(IEnumerable<string> seeds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in seeds)
        {
            var k = NormalizeHeading(seed);
            if (k.Length < 3) continue;
            if (seen.Add(k)) yield return k;

            foreach (var marker in new[] { " for ", " - ", " – ", " — " })
            {
                var idx = k.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx <= 0) continue;
                var prefix = NormalizeHeading(k[..idx]);
                // Require ≥2 tokens so we never emit a lone "Marketing".
                if (TokenCount(Slugify(prefix)) < 2) continue;
                if (seen.Add(prefix)) yield return prefix;
            }
        }
    }

    private static bool IsBetter(MatchCandidate candidate, MatchCandidate? current)
    {
        if (current is null) return true;

        var cRank = KindRank(candidate.Kind);
        var curRank = KindRank(current.Kind);
        if (cRank != curRank) return cRank < curRank;

        // Same kind: prefer deeper / longer heading (more specific), then more tools as tie-break.
        if (candidate.Depth != current.Depth) return candidate.Depth > current.Depth;
        if (candidate.HeadingTokens != current.HeadingTokens)
            return candidate.HeadingTokens > current.HeadingTokens;
        return candidate.Tools.Count > current.Tools.Count;
    }

    private static int KindRank(string kind) =>
        kind switch
        {
            "exact-heading" => 0,
            "near-exact-heading" => 1,
            _ => 9,
        };

    /// <summary>
    /// Exact slug/text, or near-exact when both sides have ≥2 tokens (prefix/starts-with).
    /// Never: topicSlug.Contains(shortParentSlug).
    /// </summary>
    internal static string? Score(string headingText, string topic)
    {
        var heading = NormalizeHeading(headingText);
        var topicNorm = NormalizeHeading(topic);
        if (heading.Length == 0 || topicNorm.Length == 0) return null;

        var headingSlug = Slugify(heading);
        var topicSlug = Slugify(topicNorm);
        if (headingSlug is "" or "tool" || topicSlug is "" or "tool") return null;

        if (string.Equals(headingSlug, topicSlug, StringComparison.OrdinalIgnoreCase)
            || string.Equals(heading, topicNorm, StringComparison.OrdinalIgnoreCase))
            return "exact-heading";

        var headingTokens = TokenCount(headingSlug);
        var topicTokens = TokenCount(topicSlug);
        // Both sides need enough substance so "marketing" cannot near-match via containment.
        if (headingTokens < 2 || topicTokens < 2) return null;

        if (headingSlug.StartsWith(topicSlug, StringComparison.OrdinalIgnoreCase)
            || topicSlug.StartsWith(headingSlug, StringComparison.OrdinalIgnoreCase))
            return "near-exact-heading";

        return null;
    }

    internal static IReadOnlyList<ToolRow> HarvestTools(GccV2HeadingNode node)
    {
        // Prefer the richest tool group (≥2) in this subtree: node itself, then each child group.
        var groups = new List<IReadOnlyList<ToolRow>>();

        var own = FilterLinks(node.Links);
        if (own.Count >= 2) groups.Add(own);

        foreach (var child in node.Children)
        {
            var childLinks = FilterLinks(child.Links);
            if (childLinks.Count >= 2) groups.Add(childLinks);

            // One level of grandchildren (tool list under an H6, etc.)
            foreach (var grand in child.Children)
            {
                var g = FilterLinks(grand.Links);
                if (g.Count >= 2) groups.Add(g);
            }
        }

        if (groups.Count == 0)
        {
            // Single-link sections still useful if that's all we have on the matched node.
            if (own.Count > 0) return own;
            return [];
        }

        return groups.OrderByDescending(g => g.Count).First();
    }

    private static IReadOnlyList<ToolRow> FilterLinks(IReadOnlyList<GccV2HeadingLink> links)
    {
        var rows = new List<ToolRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var name = (link.Text ?? "").Replace('\n', ' ').Trim();
            if (name.Length == 0 || name.Length >= 80) continue;
            if (LooksLikeSiteChrome(name)) continue;
            if (!seen.Add(name)) continue;
            var href = string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim();
            rows.Add(new ToolRow(name, href));
        }
        return rows;
    }

    private static string[] ChildHeadings(GccV2HeadingNode node) =>
        node.Children
            .Select(c => c.HeadingText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

    private static IEnumerable<(GccV2HeadingNode Node, List<string> Path)> Walk(
        IEnumerable<GccV2HeadingNode> nodes,
        List<string> path)
    {
        foreach (var node in nodes)
        {
            var next = new List<string>(path) { node.HeadingText };
            yield return (node, next);
            foreach (var child in Walk(node.Children, next))
                yield return child;
        }
    }

    private static string NormalizeHeading(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.Trim().TrimEnd(':').Trim();
    }

    internal static string Slugify(string value)
    {
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\s-]", "");
        s = Regex.Replace(s, @"[\s-]+", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? "tool" : s;
    }

    private static int TokenCount(string slug) =>
        string.IsNullOrEmpty(slug) || slug == "tool"
            ? 0
            : slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool LooksLikeSiteChrome(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n is "home" or "about" or "contact" or "login" or "sign in" or "sign up"
            or "privacy" or "terms" or "menu" or "skip to content";
    }

    private sealed record MatchCandidate(
        string Heading,
        string[] Path,
        string Kind,
        string[] ChildHeadings,
        IReadOnlyList<ToolRow> Tools,
        int Depth,
        int HeadingTokens);
}
