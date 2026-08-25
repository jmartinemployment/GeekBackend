using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeekAPI.Services.ContentCreatorV2.Geo;

/// <summary>
/// Heuristic "AI-visibility readiness" checks over a <c>GccV2AnalyzerDocument</c>-shaped document
/// (see that class's XML doc for the exact wire shape) + target keyword. This is not a live
/// ChatGPT/Perplexity citation tracker and makes no external calls — it scores whether a draft's
/// *structure* favors AI answer engines picking it up: self-contained citeable passages,
/// FAQ/direct-answer content, clear entity framing, comparison/list content where intent calls
/// for it, and a clean H2 structure. Mirrors <c>GcwSeoAnalyzer</c>'s static, heuristic,
/// named-<see cref="GeoCheck.FixHint"/> style so SEO + GEO can travel together as VALIDATE's dual
/// scores. GEO is advisory only — a low score never blocks <c>ShipReady</c> (see
/// <c>GccV2ValidationReport</c>).
/// </summary>
public static class GccV2GeoAnalyzer
{
    public sealed record GeoCheck(
        string Id,
        string Label,
        bool Passed,
        string Detail,
        string? FixHint);

    public sealed record GeoReport(
        string TargetKeyword,
        int Score,
        IReadOnlyList<GeoCheck> Checks,
        string Summary);

    private const int MinCiteablePassages = 2;
    private const int CiteableMinWords = 40;

    private static readonly Regex WordSplit = new(@"[a-z0-9']+", RegexOptions.Compiled);

    /// <summary>A paragraph opening on one of these reads as a continuation of the prior sentence,
    /// not a claim an AI engine could quote on its own.</summary>
    private static readonly string[] DependentOpeners =
    [
        "this", "it", "these", "that", "those", "so", "also", "however", "thus", "therefore",
        "additionally", "furthermore", "moreover", "meanwhile", "then", "next", "finally", "similarly",
    ];

    private static readonly string[] ComparisonMarkers =
    [
        "vs.", " vs ", "versus", "compared to", "compare ", "comparison", "alternative",
        "alternatives", "better than", "pros and cons", "which is best",
    ];

    private static readonly string[] CommercialIntentMarkers =
    [
        "best", "top", "vs", "versus", "review", "alternative", "pricing", "cost", "comparison", "compare",
    ];

    public static GeoReport Analyze(string analyzerDocumentJson, string targetKeyword)
    {
        var keyword = (targetKeyword ?? "").Trim();
        var document = ParseDocument(analyzerDocumentJson);

        var checks = new List<GeoCheck>
        {
            BuildCiteablePassagesCheck(document),
            BuildFaqOrDirectAnswersCheck(document, keyword),
            BuildEntityClarityCheck(document, keyword),
            BuildComparisonOrListCheck(document, keyword),
            BuildStructureForSnippetCheck(document),
        };

        var passed = checks.Count(c => c.Passed);
        var score = checks.Count == 0 ? 0 : (int)Math.Round(100.0 * passed / checks.Count);

        var hints = checks
            .Where(c => !c.Passed && !string.IsNullOrWhiteSpace(c.FixHint))
            .Select(c => c.FixHint!)
            .ToList();

        var summary = hints.Count == 0
            ? "Structure already favors AI answer engines: citeable passages, clear headings, and direct answers are present."
            : "Improve AI-visibility (GEO) with these edits:\n- " + string.Join("\n- ", hints);

        return new GeoReport(keyword, score, checks, summary);
    }

    // ---- checks ----

    private static GeoCheck BuildCiteablePassagesCheck(DocModel document)
    {
        var citeable = 0;
        foreach (var section in EnumerateSections(document.Sections))
        {
            foreach (var paragraph in section.Paragraphs)
            {
                if (paragraph.Type != "text") continue;
                var text = string.Join(" ", paragraph.RunTexts).Trim();
                if (text.Length == 0) continue;
                if (CountWords(text) < CiteableMinWords) continue;
                if (StartsWithDependentOpener(text)) continue;
                citeable++;
            }
        }

        var passed = citeable >= MinCiteablePassages;
        return new GeoCheck(
            "citeable-passages",
            "Self-contained citeable passages",
            passed,
            $"{citeable} paragraph(s) read as standalone, quotable answers (\u2265{CiteableMinWords} words, no dangling reference to a prior sentence).",
            passed
                ? null
                : $"Add at least {MinCiteablePassages} self-contained paragraphs (\u2265{CiteableMinWords} words) that fully state a claim without referring back to earlier text \u2014 these are what AI answer engines quote directly.");
    }

