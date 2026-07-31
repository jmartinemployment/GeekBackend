using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// Lightweight Grammarly-class polish checks on a ContentDocument.
/// Heuristic ship-check — not a full grammar engine.
/// </summary>
public static class GcwPolishAnalyzer
{
    private static readonly string[] FillerWords =
    [
        "very", "really", "just", "basically", "actually", "literally",
        "quite", "rather", "somewhat", "definitely", "simply"
    ];

    private static readonly string[] PlaceholderSnippets =
    [
        "opening lede", "draft body", "lorem ipsum", "todo:", "tbd",
        "placeholder", "section one"
    ];

    public sealed record PolishCheck(
        string Id,
        string Label,
        bool Passed,
        string Severity,
        string Detail,
        string? FixHint);

    public sealed record PolishReport(
        int Score,
        bool ShipReady,
        int WordCount,
        int SentenceCount,
        double AvgSentenceWords,
        IReadOnlyList<PolishCheck> Checks,
        string ApplyFeedback);

    public static PolishReport Analyze(
        string bodyDocumentJson,
        IReadOnlyList<string>? prohibitedClaimPhrases = null)
    {
        var text = ExtractPlainText(bodyDocumentJson, out var lede);
        var words = Tokenize(text);
        var wordCount = words.Count;
        var sentences = SplitSentences(text);
        var sentenceCount = Math.Max(sentences.Count, wordCount == 0 ? 0 : 1);
        var avgSentence = sentenceCount == 0 ? 0 : (double)wordCount / sentenceCount;

        var checks = new List<PolishCheck>();

        var ledeOk = !string.IsNullOrWhiteSpace(lede)
                     && lede.Trim().Length >= 40
                     && !ContainsAny(lede, PlaceholderSnippets);
        checks.Add(new PolishCheck(
            "lede-quality",
            "Lede quality",
            ledeOk,
            "advisory",
            ledeOk
                ? "Lede looks substantive."
                : "Lede is missing, short, or still placeholder copy.",
            ledeOk ? null : "Rewrite the lede into a clear 1–2 sentence opener (40+ characters)."));

        var placeholderHit = ContainsAny(text, PlaceholderSnippets);
        checks.Add(new PolishCheck(
            "no-placeholders",
            "No placeholder copy",
            !placeholderHit,
            "critical",
            placeholderHit
                ? "Draft still contains placeholder or template language."
                : "No obvious placeholder phrases found.",
            placeholderHit ? "Remove placeholder / template phrases before shipping." : null));

        var longSentences = sentences.Count(s => Tokenize(s).Count > 40);
        var clarityOk = avgSentence <= 28 && longSentences <= 2;
        checks.Add(new PolishCheck(
            "sentence-clarity",
            "Sentence clarity",
            clarityOk,
            "advisory",
            $"Avg ≈ {avgSentence:0.0} words/sentence; {longSentences} sentence(s) over 40 words.",
            clarityOk
                ? null
                : "Split long sentences and aim for ~15–25 words on average."));

        var fillerCount = CountFiller(words);
        var fillerRate = wordCount == 0 ? 0 : 100.0 * fillerCount / wordCount;
        var fillerOk = fillerRate <= 1.5;
        checks.Add(new PolishCheck(
            "filler-words",
            "Filler words",
            fillerOk,
            "advisory",
            $"Filler density ≈ {fillerRate:0.00}% ({fillerCount} hits).",
            fillerOk
                ? null
                : "Cut filler (very, really, just, basically, actually, etc.)."));

        var banned = (prohibitedClaimPhrases ?? [])
            .Select(p => (p ?? "").Trim())
            .Where(p => p.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var hits = banned.Where(p => ContainsPhrase(text, p)).ToList();
        var claimsOk = hits.Count == 0;
        checks.Add(new PolishCheck(
            "prohibited-claims",
            "Prohibited claims",
            claimsOk,
            "critical",
            claimsOk
                ? banned.Count == 0
                    ? "No brand prohibited-claim list on this campaign profile."
                    : "No prohibited claim phrases found in the draft."
                : $"Found prohibited phrasing: {string.Join("; ", hits.Take(5))}.",
            claimsOk
                ? null
                : "Remove or rephrase prohibited claims to match Brand Core."));

        var passed = checks.Count(c => c.Passed);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(100.0 * passed / checks.Count);
        var shipReady = checks.Where(c => c.Severity == "critical").All(c => c.Passed);

        var hints = checks
            .Where(c => !c.Passed && !string.IsNullOrWhiteSpace(c.FixHint))
            .OrderBy(c => c.Severity == "critical" ? 0 : 1)
            .Select(c => c.FixHint!)
            .ToList();

        var applyFeedback = hints.Count == 0
            ? "Polish for clarity and concision while preserving meaning, structure, and brand voice."
            : "Polish this draft for ship-readiness:\n- " + string.Join("\n- ", hints);

        return new PolishReport(
            score,
            shipReady,
            wordCount,
            sentenceCount,
            Math.Round(avgSentence, 1),
            checks,
            applyFeedback);
    }

    /// <summary>
    /// Flatten prohibited-claims JSON (object or nested) into searchable phrases.
    /// </summary>
    public static IReadOnlyList<string> ExtractClaimPhrases(Dictionary<string, object>? prohibitedClaims)
    {
        var phrases = new List<string>();
        if (prohibitedClaims is null || prohibitedClaims.Count == 0)
            return phrases;

        foreach (var (key, value) in prohibitedClaims)
        {
            if (!string.IsNullOrWhiteSpace(key) && key.Length >= 3)
                phrases.Add(key.Trim());
            CollectPhrases(value, phrases);
        }

        return phrases
            .Select(p => p.Trim())
            .Where(p => p.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectPhrases(object? value, List<string> phrases)
    {
        switch (value)
        {
            case null:
                return;
            case string s when s.Trim().Length >= 3:
                phrases.Add(s.Trim());
                break;
            case JsonElement el:
                CollectFromJson(el, phrases);
                break;
            case IEnumerable<object> list:
                foreach (var item in list)
                    CollectPhrases(item, phrases);
                break;
            case Dictionary<string, object> dict:
                foreach (var (k, v) in dict)
                {
                    if (!string.IsNullOrWhiteSpace(k) && k.Length >= 3)
                        phrases.Add(k.Trim());
                    CollectPhrases(v, phrases);
                }
                break;
            default:
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text) && text.Length >= 3 && text != value.GetType().FullName)
                    phrases.Add(text.Trim());
                break;
            }
        }
    }

