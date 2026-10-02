using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// Lightweight on-page SEO checks against a ContentDocument + target keyword.
/// Heuristic only — not a Surfer clone; good enough for Horizon B in-editor guidance.
/// </summary>
public static class GcwSeoAnalyzer
{
    public sealed record SeoCheck(
        string Id,
        string Label,
        bool Passed,
        string Detail,
        string? FixHint);

    public sealed record SeoReport(
        string TargetKeyword,
        int Score,
        int WordCount,
        int SectionCount,
        double KeywordDensityPercent,
        IReadOnlyList<SeoCheck> Checks,
        string ApplyFeedback);

    /// <summary>
    /// Scores a draft against its target keyword and <b>its own content type's</b> thresholds.
    /// </summary>
    /// <remarks>
    /// <paramref name="contentType"/> has no default on purpose. A two-argument overload used to pass
    /// null, and null reaches <c>GccV2LongFormTypes.Normalize</c>, which returns <c>blog</c> — so a
    /// pillar and a tool page, both with a 3,000-word floor, were silently graded against the blog's
    /// 1,800 and reported as passing. The caller always knows the type: a Create artifact carries
    /// <c>Type</c>, a Gcw asset carries <c>Type</c>. Requiring it is what stops the next caller
    /// recreating the default (Jeff, 2026-10-02: the score "does not appear to be per Content Type").
    /// </remarks>
    public static SeoReport Analyze(string bodyDocumentJson, string targetKeyword, string? contentType)
    {
        var keyword = (targetKeyword ?? "").Trim();
        var text = ExtractPlainText(bodyDocumentJson, out var lede, out var headings, out var sectionCount);
        var words = Tokenize(text);
        var wordCount = words.Count;
        var density = wordCount == 0 || string.IsNullOrWhiteSpace(keyword)
            ? 0
            : 100.0 * CountPhraseOccurrences(text, keyword) / wordCount;

        var checks = new List<SeoCheck>();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            checks.Add(new SeoCheck(
                "keyword-missing",
                "Target keyword",
                false,
                "Campaign has no keyword set.",
                "Set a campaign keyword on Strategy Map."));
        }
        else
        {
            var inLede = ContainsPhrase(lede, keyword);
            checks.Add(new SeoCheck(
                "keyword-in-lede",
                "Keyword in lede",
                inLede,
                inLede ? "Lede includes the target keyword." : "Lede does not include the target keyword.",
                inLede ? null : $"Include “{keyword}” naturally in the opening lede."));

            var inHeading = headings.Any(h => ContainsPhrase(h, keyword));
            checks.Add(new SeoCheck(
                "keyword-in-heading",
                "Keyword in a heading",
                inHeading,
                inHeading ? "At least one section heading includes the keyword." : "No section heading includes the keyword.",
                inHeading ? null : $"Use “{keyword}” in at least one H2-style section heading."));

            var densityOk = density >= 0.4 && density <= 2.5;
            checks.Add(new SeoCheck(
                "keyword-density",
                "Keyword density",
                densityOk,
                $"Density ≈ {density:0.00}% (target roughly 0.4–2.5%).",
                densityOk
                    ? null
                    : density < 0.4
                        ? $"Increase natural mentions of “{keyword}”."
                        : $"Reduce repetition of “{keyword}”; it reads stuffed."));
        }

        var (minWords, minSections, applyLengthChecks) = GcwContentTypeScoring.GetSeoLengthRules(contentType);

        if (applyLengthChecks)
        {
            var lengthOk = wordCount >= minWords;
            checks.Add(new SeoCheck(
                "word-count",
                "Draft length",
                lengthOk,
                $"{wordCount} words extracted from the document.",
                lengthOk
                    ? null
                    : $"CRITICAL: Expand from {wordCount} words to at least {minWords} words. Add substantive H2 sections with concrete examples and steps. Do not only rephrase — substantially increase length while keeping “{keyword}” natural (density ~0.4–2.5%)."));

            var sectionsOk = sectionCount >= minSections;
            checks.Add(new SeoCheck(
                "section-count",
                "Section structure",
                sectionsOk,
                $"{sectionCount} top-level sections.",
                sectionsOk ? null : $"Add more H2 sections (aim for {minSections}+) covering subtopics."));
        }

        var passed = checks.Count(c => c.Passed);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(100.0 * passed / checks.Count);

        var hints = checks
            .Where(c => !c.Passed && !string.IsNullOrWhiteSpace(c.FixHint))
            .Select(c => c.FixHint!)
            .ToList();

        // Put length first — revise models often ignore a soft “expand” hint.
        hints = hints
            .OrderByDescending(h => h.StartsWith("CRITICAL:", StringComparison.Ordinal))
            .ToList();

        var applyFeedback = hints.Count == 0
            ? "Polish lightly for clarity while preserving SEO structure and the target keyword."
            : "Improve on-page SEO with these edits (must fully satisfy each):\n- " + string.Join("\n- ", hints);

        return new SeoReport(
            keyword,
            score,
            wordCount,
            sectionCount,
            Math.Round(density, 2),
            checks,
            applyFeedback);
    }

    /// <summary>
    /// One reader, shared with the polish analyser -- see <see cref="GcwBodyDocument"/>. The copy
    /// that lived here read `lede`/`sections` off the envelope root, treated the lede as a string,
    /// and matched paragraphs on `$type` when the converter writes `type`. Any one of those made a
    /// full draft score 0 words.
    /// </summary>
    private static string ExtractPlainText(
        string bodyDocumentJson,
        out string lede,
        out List<string> headings,
        out int sectionCount)
    {
        var text = GcwBodyDocument.Read(bodyDocumentJson);
        lede = text.Lede;
        headings = [.. text.Headings];
        sectionCount = text.SectionCount;
        return text.PlainText;
    }


    private static List<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9']+")
            .Select(m => m.Value)
            .Where(w => w.Length > 0)
            .ToList();

    private static bool ContainsPhrase(string haystack, string phrase) =>
        CountPhraseOccurrences(haystack, phrase) > 0;

    /// <summary>
    /// Counts the phrase, treating <c>&amp;</c> and <c>and</c> as the same word.
    /// </summary>
    /// <remarks>
    /// A keyword of "Automated Data Entry &amp; Processing" is written "Automated Data Entry and
    /// Processing" in any real heading or sentence, and an exact-substring match scored that at zero
    /// occurrences. This is not loosening the check: it is the same phrase, and the alternative is a
    /// writer told to put an ampersand in its prose to satisfy a matcher. Normalisation only, never
    /// stemming or partial matching -- a draft that says "data entry" is still not using the keyword.
    /// </remarks>
    private static int CountPhraseOccurrences(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
            return 0;

        var h = NormalizeForMatch(haystack);
        var p = NormalizeForMatch(phrase);
        if (p.Length == 0) return 0;

        var count = 0;
        var idx = 0;
        while ((idx = h.IndexOf(p, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += p.Length;
        }
        return count;
    }

    private static string NormalizeForMatch(string value)
    {
        var lowered = value.ToLowerInvariant().Replace("&", " and ");
        return Regex.Replace(lowered, @"\s+", " ").Trim();
    }
}