    private static GeoCheck BuildFaqOrDirectAnswersCheck(DocModel document, string keyword)
    {
        var sections = EnumerateSections(document.Sections).ToList();
        var hasFaqHeading = sections.Any(s =>
            s.Heading.Contains("faq", StringComparison.OrdinalIgnoreCase) ||
            s.Heading.Contains("frequently asked", StringComparison.OrdinalIgnoreCase) ||
            s.Heading.TrimEnd().EndsWith("?", StringComparison.Ordinal));

        var hasDirectAnswer = sections.Any(s => SectionHasDirectAnswer(s, keyword));

        var passed = hasFaqHeading || hasDirectAnswer;
        return new GeoCheck(
            "faq-or-direct-answers",
            "FAQ or direct-answer sections",
            passed,
            passed
                ? "Found an FAQ/question-style heading or a short direct-answer sentence AI engines can lift."
                : "No FAQ/Q&A heading and no short direct-answer sentence detected.",
            passed
                ? null
                : "Add a short FAQ section (question-style H2s) or open at least one section with a direct, self-contained answer sentence (\u226420 words).");
    }

    private static GeoCheck BuildEntityClarityCheck(DocModel document, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new GeoCheck(
                "entity-clarity",
                "Entity named early",
                false,
                "No target keyword set, so entity/topic framing can't be checked.",
                "Set a target keyword so the lede and first heading can be checked for clear topic framing.");
        }

        var firstHeading = document.Sections.FirstOrDefault()?.Heading ?? "";
        var inLede = ContainsPhrase(document.Lede, keyword);
        var inFirstHeading = ContainsPhrase(firstHeading, keyword);
        var passed = inLede || inFirstHeading;

