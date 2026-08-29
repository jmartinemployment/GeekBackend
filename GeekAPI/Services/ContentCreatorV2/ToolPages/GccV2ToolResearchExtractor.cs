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
    string Pricing);

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
        var pageText = ResolvePageText(sourceUrl, partnerResearch);
        if (string.IsNullOrWhiteSpace(pageText))
        {
            _logger.LogWarning("No partner research text for tool {Tool} ({Url}).", toolName, sourceUrl);
            return EmptyResearch(toolName);
        }

        try
        {
            var fileName = string.IsNullOrWhiteSpace(sourceUrl) ? toolName : sourceUrl;
            var result = await provider.CompleteAsync(
                _prompts.BuildToolResearchExtractionPrompt(fileName, pageText), ct);
            var parsed = LlmResponseJsonParser.Parse<GccV2ExtractedToolResearch>(result.Content, "tool research extraction");
            return parsed with { Name = string.IsNullOrWhiteSpace(parsed.Name) ? toolName : parsed.Name };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool research extraction failed for {Tool}; using empty research.", toolName);
            return EmptyResearch(toolName);
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
        if (!string.IsNullOrWhiteSpace(research.Summary)) return research.Summary.Trim();
        if (!string.IsNullOrWhiteSpace(research.WhatItDoes)) return research.WhatItDoes.Trim();
        return research.Name;
    }

    private static GccV2ExtractedToolResearch EmptyResearch(string toolName) =>
        new(toolName, "", "", [], [], "", "");

    private static string? ResolvePageText(string? sourceUrl, IReadOnlyList<GccQuoteablePage> partnerResearch)
    {
        if (partnerResearch.Count == 0) return null;

        GccQuoteablePage? page = null;
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            page = partnerResearch.FirstOrDefault(p =>
                string.Equals(p.Url, sourceUrl, StringComparison.OrdinalIgnoreCase));
        }

        page ??= partnerResearch.FirstOrDefault();
        if (page is null) return null;

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(page.Title)) sb.AppendLine(page.Title);
        foreach (var h in page.Headings)
            sb.AppendLine($"H{h.Level}: {h.Text}");
        foreach (var p in page.Paragraphs)
            sb.AppendLine(p);
        return sb.ToString();
    }
}
