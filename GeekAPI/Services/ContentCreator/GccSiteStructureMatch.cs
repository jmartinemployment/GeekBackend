using System.Text.RegularExpressions;
using GeekAPI.Services.GeekCrawler;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Sections of a crawled site whose heading matches a target keyword.
///
/// Matches over <see cref="SiteStructure"/> — the tree GeekCrawlerSiteStructure builds from the
/// crawler's typed blocks.
///
/// Matching is deliberately strict: exact slug or text, or a prefix match when both sides carry at
/// least two tokens. Never plain containment — that is how a heading called "Marketing" swallows
/// every keyword on the site.
/// </summary>
public static partial class GccSiteStructureMatch
{
    /// <param name="Context">
    /// The text of the block the anchor appeared in. Not part of the anchor — the crawler records
    /// only <c>{ label, href }</c> — but it is the only thing that says what the link is about.
    /// </param>
    public sealed record ToolRow(string Name, string? Href, string Context);

    /// <param name="Level">
    /// The matched heading's own depth, h1-h6, as the crawler's block recorded it; 0 when the block
    /// carried none. Surfaced because a consumer that shows the model this section has to say which
    /// level it sits at — the structure held it all along and this record was dropping it, so the
    /// one caller that needs it was writing 0 for every match.
    /// </param>
    public sealed record MatchResult(
        string MatchedHeading,
        string[] Path,
        string Kind,
        string[] ChildHeadings,
        IReadOnlyList<ToolRow> RecommendedTools,
        string MatchTopic,
        string? SourcePageUrl,
        int Level,
        /// <summary>
        /// The matched section's own prose, as the crawler's blocks recorded it.
        ///
        /// <para>
        /// Surfaced for the same reason as <see cref="Level"/>: the structure held it and this record
        /// dropped it, so the one consumer that renders it into a brief was handed an empty list. It is
        /// what the site already says under this heading — the thing a new piece must cover without
        /// repeating.
        /// </para>
        /// </summary>
        IReadOnlyList<string> Paragraphs);

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
                            TokenCount(Slugify(node.HeadingText)),
                            node.Level,
                            node.Paragraphs),
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
                f.PageUrl,
                f.Candidate.Level,
                f.Candidate.Paragraphs))
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

            // A colon splits context from keyword the other way round: "Accounts Payable: Automated
            // Data Entry & Processing" is a page about the second half, filed under the first. Both
            // halves are seeded, because the site may file its own page under either -- and neither
            // half prefix-matches the whole, so without this the topic matched nothing at all.
            var colon = k.LastIndexOf(':');
            if (colon > 0 && colon < k.Length - 1)
            {
                foreach (var half in new[] { NormalizeHeading(k[..colon]), NormalizeHeading(k[(colon + 1)..]) })
                {
                    if (TokenCount(Slugify(half)) < 2) continue;
                    if (seen.Add(half)) yield return half;
                }
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
            var name = ResolveToolName(link.Label, link.Href);
            if (name is null) continue;
            if (!IsLikelyToolLink(name, link.Href)) continue;
            if (!seen.Add(name)) continue;
            var href = string.IsNullOrWhiteSpace(link.Href) ? null : link.Href.Trim();
            // The prose the link sits in. "Learn more" says nothing on its own; the sentence
            // around it is what tells a writer what the tool actually is.
            rows.Add(new ToolRow(name, href, link.Context));
        }
        return rows;
    }

    /// <summary>
    /// The product this anchor names, taking the name from the URL when the label does not carry one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A vague label is a naming problem, not evidence against the link.</b> <c>"Learn more"</c>
    /// pointing at <c>/tools/invoice-capture</c> is a real tool that the site labelled badly, and the
    /// href is its identity. Rejecting it outright — which this did until Jeff caught it — is the
    /// failure mode project-site crawls are warned about by name: tools and partners silently stop
    /// being found, no error, just fewer matches.
    /// </para>
    /// <para>
    /// So a CTA label is replaced by the URL's own last segment rather than discarded. The slug is the
    /// site's own words, not an invention, and nothing is guessed: if the segment is generic —
    /// <c>guide</c>, <c>index</c>, <c>blog</c> — there is no name to be had and the link is dropped.
    /// That is what separates <c>"Learn more"</c> → <c>/tools/invoice-capture</c> from
    /// <c>"read our comprehensive guide"</c> → <c>/blog/guide</c>: one resolves to a product, the
    /// other to a word that names nothing.
    /// </para>
    /// <para>
    /// This is distinct from <see cref="LooksLikeSiteChrome"/>, which names a <i>destination</i>:
    /// "Privacy Policy" and "Contact Us" say what is at the other end, and no href can make them a
    /// product. A CTA says nothing about the destination, so the destination gets to answer.
    /// </para>
    /// </remarks>
    internal static string? ResolveToolName(string? label, string? href)
    {
        var name = (label ?? "").Replace('\n', ' ').Trim();
        if (name.Length > 0 && !IsVagueCallToAction(name)) return name;

        var fromUrl = NameFromHref(href);
        return fromUrl;
    }

    /// <summary>A label that could sit on any link on the site. It names no destination.</summary>
    private static bool IsVagueCallToAction(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        ReadOnlySpan<string> needles =
        [
            "learn more", "read our", "read more", "click here", "find out more", "see more",
            "view details", "more info", "get started",
        ];
        foreach (var needle in needles)
        {
            if (n.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// The product name carried by a URL's last path segment, or null when it carries none.
    /// </summary>
    private static string? NameFromHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;

        string path;
        try
        {
            path = Uri.TryCreate(href, UriKind.Absolute, out var abs)
                ? abs.AbsolutePath
                : href.Split('?', 2)[0].Split('#', 2)[0];
        }
        catch (UriFormatException)
        {
            return null;
        }

        var segment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (segment is null) return null;

        var dot = segment.LastIndexOf('.');
        if (dot > 0) segment = segment[..dot];
        segment = segment.Trim('-', '_', ' ');
        if (segment.Length < 3) return null;

        // Words that name a kind of page rather than a product. A slug of only these resolves to
        // nothing, which is a cleaner answer than a tool called "Guide".
        ReadOnlySpan<string> generic =
        [
            "guide", "index", "home", "blog", "news", "article", "post", "page", "default",
            "overview", "more", "details", "resources", "insights", "case-study", "case-studies",
        ];
        foreach (var g in generic)
        {
            if (segment.Equals(g, StringComparison.OrdinalIgnoreCase)) return null;
        }

        var words = segment.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;

        return string.Join(' ', words.Select(w =>
            w.Length == 1 ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]));
    }

    /// <summary>
    /// Is this anchor a product, or is it the furniture that sits beside one?
    /// </summary>
    /// <remarks>
    /// <para>
    /// Site chrome, CTAs, legal links and phone numbers sit under the same heading as real partners.
    /// This test is the one the retired Site-Analyzer-shaped extractor carried, ported here on
    /// 2026-10-01 when that code was deleted, because it is stronger than what this matcher had and
    /// it was written against observed output: its note recorded <i>Privacy Policy</i>, <i>Call Us</i>
    /// and <i>Free Assessment</i> being returned as tools.
    /// </para>
    /// <para>
    /// What was here before matched ten whole labels — <c>home</c>, <c>about</c>, <c>privacy</c> and
    /// the like — so <i>"Privacy Policy"</i> and <i>"Call Us (561) 526-3512"</i> passed it untouched.
    /// They were excluded only when a larger group of real tools out-ranked them, which means a
    /// section whose nav block was bigger than its tool row shipped a phone number as a tool. Group
    /// ranking is a tie-break, not a filter, and it was doing a filter's job.
    /// </para>
    /// <para>
    /// No preference for <c>/tools/</c> paths, deliberately: a partner's own product page is a tool
    /// link and lives on their domain, so a path test would favour this site's pages over the
    /// partners the page is about.
    /// </para>
    /// </remarks>
    internal static bool IsLikelyToolLink(string name, string? href)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Replace('\n', ' ').Trim();
        if (name.Length == 0 || name.Length >= 80) return false;

        if (LooksLikeSiteChrome(name)) return false;

        // A phone number is never a product, and it reads as a plausible label until it is in a list
        // of tools.
        if (PhoneNumber().IsMatch(name)) return false;

        if (!string.IsNullOrWhiteSpace(href))
        {
            var h = href.Trim();
            // An in-page jump, a dial link, an email or a script handler cannot be a product page.
            if (h.StartsWith('#')
                || h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || h.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (LooksLikeSiteChromeHref(h)) return false;
        }

        // A product is named in a few words. A sentence, or anything quoted or questioning, is prose
        // that happens to be linked -- "read our comprehensive guide: \"How Chatbots ...\"".
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length is < 1 or > 5) return false;
        if (name.Contains('"') || name.Contains('\u201C') || name.Contains('?')) return false;

        return true;
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

    [GeneratedRegex(@"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}")]
    private static partial Regex PhoneNumber();

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

    /// <summary>
    /// Phrases that are furniture wherever they appear, matched as substrings.
    /// </summary>
    /// <remarks>
    /// Whole-label equality was the defect: it caught <c>privacy</c> and missed
    /// <i>"Privacy Policy"</i>, caught <c>contact</c> and missed <i>"Contact Us"</i>. These are the
    /// needles the retired extractor matched, plus the exact labels this list already held.
    ///
    /// <para>
    /// Every phrase here names a <b>destination</b>, so no href can redeem it. The retired extractor
    /// also listed "learn more", "read our" and "click here", which do not: they name nothing, and a
    /// link wearing one can still be a product. Those moved to
    /// <see cref="IsVagueCallToAction"/>, where the URL answers instead of the label.
    /// </para>
    /// </remarks>
    private static bool LooksLikeSiteChrome(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n is "home" or "about" or "contact" or "login" or "sign in" or "sign up"
            or "privacy" or "terms" or "menu" or "skip to content")
        {
            return true;
        }

        ReadOnlySpan<string> needles =
        [
            "privacy policy", "terms of", "terms &", "cookie policy", "cookie settings",
            "call us", "contact us", "headquarters", "get your free",
            "free assessment", "sign up", "log in",
            "subscribe", "book a", "schedule a", "about us", "careers",
            "sitemap", "follow us", "all rights reserved",
        ];
        foreach (var needle in needles)
        {
            if (n.Contains(needle, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private sealed record MatchCandidate(
        string Heading,
        string[] Path,
        string Kind,
        string[] ChildHeadings,
        IReadOnlyList<ToolRow> Tools,
        int Depth,
        int HeadingTokens,
        int Level,
        IReadOnlyList<string> Paragraphs);
}