        return new GeoCheck(
            "entity-clarity",
            "Entity named early",
            passed,
            passed
                ? "The target keyword/entity appears in the lede or the first H2, so an AI engine can identify the topic immediately."
                : "The target keyword/entity does not appear in the lede or the first H2.",
            passed
                ? null
                : $"Name \u201c{keyword}\u201d explicitly in the opening lede or the first H2 so an AI engine can identify the topic without reading further.");
    }

    private static GeoCheck BuildComparisonOrListCheck(DocModel document, string keyword)
    {
        var sections = EnumerateSections(document.Sections).ToList();
        var hasList = sections.Any(s => s.Paragraphs.Any(p => p.Type == "list"));
        var flattenedText = string.Join(" ", sections
            .SelectMany(s => s.Paragraphs)
            .Where(p => p.Type == "text")
            .SelectMany(p => p.RunTexts));
        var hasComparisonLanguage = ComparisonMarkers.Any(m => flattenedText.Contains(m, StringComparison.OrdinalIgnoreCase));
        var isCommercialIntent = !string.IsNullOrWhiteSpace(keyword) &&
            CommercialIntentMarkers.Any(m => keyword.Contains(m, StringComparison.OrdinalIgnoreCase));

        if (hasList || hasComparisonLanguage)
        {
            return new GeoCheck(
                "comparison-or-list",
                "Comparison or list content",
                true,
                hasList
                    ? "Draft includes at least one list \u2014 easy for AI engines to extract as a structured answer."
                    : "Draft includes comparison language.",
                null);
        }

        if (isCommercialIntent)
        {
            return new GeoCheck(
                "comparison-or-list",
                "Comparison or list content",
                false,
                "Keyword reads as commercial-comparison intent, but the draft has no list or comparison language.",
                "Add a short comparison list or table (or explicit \u201cX vs Y\u201d language) \u2014 commercial-intent queries are the ones AI engines most often answer with a structured comparison.");
        }

        return new GeoCheck(
            "comparison-or-list",
            "Comparison or list content",
            true,
            "No list or comparison language, but the keyword doesn't signal commercial-comparison intent, so this isn't required.",
            null);
    }

    private static GeoCheck BuildStructureForSnippetCheck(DocModel document)
    {
        var sectionCount = document.Sections.Count;
        var nonEmptySections = document.Sections.Count(SectionHasBody);
        var passed = sectionCount >= 2 && nonEmptySections == sectionCount;

        return new GeoCheck(
            "structure-for-snippet",
            "Clear H2 structure",
            passed,
            $"{sectionCount} top-level section(s), {nonEmptySections} with a non-empty body.",
            passed
                ? null
                : "Use at least 2 H2 sections, each with real body content \u2014 AI engines snippet off clean heading/body structure, not one long undivided block.");
    }

    // ---- helpers ----

    private static bool SectionHasDirectAnswer(SectionModel section, string keyword)
    {
        var firstText = section.Paragraphs.FirstOrDefault(p => p.Type == "text");
        if (firstText is null) return false;
        var text = string.Join(" ", firstText.RunTexts).Trim();
        if (text.Length == 0) return false;

        var firstSentence = SplitSentences(text).FirstOrDefault() ?? text;
        var wordCount = CountWords(firstSentence);
        if (wordCount is < 3 or > 20) return false;

        return string.IsNullOrWhiteSpace(keyword) || ContainsPhrase(firstSentence, keyword);
    }

    private static bool SectionHasBody(SectionModel section) =>
        section.Paragraphs.Any(p =>
            (p.Type == "text" && p.RunTexts.Any(t => !string.IsNullOrWhiteSpace(t))) ||
            (p.Type == "list" && p.Items.Any(item => item.Any(t => !string.IsNullOrWhiteSpace(t)))));

    private static IEnumerable<SectionModel> EnumerateSections(IReadOnlyList<SectionModel> sections)
    {
        foreach (var section in sections)
        {
            yield return section;
            foreach (var child in EnumerateSections(section.Children))
                yield return child;
        }
    }

    private static bool StartsWithDependentOpener(string text)
    {
        var firstWord = WordSplit.Match(text.ToLowerInvariant());
        return firstWord.Success && DependentOpeners.Contains(firstWord.Value);
    }

    private static IEnumerable<string> SplitSentences(string text) =>
        Regex.Split(text, @"(?<=[.!?])\s+").Where(s => !string.IsNullOrWhiteSpace(s));

    private static int CountWords(string text) => WordSplit.Matches(text.ToLowerInvariant()).Count;

    private static bool ContainsPhrase(string haystack, string phrase)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(phrase)) return false;
        var h = Regex.Replace(haystack.ToLowerInvariant(), @"\s+", " ").Trim();
        var p = Regex.Replace(phrase.ToLowerInvariant(), @"\s+", " ").Trim();
        return p.Length > 0 && h.Contains(p, StringComparison.Ordinal);
    }

    // ---- JSON parsing — same shape GccV2AnalyzerDocument.Serialize produces ----

    private sealed class DocModel
    {
        public string Lede = "";
        public List<SectionModel> Sections = [];
    }

    private sealed class SectionModel
    {
        public string Heading = "";
        public List<ParagraphModel> Paragraphs = [];
        public List<SectionModel> Children = [];
    }

    private sealed class ParagraphModel
    {
        public string Type = "text";
        public List<string> RunTexts = [];
        public List<List<string>> Items = [];
    }

    private static DocModel ParseDocument(string analyzerDocumentJson)
    {
        var model = new DocModel();
        if (string.IsNullOrWhiteSpace(analyzerDocumentJson)) return model;

        try
        {
            using var doc = JsonDocument.Parse(analyzerDocumentJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("lede", out var lede) && lede.ValueKind == JsonValueKind.String)
                model.Lede = lede.GetString() ?? "";

            if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
                model.Sections = sections.EnumerateArray().Select(ParseSection).ToList();
        }
        catch (JsonException)
        {
            // Leave the model empty — every check above degrades to "not detected" instead of throwing.
        }

        return model;
    }

    private static SectionModel ParseSection(JsonElement element)
    {
        var section = new SectionModel();
        if (element.TryGetProperty("heading", out var heading) && heading.ValueKind == JsonValueKind.String)
            section.Heading = heading.GetString() ?? "";

        if (element.TryGetProperty("paragraphs", out var paragraphs) && paragraphs.ValueKind == JsonValueKind.Array)
            section.Paragraphs = paragraphs.EnumerateArray().Select(ParseParagraph).ToList();

        if (element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            section.Children = children.EnumerateArray().Select(ParseSection).ToList();

        return section;
    }

    private static ParagraphModel ParseParagraph(JsonElement element)
    {
        var paragraph = new ParagraphModel();
        var type = element.TryGetProperty("$type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        paragraph.Type = type ?? "text";

        if (paragraph.Type == "list" && element.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            paragraph.Items = items.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Array)
                .Select(item => item.EnumerateArray()
                    .Where(run => run.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String)
                    .Select(run => run.GetProperty("text").GetString() ?? "")
                    .ToList())
                .ToList();
        }
        else if (element.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
        {
            paragraph.RunTexts = runs.EnumerateArray()
                .Where(run => run.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String)
                .Select(run => run.GetProperty("text").GetString() ?? "")
                .ToList();
        }

        return paragraph;
    }
}
