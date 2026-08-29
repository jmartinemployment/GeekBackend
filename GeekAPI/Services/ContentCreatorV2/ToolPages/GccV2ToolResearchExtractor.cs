using System.Text;
using System.Text.Json;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public sealed record GccV2ExtractedToolResearch(
    string Name,
    string Summary,
    string WhatItDoes,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> UseCases,
    string Positioning,
    string Pricing,
    string SourceQuote = "");

public sealed class GccV2ToolResearchExtractor
{
    private readonly GccV2ToolPagePromptBuilder _prompts;
    private readonly ILogger<GccV2ToolResearchExtractor> _logger;

    public GccV2ToolResearchExtractor(
        GccV2ToolPagePromptBuilder prompts,
        ILogger<GccV2ToolResearchExtractor> logger)
    {
        _prompts = prompts;
        _logger = logger;
    }

    public async Task<GccV2ExtractedToolResearch?> ExtractAsync(
        IContentGenerationProvider provider,
        string toolName,
        string? sourceUrl,
        IReadOnlyList<GccQuoteablePage> partnerResearch,
        CancellationToken ct)
    {
        var page = ResolvePage(sourceUrl, partnerResearch);
        var pageText = page is null ? "" : FormatPageText(page);
        if (string.IsNullOrWhiteSpace(pageText))
        {
            _logger.LogWarning("No partner research text for tool {Tool} ({Url}).", toolName, sourceUrl);
            return EmptyResearch(toolName);
        }

        var sourceQuote = page is null ? "" : PickVerbatimQuote(page);
        if (string.IsNullOrWhiteSpace(sourceQuote) && page is not null)
            sourceQuote = PickBestVerbatimQuote(page);

        try
        {
            var fileName = string.IsNullOrWhiteSpace(sourceUrl) ? toolName : sourceUrl;
            var result = await provider.CompleteAsync(
                _prompts.BuildToolResearchExtractionPrompt(fileName, pageText), ct);
            var parsed = LlmResponseJsonParser.Parse<GccV2ExtractedToolResearch>(result.Content, "tool research extraction");
            return parsed with
            {
                Name = string.IsNullOrWhiteSpace(parsed.Name) ? toolName : parsed.Name,
                SourceQuote = ResolveSourceQuote(sourceQuote, parsed.SourceQuote, pageText),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool research extraction failed for {Tool}; keeping verbatim quote only.", toolName);
            return new GccV2ExtractedToolResearch(toolName, "", "", [], [], "", "", sourceQuote);
        }
    }

    public static string SerializeResearch(GccV2ExtractedToolResearch research) =>
        JsonSerializer.Serialize(research, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static GccV2ExtractedToolResearch? DeserializeResearch(JsonElement? element)
    {
        if (element is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }) return null;
        try
        {
            return element.Value.Deserialize<GccV2ExtractedToolResearch>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string BuildAttributionExcerpt(GccV2ExtractedToolResearch? research)
    {
        if (research is null) return "";
        if (!string.IsNullOrWhiteSpace(research.SourceQuote)) return research.SourceQuote.Trim();
        return "";
    }

    /// <summary>Picks a verbatim passage from crawled partner page paragraphs — not a paraphrase.</summary>
    public static string PickVerbatimQuote(GccQuoteablePage page) => PickVerbatimQuote([page]);

    public static string PickVerbatimQuote(string? sourceUrl, IReadOnlyList<GccQuoteablePage> partnerResearch)
    {
        var page = ResolvePage(sourceUrl, partnerResearch);
        return page is null ? "" : PickVerbatimQuote(page);
    }

    public static string PickVerbatimQuote(IReadOnlyList<GccQuoteablePage> pages)
    {
        foreach (var page in pages)
        {
            foreach (var paragraph in page.Paragraphs)
            {
                var candidate = NormalizeQuoteCandidate(paragraph);
                if (IsUsableQuote(candidate)) return candidate;
            }
        }

        return "";
    }

    /// <summary>Best-effort verbatim passage when strict <see cref="PickVerbatimQuote"/> finds nothing — still page text, not paraphrase.</summary>
    public static string PickBestVerbatimQuote(GccQuoteablePage page) => PickBestVerbatimQuote([page]);

    public static string PickBestVerbatimQuote(string? sourceUrl, IReadOnlyList<GccQuoteablePage> partnerResearch)
    {
        var page = ResolvePage(sourceUrl, partnerResearch);
        return page is null ? "" : PickBestVerbatimQuote(page);
    }

    public static string PickBestVerbatimQuote(IReadOnlyList<GccQuoteablePage> pages)
    {
        string? best = null;
        var bestScore = 0;
        foreach (var page in pages)
        {
            foreach (var raw in EnumerateQuoteCandidates(page))
            {
                var candidate = NormalizeQuoteCandidate(raw);
                if (!IsMinimalVerbatimQuote(candidate)) continue;
                if (candidate.Length > bestScore)
                {
                    bestScore = candidate.Length;
                    best = candidate;
                }
            }
        }

        return best ?? "";
    }

    /// <summary>Resolves attribution quote for partner pages — strict verbatim first, then best-effort verbatim from crawled text.</summary>
    public static string ResolveAttributionQuote(
        string? sourceUrl,
        IReadOnlyList<GccQuoteablePage> partnerResearch,
        string? storedQuote,
        string? pageText)
    {
        var quote = PickVerbatimQuote(sourceUrl, partnerResearch);
        if (!string.IsNullOrWhiteSpace(quote)) return quote;

        quote = PickBestVerbatimQuote(sourceUrl, partnerResearch);
        if (!string.IsNullOrWhiteSpace(quote)) return quote;

        if (!string.IsNullOrWhiteSpace(storedQuote) && !string.IsNullOrWhiteSpace(pageText))
        {
            var candidate = StripWrappingQuotes(storedQuote);
            if (IsMinimalVerbatimQuote(candidate) && IsVerbatimFromPage(candidate, pageText))
                return candidate;
        }

        return "";
    }

    internal static string FormatPageText(GccQuoteablePage? page)
    {
        if (page is null) return "";
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(page.Title)) sb.AppendLine(page.Title);
        foreach (var h in page.Headings)
            sb.AppendLine($"H{h.Level}: {h.Text}");
        foreach (var p in page.Paragraphs)
            sb.AppendLine(p);
        return sb.ToString();
    }

    internal static bool IsUsableQuote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return text.Length >= 40 && words >= 8 && words <= 120;
    }

    internal static bool IsMinimalVerbatimQuote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return text.Length >= 20 && words >= 4 && words <= 120;
    }

