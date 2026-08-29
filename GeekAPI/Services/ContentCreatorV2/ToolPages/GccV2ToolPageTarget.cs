using System.Text.Json;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

public sealed record GccV2ToolPageTarget(
    string Kind,
    string Name,
    string Slug,
    string? OnSiteHref,
    string? SourceUrl,
    JsonElement? ExtractedResearch,
    int Order)
{
    public bool IsOverview => string.Equals(Kind, "overview", StringComparison.OrdinalIgnoreCase);
    public bool IsPartner => string.Equals(Kind, "partner", StringComparison.OrdinalIgnoreCase);
}

public static class GccV2ToolPageTargetParser
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static GccV2ToolPageTarget? Parse(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!doc.RootElement.TryGetProperty("toolPageTarget", out var el)
                || el.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var kind = ReadString(el, "kind") ?? "";
            var name = ReadString(el, "name") ?? "";
            var slug = ReadString(el, "slug") ?? "";
            if (string.IsNullOrWhiteSpace(kind)) return null;

            JsonElement? research = el.TryGetProperty("extractedResearch", out var researchEl)
                && researchEl.ValueKind != JsonValueKind.Null
                && researchEl.ValueKind != JsonValueKind.Undefined
                ? researchEl.Clone()
                : null;

            return new GccV2ToolPageTarget(
                kind,
                name,
                slug,
                ReadString(el, "onSiteHref"),
                ReadString(el, "sourceUrl"),
                research,
                el.TryGetProperty("order", out var orderEl) && orderEl.TryGetInt32(out var order) ? order : 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string MergeOverviewTarget(string? rawBriefJson, string? targetKeyword)
    {
        var slug = GccV2ToolSlugHelper.SlugifyKeyword(targetKeyword);
        var name = string.IsNullOrWhiteSpace(targetKeyword) ? "Tool overview" : targetKeyword.Trim();
        var onSiteHref = $"/tools/{slug}";

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "toolPageTarget", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("toolPageTarget");
                writer.WriteStartObject();
                writer.WriteString("kind", "overview");
                writer.WriteString("name", name);
                writer.WriteString("slug", slug);
                writer.WriteString("onSiteHref", onSiteHref);
                writer.WriteNumber("order", 0);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new
            {
                toolPageTarget = new
                {
                    kind = "overview",
                    name,
                    slug,
                    onSiteHref,
                    order = 0,
                },
            }, JsonOpts);
        }
    }

    public static string ResolveTabLabel(string? contentType, string? rawBriefJson)
    {
        if (!string.Equals(contentType, "tool", StringComparison.OrdinalIgnoreCase))
            return "";
        var target = Parse(rawBriefJson);
        if (target?.IsPartner == true && !string.IsNullOrWhiteSpace(target.Name))
            return $"Tool · {target.Name}";
        return "Tool page";
    }

    public static string SerializePartnerBriefSlice(
        string name,
        string slug,
        string? sourceUrl,
        object? extractedResearch,
        int order)
    {
        return JsonSerializer.Serialize(new
        {
            toolPageTarget = new
            {
                kind = "partner",
                name,
                slug,
                onSiteHref = GccV2ToolSlugHelper.OnSiteHref(slug),
                sourceUrl,
                extractedResearch,
                order,
            },
        }, JsonOpts);
    }

    private static string? ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return null;
        var value = el.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
