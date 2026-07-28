namespace ImportBlogContent;

public sealed record ImportManifestEntry(
    string Url,
    string PostType,
    string Slug,
    string Department,
    string LanguageCode = "en");

public static class ManifestDiscovery
{
    private static readonly (string Prefix, string PostType)[] Sections =
    [
        ("/blog/", "BlogPosting"),
        ("/use-cases/", "TechnicalArticle"),
        ("/tools/", "TechnicalArticle"),
    ];

    public static async Task<IReadOnlyList<ImportManifestEntry>> DiscoverAsync(
        HttpClient http,
        string baseUrl,
        CancellationToken ct = default)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var entries = new Dictionary<string, ImportManifestEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (prefix, postType) in Sections)
        {
            var indexUrl = $"{baseUrl}{prefix.TrimEnd('/')}";
            await CrawlIndexAsync(http, baseUrl, indexUrl, prefix, postType, entries, ct);

            var html = await http.GetStringAsync(indexUrl, ct);
            var deptLinks = LinkExtractor.ExtractSameHostLinks(html, baseUrl)
                .Where(u => u.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .Where(u => u.TrimEnd('/').Count(c => c == '/') >= prefix.Count(c => c == '/') + 1)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var deptUrl in deptLinks)
            {
                if (deptUrl.TrimEnd('/').Equals(indexUrl, StringComparison.OrdinalIgnoreCase))
                    continue;

                await CrawlIndexAsync(http, baseUrl, deptUrl, prefix, postType, entries, ct);
            }
        }

        return entries.Values.OrderBy(e => e.Slug, StringComparer.Ordinal).ToList();
    }

    private static async Task CrawlIndexAsync(
        HttpClient http,
        string baseUrl,
        string pageUrl,
        string prefix,
        string postType,
        Dictionary<string, ImportManifestEntry> entries,
        CancellationToken ct)
    {
        string html;
        try
        {
            html = await http.GetStringAsync(pageUrl, ct);
        }
        catch
        {
            return;
        }

        foreach (var link in LinkExtractor.ExtractSameHostLinks(html, baseUrl))
        {
            if (!TryParseArticle(link, baseUrl, prefix, postType, out var entry))
                continue;

            entries[entry.Slug] = entry;
        }
    }

    internal static bool TryParseArticle(
        string url,
        string baseUrl,
        string prefix,
        string postType,
        out ImportManifestEntry entry)
    {
        entry = null!;

        if (!url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
            return false;

        var path = url[baseUrl.Length..].TrimStart('/');
        if (!path.StartsWith(prefix.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var prefixSegments = prefix.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        if (segments.Length < prefixSegments + 2)
            return false;

        var department = segments[prefixSegments];
        if (department is "blog" or "use-cases" or "tools")
            return false;

        var slug = string.Join('/', segments);
        entry = new ImportManifestEntry(url, postType, slug, department);
        return true;
    }
}
