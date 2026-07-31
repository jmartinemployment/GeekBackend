using System.Net.Http.Json;
using System.Text.Json;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// HTTP client for the sibling image-generator app (AVIF/WebP packs).
/// </summary>
public sealed class HttpImageGeneratorClient
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpImageGeneratorClient> _logger;

    public HttpImageGeneratorClient(HttpClient http, ILogger<HttpImageGeneratorClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<ImageGeneratorResponse> GenerateAsync(
        string prompt,
        string useCase,
        string? provider,
        string? overlayTitle,
        string? overlaySubtitle,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["useCase"] = useCase,
        };
        if (!string.IsNullOrWhiteSpace(provider))
            body["provider"] = provider.Trim();
        if (!string.IsNullOrWhiteSpace(overlayTitle))
            body["overlayTitle"] = overlayTitle.Trim();
        if (!string.IsNullOrWhiteSpace(overlaySubtitle))
            body["overlaySubtitle"] = overlaySubtitle.Trim();

        using var response = await _http.PostAsJsonAsync("/api/generate", body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "image-generator failed ({Status}): {Body}",
                (int)response.StatusCode,
                text.Length > 400 ? text[..400] : text);
            throw new InvalidOperationException(
                $"image-generator returned {(int)response.StatusCode}: {Truncate(text, 300)}");
        }

        var parsed = JsonSerializer.Deserialize<ImageGeneratorResponse>(
            text,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed?.Images is null || parsed.Images.Count == 0)
            throw new InvalidOperationException("image-generator returned no images");
        return parsed;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "…";
}

public sealed class ImageGeneratorResponse
{
    public List<ImageGeneratorImage> Images { get; set; } = [];
    public List<ImageGeneratorError>? Errors { get; set; }
}

public sealed class ImageGeneratorImage
{
    public string Provider { get; set; } = "";
    public string? UseCase { get; set; }
    public int DurationMs { get; set; }
    public ImageGeneratorFormats Formats { get; set; } = new();
}

public sealed class ImageGeneratorFormats
{
    public ImageGeneratorFormat? Avif { get; set; }
    public ImageGeneratorFormat? Webp { get; set; }
}

public sealed class ImageGeneratorFormat
{
    public string BytesBase64 { get; set; } = "";
    public string ContentType { get; set; } = "";
}

public sealed class ImageGeneratorError
{
    public string Provider { get; set; } = "";
    public string Error { get; set; } = "";
}

public static class GcwVisualCatalog
{
    public static readonly IReadOnlyList<(string Slug, string Name, string Hint)> UseCases =
    [
        ("pillar-figure", "Pillar figure", "Landscape editorial illustration for the article."),
        ("social-linkedin", "LinkedIn / social", "1200×630 feed graphic."),
        ("social-facebook", "Facebook / OG social", "1200×630 share graphic."),
        ("youtube-thumbnail", "YouTube thumbnail", "1280×720 thumbnail concept."),
        ("og-image", "OG image", "Open Graph share card."),
    ];

    public static bool IsKnownUseCase(string useCase) =>
        UseCases.Any(u => u.Slug.Equals(useCase, StringComparison.OrdinalIgnoreCase));

    public sealed record VisualCopy(string Headline, string Subtitle, string Caption);

    /// <summary>
    /// Derive on-image headline + post caption from the source draft (and optional direction).
    /// </summary>
    public static VisualCopy BuildCopy(string bodyDocumentJson, string useCase, string? extraDirection)
    {
        ExtractContent(bodyDocumentJson, out var lede, out var headings);
        var useCaseName = UseCases.FirstOrDefault(u =>
            u.Slug.Equals(useCase, StringComparison.OrdinalIgnoreCase)).Name;
        if (string.IsNullOrWhiteSpace(useCaseName))
            useCaseName = useCase;

        string headline;
        if (!string.IsNullOrWhiteSpace(extraDirection) && extraDirection!.Trim().Length <= 80)
            headline = extraDirection.Trim();
        else if (headings.Count > 0)
            headline = TruncateWords(headings[0], 10);
        else if (!string.IsNullOrWhiteSpace(lede))
            headline = TruncateWords(FirstSentence(lede), 10);
        else
            headline = useCaseName;

        var subtitle = headings.Count > 1
            ? TruncateWords(headings[1], 12)
            : useCaseName;

        var caption = !string.IsNullOrWhiteSpace(lede)
            ? Truncate(lede.Trim(), 320)
            : Truncate(string.Join(" · ", headings.Take(3)), 320);

        if (!string.IsNullOrWhiteSpace(extraDirection) && extraDirection!.Trim().Length > 80)
            caption = Truncate($"{extraDirection.Trim()}\n\n{caption}", 400);

        return new VisualCopy(headline, subtitle, caption);
    }

