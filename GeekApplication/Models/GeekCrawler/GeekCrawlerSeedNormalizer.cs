using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GeekApplication.Models.GeekCrawler;

public static class GeekCrawlerSeedNormalizer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<string> NormalizeSeeds(IEnumerable<string>? rawSeeds)
    {
        if (rawSeeds is null) return [];

        var urls = new List<string>();
        foreach (var raw in rawSeeds)
        {
            if (!TryNormalizeSeedUrl(raw, out var normalized)) continue;
            if (!urls.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                urls.Add(normalized);
        }

        return urls;
    }

    public static string NormalizeOriginAuthority(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return origin;

        var host = uri.Host;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host[4..];

        return $"{uri.Scheme}://{host}";
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<string>> GroupSeedsByOrigin(
        IReadOnlyList<string> seedUrls)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in seedUrls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            var origin = NormalizeOriginAuthority(uri.GetLeftPart(UriPartial.Authority));
            if (!map.TryGetValue(origin, out var list))
            {
                list = [];
                map[origin] = list;
            }

            if (!list.Contains(url, StringComparer.OrdinalIgnoreCase))
                list.Add(url);
        }

        return map.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool SeedUrlsMatch(string? seedUrlsJson, IReadOnlyList<string> expectedSeeds)
    {
        if (expectedSeeds.Count == 0) return false;
        List<string> stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<string>>(seedUrlsJson ?? "[]", JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return false;
        }

        if (stored.Count != expectedSeeds.Count) return false;
        var set = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);
        return expectedSeeds.All(u => set.Contains(u));
    }

    public static string SerializeSeeds(IReadOnlyList<string> seeds) =>
        JsonSerializer.Serialize(seeds, JsonOpts);

    public static string ComputeSeedKey(IReadOnlyList<string> normalizedSeeds)
    {
        var sorted = normalizedSeeds.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var json = SerializeSeeds(sorted);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns an error message when any raw seed is invalid; otherwise null.</summary>
    public static string? ValidateRawSeeds(IEnumerable<string>? rawSeeds)
    {
        if (rawSeeds is null)
            return "At least one seed URL is required.";

        foreach (var raw in rawSeeds)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!TryNormalizeSeedUrl(raw, out _))
                return $"Invalid seed URL: {raw.Trim()}";
        }

        return null;
    }

    public static bool TryNormalizeSeedUrl(string? raw, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        trimmed = StripListPrefix(trimmed);
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (trimmed.StartsWith("//"))
                trimmed = "https:" + trimmed;
            else
                trimmed = "https://" + trimmed.TrimStart('/');
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;
        if (string.IsNullOrWhiteSpace(uri.Host)) return false;
        if (!IsValidCrawlHost(uri.Host)) return false;

        url = uri.GetLeftPart(UriPartial.Query);
        if (url.EndsWith('/') && uri.AbsolutePath == "/")
            url = url.TrimEnd('/');
        return true;
    }

    private static string StripListPrefix(string trimmed)
    {
        if (trimmed.StartsWith("* ", StringComparison.Ordinal)
            || trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("+ ", StringComparison.Ordinal))
        {
            return trimmed[2..].TrimStart();
        }

        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]))
            i++;
        if (i > 0 && i < trimmed.Length && trimmed[i] == '.' && i + 1 < trimmed.Length && trimmed[i + 1] == ' ')
            return trimmed[(i + 2)..].TrimStart();

        return trimmed;
    }

    private static bool IsValidCrawlHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IPAddress.TryParse(host, out _))
            return true;
        return host.Contains('.');
    }
}
