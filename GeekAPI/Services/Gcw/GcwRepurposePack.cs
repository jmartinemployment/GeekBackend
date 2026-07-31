using System.Text.Json;

namespace GeekAPI.Services.Gcw;

/// <summary>
/// Parses LLM repurpose pack JSON and maps each variant into a ContentDocument for storage.
/// </summary>
public static class GcwRepurposePack
{
    public sealed record Variant(
        string Channel,
        string Title,
        string? Headline,
        string Body,
        string? Cta,
        IReadOnlyList<string> Hashtags);

    public sealed record Pack(IReadOnlyList<Variant> Variants);

    public static Pack Parse(string packJson)
    {
        using var doc = JsonDocument.Parse(ExtractJsonObject(packJson));
        if (!doc.RootElement.TryGetProperty("variants", out var variantsEl)
            || variantsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Repurpose pack JSON must contain a top-level \"variants\" array.");
        }

        var variants = new List<Variant>();
        foreach (var item in variantsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var channel = GetString(item, "channel") ?? "";
            var title = GetString(item, "title")
                        ?? GetString(item, "label")
                        ?? $"{channel} variant";
            var headline = GetString(item, "headline");
            var body = GetString(item, "body") ?? GetString(item, "text") ?? "";
            var cta = GetString(item, "cta") ?? GetString(item, "callToAction");
            var hashtags = new List<string>();
            if (item.TryGetProperty("hashtags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tags.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.String)
                    {
                        var s = t.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            hashtags.Add(s.Trim());
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(body))
                continue;

            variants.Add(new Variant(
                channel.Trim().ToLowerInvariant(),
                title.Trim(),
                string.IsNullOrWhiteSpace(headline) ? null : headline.Trim(),
                body.Trim(),
                string.IsNullOrWhiteSpace(cta) ? null : cta.Trim(),
                hashtags));
        }

        if (variants.Count == 0)
            throw new InvalidOperationException("Repurpose pack contained no usable variants.");

        return new Pack(variants);
    }

    public static string ToContentDocumentJson(Variant variant)
    {
        var sections = new List<Dictionary<string, object?>>();

        void AddSection(string heading, string text)
        {
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

        if (!string.IsNullOrWhiteSpace(variant.Headline))
            AddSection("Headline", variant.Headline!);
        AddSection("Copy", variant.Body);
        if (!string.IsNullOrWhiteSpace(variant.Cta))
            AddSection("CTA", variant.Cta!);
        if (variant.Hashtags.Count > 0)
            AddSection("Hashtags", string.Join(" ", variant.Hashtags));

        var payload = new Dictionary<string, object?>
        {
            ["lede"] = string.IsNullOrWhiteSpace(variant.Headline)
                ? Truncate(variant.Body, 280)
                : variant.Headline,
            ["sections"] = sections,
        };

        return JsonSerializer.Serialize(payload);
    }

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
            throw new InvalidOperationException("Repurpose response was not a JSON object.");
        return cleaned[start..(end + 1)];
    }
}
