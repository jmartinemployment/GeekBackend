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
    /// The share of a page's words the exact phrase may take before it reads stuffed. Read by the page
    /// guard; never copied.
    /// </summary>
    /// <remarks>
    /// <b>There is no minimum</b> (Jeff, 2026-10-13: the SEO standard decides, not a number of the
    /// code's own). Until then a page under 0.4% failed this check, a figure that is in none of his SEO
    /// skills: <c>seo-content-brief</c> says density "is not a Google percentage rule", "do not target a
    /// quota", and that the keyword is judged by where it is placed, with density kept as a stuffing
    /// heuristic only. On the 13:16 UTC run of that day the minimum asked 33 to 39 exact uses of a
    /// five-word phrase on pages of 8,100 to 9,800 words and was listed against all five. Where the
    /// phrase must be is checked beside this: the opening and a heading.
    /// </remarks>
    public const double MaxKeywordDensityPercent = 2.5;

    /// <summary>The exact phrase counted over a stored body: how many times, in how many words, and the share.</summary>
    public sealed record KeywordCount(int Uses, int Words, double DensityPercent);

    /// <summary>
    /// The one count the report, the page guard and the run record all read: the same reader, the same
    /// tokenizer and the same phrase match as <see cref="Analyze"/>.
    /// </summary>
    public static KeywordCount CountKeyword(string bodyDocumentJson, string keyword) =>
        CountKeywordIn(GcwBodyDocument.Read(bodyDocumentJson).PlainText, (keyword ?? "").Trim());

    /// <summary>
    /// A stored body's words as the report counts them: the same reader and tokenizer as
    /// <see cref="Analyze"/>'s "Draft length" check, so the page guard and the score cannot disagree
    /// about whether a page has reached its floor.
    /// </summary>
    public static int CountWords(string bodyDocumentJson) =>
        Tokenize(GcwBodyDocument.Read(bodyDocumentJson).PlainText).Count;

    /// <summary>
    /// Whether any heading of a stored body carries the exact phrase: the report's "Keyword in a
    /// heading" check, by the same reader and the same match. The opening's heading counts, as it
    /// does there.
    /// </summary>
    public static bool KeywordInAHeading(string bodyDocumentJson, string keyword) =>
        HeadingsCarry(GcwBodyDocument.Read(bodyDocumentJson).Headings, (keyword ?? "").Trim());

    /// <summary>
    /// Whether the opening of a stored body carries the exact phrase: the report's "Keyword in lede"
    /// check, by the same reader and the same match.
    /// </summary>
    public static bool KeywordInTheOpening(string bodyDocumentJson, string keyword) =>
        ContainsPhrase(GcwBodyDocument.Read(bodyDocumentJson).Lede, (keyword ?? "").Trim());

    /// <summary>
    /// Whether a title or a meta description carries the exact phrase: the report's "Keyword in the
    /// title" and "Keyword in the meta description" checks, by the same match. The page's own check
    /// (<c>GccDraftGuard.KeywordPlacement</c>) reads this, so a run's lines and the score cannot
    /// disagree.
    /// </summary>
    public static bool Carries(string? text, string keyword) =>
        ContainsPhrase(text ?? "", (keyword ?? "").Trim());

    private static bool HeadingsCarry(IEnumerable<string> headings, string keyword) =>
        headings.Any(h => ContainsPhrase(h, keyword));

    private static KeywordCount CountKeywordIn(string plainText, string keyword)
    {
        var words = Tokenize(plainText).Count;
        var uses = string.IsNullOrWhiteSpace(keyword) ? 0 : CountPhraseOccurrences(plainText, keyword);
        var density = words == 0 ? 0 : 100.0 * uses / words;
        return new KeywordCount(uses, words, density);
    }

    /// <summary>
    /// Scores a draft against its target keyword and <b>its own content type's</b> thresholds.
    /// </summary>
    /// <remarks>
    /// <paramref name="contentType"/> has no default on purpose. A two-argument overload used to pass
    /// null, and null reaches <c>GccLongFormTypes.Normalize</c>, which returns <c>blog</c> — so a
    /// pillar and a tool page, both with a 3,000-word floor, were silently graded against the blog's
    /// 1,800 and reported as passing. The caller always knows the type: a Create artifact carries
    /// <c>Type</c>, a Gcw asset carries <c>Type</c>. Requiring it is what stops the next caller
    /// recreating the default (Jeff, 2026-10-02: the score "does not appear to be per Content Type").
    /// </remarks>
    public static SeoReport Analyze(string bodyDocumentJson, string targetKeyword, string? contentType)
    {
        var keyword = (targetKeyword ?? "").Trim();
        // One reader, shared with the polish analyser -- see GcwBodyDocument.
        var body = GcwBodyDocument.Read(bodyDocumentJson);
        var lede = body.Lede;
        var headings = body.Headings;
        var sectionCount = body.SectionCount;
        var counted = CountKeywordIn(body.PlainText, keyword);
        var wordCount = counted.Words;
        var density = counted.DensityPercent;
        var (minWords, minSections, applyLengthChecks) = GcwContentTypeScoring.GetSeoLengthRules(contentType);

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

            var inHeading = HeadingsCarry(headings, keyword);
            checks.Add(new SeoCheck(
                "keyword-in-heading",
                "Keyword in a heading",
                inHeading,
                inHeading ? "At least one section heading includes the keyword." : "No section heading includes the keyword.",
                inHeading ? null : $"Use “{keyword}” in at least one H2-style section heading."));

            // The title and the meta description, on a page that is saved with them (Jeff, 2026-10-13:
            // "SEO skill requires, in which case it should be implemented"). His seo-content-brief
            // skill lists where the primary keyword MUST appear: the title tag and the H1, both made
            // from this title; the URL slug, which a pillar and a blog take from this title; the meta
            // description; and the first paragraph, which is the lede check above. Until then nothing
            // read the title or the meta description, and only the pillar's request asked for either.
            // Not for a short piece, which has no title tag, and not for a bare document, which
            // carries neither field.
            if (applyLengthChecks && body.Title is { } title)
            {
                var inTitle = ContainsPhrase(title, keyword);
                checks.Add(new SeoCheck(
                    "keyword-in-title",
                    "Keyword in the title",
                    inTitle,
                    inTitle
                        ? "The title includes the target keyword."
                        : title.Length == 0
                            ? "The page has no title."
                            : $"The title, “{title}”, does not include the target keyword.",
                    inTitle ? null : $"Put “{keyword}” in the title, near its start."));
            }

            if (applyLengthChecks && body.MetaDescription is { } metaDescription)
            {
                var inMeta = ContainsPhrase(metaDescription, keyword);
                checks.Add(new SeoCheck(
                    "keyword-in-meta-description",
                    "Keyword in the meta description",
                    inMeta,
                    inMeta
                        ? "The meta description includes the target keyword."
                        : metaDescription.Length == 0
                            ? "The page has no meta description."
                            : "The meta description does not include the target keyword.",
                    inMeta ? null : $"Work “{keyword}” into the meta description."));
            }

            var densityOk = density <= MaxKeywordDensityPercent;
            checks.Add(new SeoCheck(
                "keyword-density",
                "Keyword density",
                densityOk,
                $"Density ≈ {density:0.00}% (over {MaxKeywordDensityPercent:0.0}% reads stuffed; there is no minimum).",
                densityOk ? null : $"Reduce repetition of “{keyword}”; it reads stuffed."));
        }

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
                    : $"CRITICAL: Expand from {wordCount} words to at least {minWords} words. Add substantive H2 sections with concrete examples and steps. Do not only rephrase — substantially increase length while keeping “{keyword}” natural (under {MaxKeywordDensityPercent:0.0}% of the words)."));

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
    internal static int CountPhraseOccurrences(string haystack, string phrase)
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
