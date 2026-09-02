using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public static class GccV2LinkedInCarouselParser
{
    public const int MinSlides = 6;
    public const int MaxSlides = 12;
    public const int MaxBulletsPerSlide = 5;
    public const int MinCaptionWords = 40;
    public const int MaxCaptionWords = 350;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static LinkedInCarouselDraft Parse(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            throw new InvalidOperationException("LinkedIn carousel LLM returned empty content.");

        var json = ExtractJsonObject(rawJson);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("slides", out var slidesEl) || slidesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Carousel JSON must include a slides array.");

        var slides = new List<CarouselSlide>();
        var index = 0;
        foreach (var slideEl in slidesEl.EnumerateArray())
        {
            var role = ReadString(slideEl, "role") ?? GccV2LinkedInCarouselRoles.Teach;
            var title = ReadRequiredString(slideEl, "title", $"slides[{index}].title");
            var subtitle = ReadString(slideEl, "subtitle");
            var bullets = ReadBullets(slideEl);
            slides.Add(new CarouselSlide(index, role, title, bullets, subtitle));
            index++;
        }

        if (slides.Count < MinSlides || slides.Count > MaxSlides)
        {
            throw new InvalidOperationException(
                $"Carousel must have {MinSlides}–{MaxSlides} slides (got {slides.Count}).");
        }

        foreach (var slide in slides)
        {
            if (slide.Bullets.Count > MaxBulletsPerSlide)
            {
                throw new InvalidOperationException(
                    $"Slide '{slide.Title}' has too many bullets (max {MaxBulletsPerSlide}).");
            }
        }

        var caption = ReadRequiredString(root, "caption", "caption");
        var captionWords = CountWords(caption);
        if (captionWords < MinCaptionWords || captionWords > MaxCaptionWords)
        {
            throw new InvalidOperationException(
                $"Carousel caption must be {MinCaptionWords}–{MaxCaptionWords} words (got {captionWords}).");
        }

        var hashtags = ReadHashtags(root);
        var suggestedFilename = ReadString(root, "suggestedFilename")
            ?? ReadString(root, "suggestedFileName")
            ?? SlugifyFilename(slides[0].Title);

        return new LinkedInCarouselDraft(slides, caption.Trim(), hashtags, suggestedFilename);
    }

    private static string ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                return trimmed[start..(end + 1)];
        }

        var first = trimmed.IndexOf('{');
        var last = trimmed.LastIndexOf('}');
        if (first >= 0 && last > first)
            return trimmed[first..(last + 1)];

        return trimmed;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;

    private static string ReadRequiredString(JsonElement el, string name, string label)
    {
        var value = ReadString(el, name);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Carousel JSON missing required field '{label}'.");
        return value;
    }

    private static IReadOnlyList<string> ReadBullets(JsonElement slideEl)
    {
        if (!slideEl.TryGetProperty("bullets", out var bulletsEl) || bulletsEl.ValueKind != JsonValueKind.Array)
            return [];

        return bulletsEl.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Take(MaxBulletsPerSlide)
            .ToList();
    }

    private static IReadOnlyList<string> ReadHashtags(JsonElement root)
    {
        if (!root.TryGetProperty("hashtags", out var tagsEl) || tagsEl.ValueKind != JsonValueKind.Array)
            return [];

        return tagsEl.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.TrimStart('#'))
            .Take(8)
            .ToList();
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string SlugifyFilename(string title)
    {
        var slug = string.Join('_', title.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
        slug = new string(slug.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return string.IsNullOrWhiteSpace(slug) ? "linkedin_carousel" : slug;
    }

    public static string SerializeDraft(LinkedInCarouselDraft draft) =>
        JsonSerializer.Serialize(new
        {
            slides = draft.Slides.Select(s => new
            {
                index = s.Index,
                role = s.Role,
                title = s.Title,
                subtitle = s.Subtitle,
                bullets = s.Bullets,
            }),
            caption = draft.Caption,
            hashtags = draft.Hashtags,
            suggestedFilename = draft.SuggestedFilename,
        }, JsonOpts);
}
