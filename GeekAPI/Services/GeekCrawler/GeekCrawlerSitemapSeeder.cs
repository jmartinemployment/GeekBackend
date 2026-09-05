using System.Text.RegularExpressions;
using System.Xml.Linq;
using GeekAPI.Services.GeekCrawler.Polite;

using GeekApplication.Models.GeekCrawler;

namespace GeekAPI.Services.GeekCrawler;

/// <summary>Fetches and parses /sitemap.xml to seed BFS queues.</summary>
public sealed class GeekCrawlerSitemapSeeder
{
    private readonly HttpClient _http;
    private readonly GeekCrawlerPoliteGate _polite;
    private readonly ILogger<GeekCrawlerSitemapSeeder> _logger;

    public GeekCrawlerSitemapSeeder(
        HttpClient http,
        GeekCrawlerPoliteGate polite,
        ILogger<GeekCrawlerSitemapSeeder> logger)
    {
        _http = http;
        _polite = polite;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> CollectAllowedUrlsAsync(
        string origin,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
            return [];

        var sitemapUrl = new Uri(originUri, "/sitemap.xml");
        string xml;
        try
        {
            using var response = await _http.GetAsync(sitemapUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "No sitemap at {SitemapUrl} (HTTP {Status}).",
                    sitemapUrl,
                    (int)response.StatusCode);
                return [];
            }

            xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "No sitemap found at {SitemapUrl}", sitemapUrl);
            return [];
        }

        await _polite.EnsureRobotsForOriginAsync(origin, ct).ConfigureAwait(false);

        var parsed = await ParseSitemapUrlsAsync(xml, originUri, ct).ConfigureAwait(false);
        var allowed = new List<string>();
        foreach (var url in parsed)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                continue;
            if (!_polite.IsUrlAllowed(uri))
                continue;
            allowed.Add(url);
        }

        if (allowed.Count > 0)
        {
            _logger.LogInformation(
                "Seeded {Count} allowed URL(s) from {SitemapUrl}.",
                allowed.Count,
                sitemapUrl);
        }

        if (allowed.Count > GeekCrawlerCaps.MaxSitemapUrlsPerOrigin)
        {
            _logger.LogWarning(
                "Sitemap for {Origin} yielded {Count} URLs; truncating to {Cap}.",
                origin,
                allowed.Count,
                GeekCrawlerCaps.MaxSitemapUrlsPerOrigin);
            allowed = allowed.Take(GeekCrawlerCaps.MaxSitemapUrlsPerOrigin).ToList();
        }

        return allowed;
    }

    private async Task<List<string>> ParseSitemapUrlsAsync(string xml, Uri rootUri, CancellationToken ct)
    {
        var urls = new List<string>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse sitemap XML for {Host}", rootUri.Host);
            return urls;
        }

        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var sitemapLocs = doc.Descendants(ns + "sitemap")
            .Select(node => node.Element(ns + "loc")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        if (sitemapLocs.Count > 0)
        {
            foreach (var childSitemap in sitemapLocs)
            {
                try
                {
                    using var response = await _http.GetAsync(childSitemap, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        continue;
                    var childXml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    urls.AddRange(ParseUrlLocs(childXml, rootUri));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to fetch child sitemap {ChildSitemap}", childSitemap);
                }
            }

            return urls;
        }

        urls.AddRange(ParseUrlLocs(xml, rootUri));
        return urls;
    }

    private static List<string> ParseUrlLocs(string xml, Uri rootUri)
    {
        var urls = new List<string>();
        foreach (Match match in Regex.Matches(xml, @"<loc>\s*(.*?)\s*</loc>", RegexOptions.IgnoreCase))
        {
            var loc = match.Groups[1].Value.Trim();
            if (!Uri.TryCreate(loc, UriKind.Absolute, out var absolute))
                continue;

            if (!absolute.Host.Equals(rootUri.Host, StringComparison.OrdinalIgnoreCase))
                continue;

            urls.Add(absolute.AbsoluteUri);
        }

        return urls;
    }
}
