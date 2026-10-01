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

/// <summary>
/// Serialization and verification helpers for the v2 tool pages' extracted partner research.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not choose a quotation, 2026-10-01.</b> It used to, in four steps that were a second
/// way to pick one: the first paragraph over forty characters; failing that, the longest paragraph,
/// with the page <i>title</i> among the candidates; truncated at five hundred characters with an
/// ellipsis appended, which edits a quotation and then presents it as verbatim; and failing all of
/// that, the model's own retyped sentence, checked by substring. The methods are deleted and are
/// deliberately not named, here or anywhere: a name in a comment is what the next reader greps.
/// </para>
/// <para>
/// Its input was <c>GccQuoteablePage.Paragraphs</c>, which is <c>RenderChunk</c> output, so a
/// paragraph here begins <c>Section:</c> or <c>Specific detail:</c>. With no stripper on this path,
/// the first span long enough to pass could be a prompt label in a quote box attributed to a partner.
/// </para>
/// <para>
/// <b>There is one way to choose a quotable span: <c>GccQuoteCandidates</c>, over a partner page's
/// typed blocks.</b> The model answers with a candidate's number, so no quotation is ever retyped
/// and nothing has to be matched back. What was removed here was reachable only from
/// <c>GccV2WriteService.WriteAsync</c>, which has no live caller — the job worker routes writing to
/// <c>V1Restore.GccV2V1WriteAdapter</c> — and from the <c>ExtractAsync</c> below.
/// </para>
/// <para>
/// <b><c>ExtractAsync</c> no longer returns a <c>SourceQuote</c>, and no longer refuses without one.</b>
/// Its job is the structured fields the tool brief slice needs — summary, features, use cases,
/// positioning, pricing. The quotation was a bolt-on: it picked one before the model was even called
/// and threw if that pick came back empty, so a partner page whose first long paragraph happened to be
/// a prompt label decided whether extraction ran at all. The requirement that a partner tool page
/// carry a block quotation is unchanged and enforced where the page is built —
/// <c>RequireSourceAttributionHtml</c> refuses without one, and on the live path
/// <c>GccToolQuoteGuard</c> checks the draft against the candidates the writer was shown.
/// </para>
/// <para>
/// What stays is what live code reads: the serializers, <c>ExtractAsync</c>, <c>FormatPageText</c>,
/// and the two verifiers. <c>IsVerbatimFromPage</c> and <c>StripWrappingQuotes</c> check and sanitise
/// text someone else chose, which is the opposite job from producing it.
/// </para>
/// </remarks>
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

        try
        {
            var fileName = string.IsNullOrWhiteSpace(sourceUrl) ? toolName : sourceUrl;
            var result = await provider.CompleteAsync(
                _prompts.BuildToolResearchExtractionPrompt(fileName, pageText), ct);
            var parsed = LlmResponseJsonParser.Parse<GccV2ExtractedToolResearch>(result.Content, "tool research extraction");
            return parsed with
            {
                Name = string.IsNullOrWhiteSpace(parsed.Name) ? toolName : parsed.Name,

                // Blank, always. The model is asked for the structured fields and whatever it writes
                // here is its own wording about the partner -- which is exactly what a quote box must
                // never carry. A quotation comes from GccQuoteCandidates over the page's typed blocks,
                // chosen by number, or the page refuses.
                SourceQuote = "",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool research extraction failed for {Tool}.", toolName);
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

    internal static bool IsVerbatimFromPage(string quote, string pageText)
    {
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(pageText)) return false;
        return pageText.Contains(quote, StringComparison.OrdinalIgnoreCase)
               || pageText.Contains(StripWrappingQuotes(quote), StringComparison.OrdinalIgnoreCase);
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
