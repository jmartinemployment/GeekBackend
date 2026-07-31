using System.Text.Json;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// VidIQ-class YouTube / video SEO pack parsing + ContentDocument mapping.
/// </summary>
public static class GcwVideoSeoPack
{
    public sealed record Section(
        string Kind,
        string Title,
        string Body,
        IReadOnlyList<string> Items);

    public sealed record Pack(IReadOnlyList<Section> Sections);

    public static readonly IReadOnlyList<(string Kind, string Label, string Guidance)> Spec =
    [
        ("titles", "Title options",
            "Provide exactly 5 distinct YouTube title options (≤70 chars). Put each title as an items[] entry; body can summarize the strategy."),
        ("description", "Description",
            "Write a full YouTube description: hook paragraph, value bullets, CTA, and timestamps placeholder note. Put the full text in body."),
        ("tags", "Tags",
            "Provide 12–18 discoverability tags/phrases in items[]. Body can note primary vs secondary."),
        ("chapters", "Chapters",
            "Provide 6–10 chapter lines in items[] as \"0:00 Intro\" style. Body can explain chapter strategy."),
        ("thumbnails", "Thumbnail concepts",
            "Provide 3 thumbnail text/concept ideas in items[] (short overlay text + visual idea). Body optional."),
        ("shorts", "Shorts / reel hooks",
            "Provide 3 short-form hooks in items[] (≤25 words each) suitable for Shorts/Reels. Body optional."),
    ];

    public static string BuildPackBrief()
    {
        var lines = Spec.Select(s => $"- kind={s.Kind}: {s.Label}. {s.Guidance}");
        return
            "Produce exactly one section object per kind below (no extras, none missing).\n" +
            string.Join("\n", lines);
    }

    public static Pack Parse(string packJson)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(packJson));
        if (!doc.RootElement.TryGetProperty("sections", out var sectionsEl)
            || sectionsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Video SEO pack JSON must contain a top-level \"sections\" array.");
        }

        var sections = new List<Section>();
        foreach (var item in sectionsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var kind = GetString(item, "kind") ?? GetString(item, "type") ?? "";
            var title = GetString(item, "title") ?? kind;
            var body = GetString(item, "body") ?? GetString(item, "text") ?? "";
            var items = new List<string>();
            if (item.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            items.Add(s.Trim());
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(kind))
                continue;
            if (string.IsNullOrWhiteSpace(body) && items.Count == 0)
                continue;

            sections.Add(new Section(
                kind.Trim().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(title) ? kind : title.Trim(),
                body.Trim(),
                items));
        }

        if (sections.Count == 0)
            throw new InvalidOperationException("Video SEO pack contained no usable sections.");

        return new Pack(sections);
    }

    public static string ToContentDocumentJson(Section section)
    {
        var sections = new List<Dictionary<string, object?>>();

        void AddTextSection(string heading, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            sections.Add(new Dictionary<string, object?>
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
            });
        }

        void AddListSection(string heading, IReadOnlyList<string> items)
        {
            if (items.Count == 0)
                return;
            sections.Add(new Dictionary<string, object?>
            {
                ["heading"] = heading,
                ["paragraphs"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["$type"] = "list",
                        ["ordered"] = false,
                        ["items"] = items.Select(i => new object[]
                        {
                            new Dictionary<string, object?> { ["text"] = i },
                        }).ToArray(),
                    },
                },
            });
        }

        AddTextSection("Overview", section.Body);
        AddListSection(
            section.Kind switch
            {
                "titles" => "Title options",
                "tags" => "Tags",
                "chapters" => "Chapters",
                "thumbnails" => "Thumbnail concepts",
                "shorts" => "Shorts hooks",
                _ => "Items",
            },
            section.Items);

        if (sections.Count == 0)
            AddTextSection("Copy", section.Title);

        var lede = !string.IsNullOrWhiteSpace(section.Body)
            ? Truncate(section.Body, 280)
            : section.Items.FirstOrDefault() ?? section.Title;

        var payload = new Dictionary<string, object?>
        {
            ["lede"] = lede,
            ["sections"] = sections,
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string ChannelLabel(string kind) => kind.ToLowerInvariant() switch
    {
        "titles" => "YouTube titles",
        "description" => "YouTube description",
        "tags" => "YouTube tags",
        "chapters" => "YouTube chapters",
        "thumbnails" => "Thumbnail concepts",
        "shorts" => "Shorts hooks",
        _ => $"Video · {kind}",
    };

    private static string Truncate(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= max)
            return text ?? "";
        return text[..(max - 1)].TrimEnd() + "…";
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static string ExtractJsonObject(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            cleaned = firstNewline >= 0 ? cleaned[(firstNewline + 1)..] : cleaned;
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                cleaned = cleaned[..lastFence];
            cleaned = cleaned.Trim();
        }

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Video SEO response was not a JSON object.");
        return cleaned[start..(end + 1)];
    }
}