    public static string BuildPrompt(
        string bodyDocumentJson,
        string useCase,
        string? extraDirection,
        VisualCopy copy)
    {
        var excerpt = ExtractExcerpt(bodyDocumentJson);
        var useCaseName = UseCases.FirstOrDefault(u =>
            u.Slug.Equals(useCase, StringComparison.OrdinalIgnoreCase)).Name;
        if (string.IsNullOrWhiteSpace(useCaseName))
            useCaseName = useCase;

        var direction = string.IsNullOrWhiteSpace(extraDirection)
            ? ""
            : $"\nCreative direction: {extraDirection.Trim()}";

        return $"""
            Create a polished marketing background visual for use case “{useCaseName}”.
            Style: clean, modern, high-contrast, cinematic lighting, no watermarks,
            no logos, no UI chrome. Leave the lower third relatively dark/uncluttered —
            readable title text will be composited later (do NOT paint words into the image).
            Subject grounded in this content excerpt:
            {excerpt}
            Intended headline (for composition, not to draw): “{copy.Headline}”
            {direction}
            """.Trim();
    }

    public static string ToContentDocumentJson(
        string prompt,
        string useCase,
        string provider,
        string contentType,
        string bytesBase64,
        VisualCopy copy)
    {
        var dataUrl = $"data:{contentType};base64,{bytesBase64}";
        var sections = new List<object>
        {
            TextSection("Headline", copy.Headline),
            TextSection("Subtitle", copy.Subtitle),
            TextSection("Caption / post copy", copy.Caption),
            new Dictionary<string, object?>
            {
                ["heading"] = "Visual",
                ["paragraphs"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["$type"] = "image",
                        ["alt"] = copy.Headline,
                        ["contentType"] = contentType,
                        ["src"] = dataUrl,
                    },
                },
            },
            TextSection("Prompt", prompt),
        };

        var payload = new Dictionary<string, object?>
        {
            ["lede"] = copy.Headline,
            ["sections"] = sections,
        };
        return JsonSerializer.Serialize(payload);
    }

    private static Dictionary<string, object?> TextSection(string heading, string text) =>
        new()
        {
            ["heading"] = heading,
            ["paragraphs"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["$type"] = "text",
                    ["runs"] = new object[]
                    {
                        new Dictionary<string, object?> { ["text"] = text },
                    },
                },
            },
        };

    private static void ExtractContent(
        string bodyDocumentJson,
        out string lede,
        out List<string> headings)
    {
        lede = "";
        headings = [];
        if (string.IsNullOrWhiteSpace(bodyDocumentJson))
            return;
        try
        {
            using var doc = JsonDocument.Parse(bodyDocumentJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("lede", out var ledeEl) && ledeEl.ValueKind == JsonValueKind.String)
                lede = ledeEl.GetString()?.Trim() ?? "";
            if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    if (section.TryGetProperty("heading", out var h) && h.ValueKind == JsonValueKind.String)
                    {
                        var hs = h.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(hs))
                            headings.Add(hs);
                    }
                }
            }
        }
        catch (JsonException)
        {
            lede = Truncate(bodyDocumentJson, 400);
        }
    }

    private static string FirstSentence(string text)
    {
        var t = text.Trim();
        var idx = t.IndexOfAny(['.', '!', '?']);
        if (idx > 20)
            return t[..(idx + 1)].Trim();
        return t;
    }

    private static string TruncateWords(string text, int maxWords)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return string.Join(" ", words);
        return string.Join(" ", words.Take(maxWords));
    }

    private static string ExtractExcerpt(string bodyDocumentJson)
    {
        ExtractContent(bodyDocumentJson, out var lede, out var headings);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(lede))
            parts.Add(lede);
        parts.AddRange(headings.Take(3));
        var joined = string.Join(" — ", parts);
        if (string.IsNullOrWhiteSpace(joined))
            return Truncate(bodyDocumentJson, 600);
        return Truncate(joined, 900);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..(max - 1)] + "…";
}
