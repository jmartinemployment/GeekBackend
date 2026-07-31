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
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["useCase"] = useCase,
        };
        if (!string.IsNullOrWhiteSpace(provider))
            body["provider"] = provider.Trim();

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

    public static string BuildPrompt(string bodyDocumentJson, string useCase, string? extraDirection)
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
            Create a polished marketing visual for use case “{useCaseName}”.
            Style: clean, modern, high-contrast, no watermarks, no fake UI chrome,
            readable if text appears (prefer minimal or no small text).
            Subject grounded in this content excerpt:
            {excerpt}
            {direction}
            """.Trim();
    }

    public static string ToContentDocumentJson(
        string prompt,
        string useCase,
        string provider,
        string contentType,
        string bytesBase64)
    {
        var dataUrl = $"data:{contentType};base64,{bytesBase64}";
        var payload = new Dictionary<string, object?>
        {
            ["lede"] = $"Generated visual ({useCase}) via {provider}",
            ["sections"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["heading"] = "Visual",
                    ["paragraphs"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["$type"] = "image",
                            ["alt"] = useCase,
                            ["contentType"] = contentType,
                            ["src"] = dataUrl,
                        },
                    },
                },
                new Dictionary<string, object?>
                {
                    ["heading"] = "Prompt",
                    ["paragraphs"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["$type"] = "text",
                            ["runs"] = new object[]
                            {
                                new Dictionary<string, object?> { ["text"] = prompt },
                            },
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string ExtractExcerpt(string bodyDocumentJson)
    {
        if (string.IsNullOrWhiteSpace(bodyDocumentJson))
            return "(empty draft)";
        try
        {
            using var doc = JsonDocument.Parse(bodyDocumentJson);
            var root = doc.RootElement;
            var parts = new List<string>();
            if (root.TryGetProperty("lede", out var lede) && lede.ValueKind == JsonValueKind.String)
            {
                var s = lede.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add(s.Trim());
            }
            if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray().Take(3))
                {
                    if (section.TryGetProperty("heading", out var h) && h.ValueKind == JsonValueKind.String)
                    {
                        var hs = h.GetString();
                        if (!string.IsNullOrWhiteSpace(hs))
                            parts.Add(hs.Trim());
                    }
                }
            }
            var joined = string.Join(" — ", parts);
            if (joined.Length > 900)
                joined = joined[..897] + "…";
            return string.IsNullOrWhiteSpace(joined) ? Truncate(bodyDocumentJson, 600) : joined;
        }
        catch (JsonException)
        {
            return Truncate(bodyDocumentJson, 600);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
