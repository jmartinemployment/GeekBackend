using System.Text;
using System.Text.Json;
using GeekApplication.Models.ContentCreator;

namespace GeekAPI.Services.ContentCreatorV2.Partner;

/// <summary>Brief JSON helpers for partner/competitor tool rows — no crawl/fetch.</summary>
public static class GccV2PartnerUrlResearchService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<string> CollectPartnerHrefs(string? rawBriefJson) =>
        CollectPartnerToolRows(rawBriefJson)
            .Where(t => !string.IsNullOrWhiteSpace(t.Url))
            .OrderBy(t => string.Equals(t.Source, "operator", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(t => t.Url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GccPartnerResearchCaps.MaxUrls)
            .ToList();

    public static IReadOnlyList<string> CollectCompetitorHrefs(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "competitorUrls", out var el)
                || el.ValueKind != JsonValueKind.String)
            {
                return [];
            }

            var urls = new List<string>();
            foreach (var line in el.GetString()!.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!TryNormalizeHttpUrl(line, out var normalized)) continue;
                if (urls.Contains(normalized, StringComparer.OrdinalIgnoreCase)) continue;
                urls.Add(normalized);
                if (urls.Count >= 5) break;
            }

            return urls;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<PartnerToolRow> CollectPartnerToolRows(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            var root = doc.RootElement;

            var rows = new List<PartnerToolRow>();
            if (TryGetPropertyIgnoreCase(root, "hierarchyPlan", out var plan)
                && plan.ValueKind == JsonValueKind.Object
                && TryGetPropertyIgnoreCase(plan, "recommendedTools", out var tools)
                && tools.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tools.EnumerateArray())
                {
                    if (rows.Count >= GccPartnerResearchCaps.MaxUrls) break;
                    if (t.ValueKind != JsonValueKind.Object) continue;
                    var name = TryGetPropertyIgnoreCase(t, "name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()?.Trim()
                        : null;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (rows.Any(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                    var href = TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String
                        ? h.GetString()
                        : TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString()
                            : null;
                    string? url = null;
                    if (!string.IsNullOrWhiteSpace(href) && TryNormalizeHttpUrl(href, out var normalized))
                        url = normalized;
                    rows.Add(new PartnerToolRow(name!, url, "crawl"));
                }
            }

            if (rows.Count == 0) return [];

            if (TryGetPropertyIgnoreCase(root, "operatorTools", out var ops)
                && ops.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in ops.EnumerateArray())
                {
                    string? opName = null;
                    string? opUrl = null;
                    if (t.ValueKind == JsonValueKind.String)
                    {
                        opUrl = t.GetString();
                    }
                    else if (t.ValueKind == JsonValueKind.Object)
                    {
                        opName = TryGetPropertyIgnoreCase(t, "name", out var n) && n.ValueKind == JsonValueKind.String
                            ? n.GetString()?.Trim()
                            : null;
                        opUrl = TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String
                            ? u.GetString()
                            : TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String
                                ? h.GetString()
                                : null;
                    }
                    else continue;

                    if (string.IsNullOrWhiteSpace(opUrl) || !TryNormalizeHttpUrl(opUrl, out var dest))
                        continue;

                    var idx = FindCrawlToolIndex(rows, opName, dest);
                    if (idx < 0) continue;
                    rows[idx] = rows[idx] with { Url = dest, Source = "operator" };
                }
            }

            return rows;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> CollectOperatorSeedUrls(string? rawBriefJson)
    {
        if (string.IsNullOrWhiteSpace(rawBriefJson)) return [];

        try
        {
            using var doc = JsonDocument.Parse(rawBriefJson);
            if (!TryGetPropertyIgnoreCase(doc.RootElement, "operatorTools", out var ops)
                || ops.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var urls = new List<string>();
            foreach (var t in ops.EnumerateArray())
            {
                string? opUrl = null;
                if (t.ValueKind == JsonValueKind.String)
                    opUrl = t.GetString();
                else if (t.ValueKind == JsonValueKind.Object)
                {
                    opUrl = TryGetPropertyIgnoreCase(t, "url", out var u) && u.ValueKind == JsonValueKind.String
                        ? u.GetString()
                        : TryGetPropertyIgnoreCase(t, "href", out var h) && h.ValueKind == JsonValueKind.String
                            ? h.GetString()
                            : null;
                }

                if (string.IsNullOrWhiteSpace(opUrl) || !TryNormalizeHttpUrl(opUrl, out var normalized))
                    continue;
                if (!urls.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    urls.Add(normalized);
            }

            return urls;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupOperatorSeedsByOrigin(
        IReadOnlyList<string> seedUrls)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in seedUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var origin = uri.GetLeftPart(UriPartial.Authority);
            if (!map.TryGetValue(origin, out var list))
            {
                list = [];
                map[origin] = list;
            }

            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.OrdinalIgnoreCase);
    }

    public sealed record PartnerToolRow(string Name, string? Url, string Source);

    public static string? MergePartnerResearchIntoBriefJson(
        string? rawBriefJson,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "partnerResearch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("partnerResearch");
                JsonSerializer.Serialize(writer, pages, JsonOpts);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    public static string? MergeCompetitorResearchIntoBriefJson(
        string? rawBriefJson,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "competitorResearch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("competitorResearch");
                JsonSerializer.Serialize(writer, pages, JsonOpts);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    public static string? MergeLocalResearchIntoBriefJson(
        string? rawBriefJson,
        IReadOnlyList<GccQuoteablePage> pages)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBriefJson) ? "{}" : rawBriefJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return rawBriefJson;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "localResearch", StringComparison.OrdinalIgnoreCase))
                        continue;
                    prop.WriteTo(writer);
                }

                writer.WritePropertyName("localResearch");
                JsonSerializer.Serialize(writer, pages, JsonOpts);
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return rawBriefJson;
        }
    }

    private static int FindCrawlToolIndex(IReadOnlyList<PartnerToolRow> rows, string? opName, string destUrl)
    {
        if (!string.IsNullOrWhiteSpace(opName))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Name.Equals(opName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        var hostGuess = HostLabelFromUrl(destUrl);
        if (string.IsNullOrWhiteSpace(hostGuess)) return -1;

        for (var i = 0; i < rows.Count; i++)
        {
            var compactTool = CompactAlnum(rows[i].Name);
            var compactHost = CompactAlnum(hostGuess);
            if (compactTool.Length == 0 || compactHost.Length == 0) continue;
            if (compactTool == compactHost
                || compactTool.Contains(compactHost, StringComparison.Ordinal)
                || compactHost.Contains(compactTool, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static string HostLabelFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host.Trim().TrimStart('.');
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host[4..];
            return host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }
        catch (UriFormatException)
        {
            return "";
        }
    }

    private static string CompactAlnum(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant);
        return string.Concat(chars);
    }

    private static bool TryNormalizeHttpUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("//"))
                trimmed = "https:" + trimmed;
            else
                return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        url = uri.GetLeftPart(UriPartial.Query);
        if (url.EndsWith('/') && uri.AbsolutePath == "/")
            url = url.TrimEnd('/');
        return true;
    }

    internal static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value;
            return true;
        }

        value = default;
        return false;
    }
}