    private static void CollectFromJson(JsonElement el, List<string> phrases)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s.Length >= 3)
                    phrases.Add(s.Trim());
                break;
            }
            case JsonValueKind.Array:
                foreach (var child in el.EnumerateArray())
                    CollectFromJson(child, phrases);
                break;
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Name.Length >= 3)
                        phrases.Add(prop.Name.Trim());
                    CollectFromJson(prop.Value, phrases);
                }
                break;
        }
    }

    private static string ExtractPlainText(string bodyDocumentJson, out string lede)
    {
        lede = "";
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
                    CollectSection(section, parts);
            }

            return string.Join("\n", parts);
        }
        catch (JsonException)
        {
            return bodyDocumentJson;
        }
    }

    private static void CollectSection(JsonElement section, List<string> parts)
    {
        if (section.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String)
        {
            var h = heading.GetString();
            if (!string.IsNullOrWhiteSpace(h))
                parts.Add(h);
        }

        if (section.TryGetProperty("paragraphs", out var paragraphs) && paragraphs.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in paragraphs.EnumerateArray())
                CollectParagraph(p, parts);
        }

        if (section.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                CollectSection(child, parts);
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
                if (item.ValueKind == JsonValueKind.String)
                    parts.Add(item.GetString() ?? "");
                else if (item.ValueKind == JsonValueKind.Object)
                    CollectParagraph(item, parts);
            }
        }
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9']+")
            .Select(m => m.Value)
            .Where(w => w.Length > 0)
            .ToList();

    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        return Regex.Split(text, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    private static int CountFiller(IReadOnlyList<string> words)
    {
        var set = new HashSet<string>(FillerWords, StringComparer.OrdinalIgnoreCase);
        return words.Count(w => set.Contains(w));
    }

    private static bool ContainsAny(string haystack, IEnumerable<string> needles) =>
        needles.Any(n => ContainsPhrase(haystack, n));

    private static bool ContainsPhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
            return false;
        return haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