    internal static string ResolveSourceQuote(string pageQuote, string? llmQuote, string pageText)
    {
        if (IsUsableQuote(pageQuote)) return pageQuote;
        if (IsMinimalVerbatimQuote(pageQuote)) return pageQuote;
        var candidate = StripWrappingQuotes(llmQuote ?? "");
        if (IsMinimalVerbatimQuote(candidate) && IsVerbatimFromPage(candidate, pageText)) return candidate;
        return "";
    }

    internal static bool IsVerbatimFromPage(string quote, string pageText)
    {
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(pageText)) return false;
        return pageText.Contains(quote, StringComparison.OrdinalIgnoreCase)
               || pageText.Contains(StripWrappingQuotes(quote), StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeQuoteCandidate(string raw)
    {
        var text = raw.Trim();
        if (text.Length > 500)
        {
            var cut = text[..500];
            var lastPeriod = cut.LastIndexOf('.');
            if (lastPeriod >= 80) text = cut[..(lastPeriod + 1)];
            else text = cut.TrimEnd() + "…";
        }

        return StripWrappingQuotes(text);
    }

    internal static string StripWrappingQuotes(string text)
    {
        var t = text.Trim();
        while (t.Length >= 2 && (t.StartsWith('"') || t.StartsWith('\u201C')) && (t.EndsWith('"') || t.EndsWith('\u201D')))
        {
            t = t[1..^1].Trim();
        }

        return t;
    }

    private static IEnumerable<string> EnumerateQuoteCandidates(GccQuoteablePage page)
    {
        if (!string.IsNullOrWhiteSpace(page.Title)) yield return page.Title;
        foreach (var paragraph in page.Paragraphs) yield return paragraph;
    }

    private static GccV2ExtractedToolResearch EmptyResearch(string toolName) =>
        new(toolName, "", "", [], [], "", "", "");

    private static GccQuoteablePage? ResolvePage(string? sourceUrl, IReadOnlyList<GccQuoteablePage> partnerResearch)
    {
        if (partnerResearch.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            var match = partnerResearch.FirstOrDefault(p =>
                string.Equals(p.Url, sourceUrl, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }

        return partnerResearch.FirstOrDefault();
    }
}
