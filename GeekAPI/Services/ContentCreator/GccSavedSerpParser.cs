using System.Text.RegularExpressions;
using GeekApplication.Models.ContentCreator;
using HtmlAgilityPack;

namespace GeekAPI.Services.ContentCreator;

/// <summary>
/// Parses an operator-saved Google results page (HTML or text) into organic pairs, PAA, related searches,
/// and a SERP shape summary. Degrades gracefully when markup churns — never throws on empty partials.
/// </summary>
public static partial class GccSavedSerpParser
{
    public static SavedSerpParseResult Parse(string rawContent, string? targetKeyword = null)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return Empty("Empty upload — paste or save the Google results page and try again.");
        }

        var trimmed = rawContent.Trim();
        var looksHtml = trimmed.Contains('<') && (
            trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<!doctype", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<div", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<a ", StringComparison.OrdinalIgnoreCase));

        List<SavedSerpOrganic> organics;
        List<string> paa;
        List<string> related;
        string? warning = null;

        if (looksHtml)
        {
            (organics, paa, related, warning) = ParseHtml(trimmed);
        }
        else
        {
            (organics, paa, related, warning) = ParsePlainText(trimmed);
        }

        organics = DedupOrganics(organics).Take(12).ToList();
        paa = DedupStrings(paa).Take(80).ToList();
        related = DedupStrings(related).Take(20).ToList();

        var missingPaa = paa.Count == 0 && organics.Count > 0;
        if (missingPaa)
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? "No People Also Ask found. Capture page 1 of the results (PAA sits on page 1)."
                : warning + " No PAA found — capture page 1 if you need PAA.";
        }

        var kw = targetKeyword?.Trim() ?? "";
        var paaCandidates = paa
            .Select(q => ScorePaaRelevance(q, kw))
            .ToList();

        var shape = InferShape(organics, paa.Count > 0);

        return new SavedSerpParseResult(
            organics,
            paaCandidates,
            related,
            shape,
            missingPaa,
            warning);
    }

    public static InformationGainNote BuildPartialInformationGain(
        string gapTopic,
        IReadOnlyList<RelatedPageDto> relatedPages,
        IReadOnlyList<SavedSerpOrganic>? organics = null)
    {
        var covers = new List<string>();
        foreach (var page in relatedPages.Take(12))
        {
            var bits = new List<string> { page.Title };
            bits.AddRange(page.Headings.Take(4));
            if (!string.IsNullOrWhiteSpace(page.Excerpt))
                bits.Add(Truncate(page.Excerpt, 120));
            covers.Add($"{page.Url}: {string.Join(" · ", bits.Where(b => !string.IsNullOrWhiteSpace(b)).Distinct())}");
        }

        var opens = new List<string>();
        if (organics is { Count: > 0 })
        {
            var siteHosts = relatedPages
                .Select(p => TryHost(p.Url))
                .Where(h => h is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var o in organics.Take(10))
            {
                var host = TryHost(o.Url);
                if (host is not null && siteHosts.Contains(host))
                    continue;
                opens.Add($"{o.Title} ({o.Url})");
            }
        }

        var summary = covers.Count == 0
            ? $"No related site pages resolved for “{gapTopic}” — Information Gain needs section context."
            : opens.Count == 0
                ? $"This site already covers {covers.Count} related page(s) near “{gapTopic}”. Upload a saved SERP to compare competitor opens."
                : $"This site covers {covers.Count} related page(s); SERP shows {opens.Count} competitor result(s) to differentiate against.";

        return new InformationGainNote(covers, opens, summary);
    }

    public static SerpShapeSummary InferShape(
        IReadOnlyList<SavedSerpOrganic> organics,
        bool hasPaa)
    {
        var formats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var patterns = new List<string>();

        foreach (var o in organics)
        {
            var title = o.Title;
            var lower = title.ToLowerInvariant();
            void Hit(string format)
            {
                formats[format] = formats.GetValueOrDefault(format) + 1;
            }

            if (lower.Contains(" vs ") || lower.Contains("versus") || lower.Contains("comparison") || lower.Contains("compared"))
                Hit("comparison");
            if (lower.Contains("how to") || lower.Contains("how-to") || lower.StartsWith("fix ") || lower.Contains("troubleshoot"))
                Hit("problem-solution");
            if (lower.Contains("guide") || lower.Contains("ultimate") || lower.Contains("complete ") || lower.Contains("manual"))
                Hit("guide");
            if (lower.Contains("best ") || lower.StartsWith("top ") || Regex.IsMatch(lower, @"\b\d+\s+(best|ways|tips|tools)"))
                Hit("listicle");
            if (lower.Contains("case study") || lower.Contains("we built") || lower.Contains("our experience"))
                Hit("case-study");
            if (lower.Contains("review") || lower.Contains("pricing"))
                Hit("review");

            if (patterns.Count < 8)
                patterns.Add(title);
        }

        var dominant = formats
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .Select(kv => kv.Key)
            .Take(3)
            .ToList();

        if (dominant.Count == 0 && organics.Count > 0)
            dominant.Add("mixed/informational");

        var guidance = dominant.Count == 0
            ? "No organic titles parsed — hand-enter SERP fields or re-save the results page."
            : $"Dominant SERP formats: {string.Join(", ", dominant)}. Prefer an Angle for SEO that matches (advisory only — do not auto-set). "
              + (dominant.Contains("comparison")
                  ? "Head-to-head results suggest Comparative."
                  : dominant.Contains("listicle") || dominant.Contains("guide")
                      ? "Listicle/guide-heavy SERPs often fit Ultimate Guide."
                      : dominant.Contains("problem-solution")
                          ? "How-to/fix titles often fit Problem-Solution."
                          : dominant.Contains("case-study")
                              ? "Experience-led titles often fit Case Study / Data-Driven."
                              : "Pick the angle that adds Information Gain without fighting the SERP.");

        return new SerpShapeSummary(
            dominant,
            patterns,
            guidance,
            hasPaa,
            organics.Count,
            hasPaa ? "page1-or-unknown" : "maybe-page2");
    }

    private static (List<SavedSerpOrganic> Organics, List<string> Paa, List<string> Related, string? Warning)
        ParseHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var organics = new List<SavedSerpOrganic>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Classic Google: a[href] under result blocks; also /url?q= redirect links.
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        var position = 0;
        if (anchors is not null)
        {
            foreach (var a in anchors)
            {
                var href = a.GetAttributeValue("href", "");
                var url = NormalizeGoogleHref(href);
                if (url is null || !seenUrls.Add(url))
                    continue;
                if (IsGoogleChromeUrl(url))
                    continue;

                var title = CleanText(a.InnerText);
                if (title.Length < 8 || title.Length > 200)
                    continue;
                // Skip nav chrome
                if (title is "Cached" or "Similar" or "Translate this page")
                    continue;

                position++;
                organics.Add(new SavedSerpOrganic(Truncate(title, 200), url, position));
                if (organics.Count >= 12)
                    break;
            }
        }

        var paa = new List<string>();
        var questionNodes = doc.DocumentNode.SelectNodes(
            "//*[contains(@class,'related-question') or @data-q or contains(@jsname,'Vy1nD') or contains(@class,'related-question-pair')]");
        if (questionNodes is not null)
        {
            foreach (var node in questionNodes)
            {
                var text = CleanText(node.InnerText);
                if (text.EndsWith('?') && text.Length is > 8 and < 300)
                    paa.Add(Truncate(text, 300));
            }
        }

        // Aria / heading fallbacks ending in ?
        var headingQs = doc.DocumentNode.SelectNodes("//h2 | //h3 | //span");
        if (headingQs is not null)
        {
            foreach (var node in headingQs)
            {
                var text = CleanText(node.InnerText);
                if (text.EndsWith('?') && text.Length is > 12 and < 300)
                    paa.Add(Truncate(text, 300));
            }
        }

        var related = new List<string>();
        // Related searches often in a#bres or "Related searches" section links
        var relatedSection = doc.DocumentNode.SelectNodes(
            "//*[contains(.,'Related searches')]/following::a[@href] | //div[@id='bres']//a | //*[contains(@class,'related-question')]");
        // Simpler: any short link text near "Related searches"
        var allText = CleanText(doc.DocumentNode.InnerText);
        related.AddRange(ExtractRelatedFromPlain(allText));

        string? warning = organics.Count == 0
            ? "Could not extract organic title→URL pairs from this HTML (Google markup may have changed). Use hand-entry fallback."
            : null;

        return (organics, DedupStrings(paa), DedupStrings(related), warning);
    }

    private static (List<SavedSerpOrganic> Organics, List<string> Paa, List<string> Related, string? Warning)
        ParsePlainText(string text)
    {
        var organics = new List<SavedSerpOrganic>();
        var paa = new List<string>();
        var related = new List<string>();

        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? pendingTitle = null;
        var pos = 0;

        foreach (var line in lines)
        {
            var cleaned = line.TrimStart('-', '*', '•', ' ').Trim();
            if (cleaned.Length == 0)
                continue;

            if (cleaned.EndsWith('?') && cleaned.Length is > 8 and < 300)
            {
                paa.Add(Truncate(cleaned, 300));
                continue;
            }

            if (Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !IsGoogleChromeUrl(cleaned))
            {
                var title = pendingTitle ?? uri.Host;
                pos++;
                organics.Add(new SavedSerpOrganic(Truncate(title, 200), cleaned, pos));
                pendingTitle = null;
                continue;
            }

            // Title then URL on next line
            if (cleaned.Length is >= 8 and <= 200 && !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                pendingTitle = cleaned;
        }

        related.AddRange(ExtractRelatedFromPlain(text));

        string? warning = organics.Count == 0 && paa.Count == 0
            ? "Plain-text parse found no organics or PAA. Prefer a saved HTML results page, or hand-enter fields."
            : null;

        return (organics, paa, related, warning);
    }

    private static IEnumerable<string> ExtractRelatedFromPlain(string text)
    {
        var idx = text.IndexOf("Related searches", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            yield break;
        var slice = text[(idx + "Related searches".Length)..];
        foreach (var line in slice.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(15))
        {
            var cleaned = line.TrimStart('-', '*', '•', ' ').Trim();
            if (cleaned.Length is >= 3 and <= 120
                && !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !cleaned.EndsWith('?'))
                yield return Truncate(cleaned, 200);
        }
    }

    private static PaaCandidate ScorePaaRelevance(string question, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new PaaCandidate(question, LikelyRelevant: true, "No keyword — left for operator review");

        var q = question.ToLowerInvariant();
        var noise = new[]
        {
            "make $", "make money", "richest", "pay taxes", "jobs gone", "gone by 2030",
            "get rich", "passive income", "onlyfans",
        };
        foreach (var n in noise)
        {
            if (q.Contains(n))
                return new PaaCandidate(question, false, "Likely off-topic / clickbait");
        }

        var tokens = keyword.ToLowerInvariant()
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 2)
            .ToList();
        if (tokens.Count == 0)
            return new PaaCandidate(question, true, null);

        var hits = tokens.Count(t => q.Contains(t));
        var ratio = (double)hits / tokens.Count;
        if (ratio >= 0.4)
            return new PaaCandidate(question, true, "Shares keyword terms");
        if (hits >= 1)
            return new PaaCandidate(question, true, "Partial keyword overlap");
        return new PaaCandidate(question, false, "Weak keyword overlap — review before seeding");
    }

    private static string? NormalizeGoogleHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        href = href.Trim();
        if (href.StartsWith("/url?", StringComparison.OrdinalIgnoreCase))
        {
            var qIdx = href.IndexOf("q=", StringComparison.OrdinalIgnoreCase);
            if (qIdx >= 0)
            {
                var rest = href[(qIdx + 2)..];
                var amp = rest.IndexOf('&');
                var encoded = amp >= 0 ? rest[..amp] : rest;
                href = Uri.UnescapeDataString(encoded);
            }
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;
        return uri.ToString();
    }

    private static bool IsGoogleChromeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;
        var host = uri.Host.ToLowerInvariant();
        return host.Contains("google.")
               || host.Contains("gstatic.")
               || host.Contains("youtube.com") && uri.AbsolutePath.StartsWith("/results", StringComparison.OrdinalIgnoreCase)
               || host is "webcache.googleusercontent.com" or "policies.google.com" or "support.google.com"
               || host.Contains("schema.org");
    }

    private static List<SavedSerpOrganic> DedupOrganics(List<SavedSerpOrganic> list)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<SavedSerpOrganic>();
        foreach (var o in list)
        {
            if (!seen.Add(o.Url))
                continue;
            result.Add(o with { Position = result.Count + 1 });
        }
        return result;
    }

    private static List<string> DedupStrings(IEnumerable<string> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var item in items)
        {
            var t = item.Trim();
            if (t.Length == 0 || !seen.Add(t))
                continue;
            result.Add(t);
        }
        return result;
    }

    private static SavedSerpParseResult Empty(string warning) =>
        new([], [], [], new SerpShapeSummary([], [], warning, false, 0, null), false, warning);

    private static string? TryHost(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return null;
        }
    }

    private static string CleanText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        var decoded = HtmlEntity.DeEntitize(raw) ?? string.Empty;
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
