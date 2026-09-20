using System.Text.RegularExpressions;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Sections of a crawled site whose heading matches a target keyword.
///
/// v1's own matcher, over <see cref="SiteStructure"/> — the tree GeekCrawlerSiteStructure builds
/// from the crawler's typed blocks. v1 previously reached into ContentCreatorV2 for this; it no
/// longer does.
///
/// Matching is deliberately strict: exact slug or text, or a prefix match when both sides carry at
/// least two tokens. Never plain containment — that is how a heading called "Marketing" swallows
/// every keyword on the site.
/// </summary>
public static class GccSiteStructureMatch
{
    /// <param name="Context">
    /// The text of the block the anchor appeared in. Not part of the anchor — the crawler records
    /// only <c>{ label, href }</c> — but it is the only thing that says what the link is about.
    /// </param>
    public sealed record ToolRow(string Name, string? Href, string Context);

    public sealed record MatchResult(
        string MatchedHeading,
        string[] Path,
        string Kind,
        string[] ChildHeadings,
        IReadOnlyList<ToolRow> RecommendedTools,
        string MatchTopic,
        string? SourcePageUrl);

    /// <summary>
    /// Every match, ordered best-first. Nothing is deduplicated.
    ///
    /// Duplicates are a crawl defect the caller reports — the same page stored under two hostnames,
    /// or two responsive copies of one section both indexed. Collapsing them here would hide the
    /// defect and hand the operator a clean list that is quietly wrong.
    /// </summary>
    public static IReadOnlyList<MatchResult> MatchAll(SiteStructure? structure, IEnumerable<string> seeds)
    {
        if (structure is null || structure.Pages.Count == 0) return [];

        var topics = ExpandSeeds(seeds).ToList();
        if (topics.Count == 0) return [];

        var found = new List<(MatchCandidate Candidate, string Topic, string? PageUrl)>();

        foreach (var page in structure.Pages)
        {
            foreach (var (node, path) in Walk(page.Roots, []))
            {
                foreach (var topic in topics)
                {
                    var kind = Score(node.HeadingText, topic);
                    if (kind is null) continue;

                    found.Add((
                        new MatchCandidate(
                            node.HeadingText,
                            path.ToArray(),
                            kind,
                            ChildHeadings(node),
                            HarvestTools(node),
                            path.Count,
                            TokenCount(Slugify(node.HeadingText))),
                        topic,
                        page.PageUrl));
                }
            }
        }

        return found
            .OrderBy(f => KindRank(f.Candidate.Kind))
            .ThenByDescending(f => f.Candidate.Depth)
            .ThenByDescending(f => f.Candidate.HeadingTokens)
            .ThenByDescending(f => f.Candidate.Tools.Count)
            .Select(f => new MatchResult(
                f.Candidate.Heading,
                f.Candidate.Path,
                f.Candidate.Kind,
                f.Candidate.ChildHeadings,
                f.Candidate.Tools,
                f.Topic,
                f.PageUrl))
            .ToList();
    }

    /// <summary>Full phrase plus one strip of " for …" / dash. Never peels to a lone vertical word.</summary>
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
                // Require >= 2 tokens so a lone "Marketing" is never emitted as a seed.
                if (TokenCount(Slugify(prefix)) < 2) continue;
                if (seen.Add(prefix)) yield return prefix;
            }
        }
    }

    private static int KindRank(string kind) =>
        kind switch
        {
            "exact-heading" => 0,
            "near-exact-heading" => 1,
            _ => 9,
        };

    /// <summary>
    /// Exact slug/text, or near-exact when both sides have >= 2 tokens (prefix/starts-with).
    /// Never <c>topicSlug.Contains(shortParentSlug)</c>.
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

    /// <summary>The richest link group (>= 2) in this subtree: the node, its children, one level below.</summary>
    internal static IReadOnlyList<ToolRow> HarvestTools(SiteStructureNode node)
    {
        var groups = new List<IReadOnlyList<ToolRow>>();

        var own = FilterLinks(node.Links);
        if (own.Count >= 2) groups.Add(own);

        foreach (var child in node.Children)
        {
            var childLinks = FilterLinks(child.Links);
            if (childLinks.Count >= 2) groups.Add(childLinks);

            foreach (var grand in child.Children)
            {
                var g = FilterLinks(grand.Links);
                if (g.Count >= 2) groups.Add(g);
            }
        }

        if (groups.Count == 0)
        {
            // A single-link section is still worth returning when it is all the matched node has.
            if (own.Count > 0) return own;
            return [];
        }

        return groups.OrderByDescending(g => g.Count).First();
    }

    private static IReadOnlyList<ToolRow> FilterLinks(IReadOnlyList<SiteStructureLink> links)
    {
        var rows = new List<ToolRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var name = (link.Label ?? "").Replace('\n', ' ').Trim();
            if (name.Length == 0 || name.Length >= 80) continue;
            if (LooksLikeSiteChrome(name)) continue;
            if (!seen.Add(name)) continue;
            var href = string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim();
            // The prose the link sits in. "Learn more" says nothing on its own; the sentence
            // around it is what tells a writer what the tool actually is.
            rows.Add(new ToolRow(name, href, link.Context));
        }
        return rows;
    }

    private static string[] ChildHeadings(SiteStructureNode node) =>
        node.Children
            .Select(c => c.HeadingText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

    private static IEnumerable<(SiteStructureNode Node, List<string> Path)> Walk(
        IEnumerable<SiteStructureNode> nodes,
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
