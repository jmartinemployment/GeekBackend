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

    public static SeoReport Analyze(string bodyDocumentJson, string targetKeyword)
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

        var lengthOk = wordCount >= 800;
        checks.Add(new SeoCheck(
            "word-count",
            "Draft length",
            lengthOk,
            $"{wordCount} words extracted from the document.",
            lengthOk ? null : "Expand to at least ~800 words for pillar SEO depth."));

        var sectionsOk = sectionCount >= 3;
        checks.Add(new SeoCheck(
            "section-count",
            "Section structure",
            sectionsOk,
            $"{sectionCount} top-level sections.",
            sectionsOk ? null : "Add more H2 sections (aim for 3–6) covering subtopics."));

        var passed = checks.Count(c => c.Passed);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(100.0 * passed / checks.Count);

        var hints = checks
            .Where(c => !c.Passed && !string.IsNullOrWhiteSpace(c.FixHint))
            .Select(c => c.FixHint!)
            .ToList();

        var applyFeedback = hints.Count == 0
            ? "Polish lightly for clarity while preserving SEO structure and the target keyword."
            : "Improve on-page SEO with these edits:\n- " + string.Join("\n- ", hints);

        return new SeoReport(
            keyword,
            score,
            wordCount,
            sectionCount,
            Math.Round(density, 2),
            checks,
            applyFeedback);
    }

    private static string ExtractPlainText(
        string bodyDocumentJson,
        out string lede,
        out List<string> headings,
        out int sectionCount)
    {
        lede = "";
        headings = [];
        sectionCount = 0;
        if (string.IsNullOrWhiteSpace(bodyDocumentJson))
            return "";

        try
        {
            using var doc = JsonDocument.Parse(bodyDocumentJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("lede", out var ledeEl) && ledeEl.ValueKind == JsonValueKind.String)
                lede = ledeEl.GetString() ?? "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(lede))
                parts.Add(lede);

            if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    sectionCount++;
                    CollectSection(section, parts, headings);
                }
            }

            return string.Join("\n", parts);
        }
        catch (JsonException)
        {
            return bodyDocumentJson;
        }
    }

    private static void CollectSection(JsonElement section, List<string> parts, List<string> headings)
    {
        if (section.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String)
        {
            var h = heading.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(h))
            {
                headings.Add(h);
                parts.Add(h);
            }
        }

        if (section.TryGetProperty("paragraphs", out var paragraphs) && paragraphs.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in paragraphs.EnumerateArray())
                CollectParagraph(p, parts);
        }

        if (section.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectSection(child, parts, headings);
        }
    }

    private static void CollectParagraph(JsonElement paragraph, List<string> parts)
    {
        if (!paragraph.TryGetProperty("$type", out var type) || type.ValueKind != JsonValueKind.String)
            return;

        var t = type.GetString();
        if (t == "text" && paragraph.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
        {
            foreach (var run in runs.EnumerateArray())
            {
                if (run.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? "");
            }
        }
        else if (t == "list" && paragraph.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Array) continue;
                foreach (var run in item.EnumerateArray())
                {
                    if (run.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        parts.Add(text.GetString() ?? "");
                }
            }
        }
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9']+")
            .Select(m => m.Value)
            .Where(w => w.Length > 0)
            .ToList();

    private static bool ContainsPhrase(string haystack, string phrase) =>
        CountPhraseOccurrences(haystack, phrase) > 0;

    private static int CountPhraseOccurrences(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase))
            return 0;

        var h = Regex.Replace(haystack.ToLowerInvariant(), @"\s+", " ").Trim();
        var p = Regex.Replace(phrase.ToLowerInvariant(), @"\s+", " ").Trim();
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
}
