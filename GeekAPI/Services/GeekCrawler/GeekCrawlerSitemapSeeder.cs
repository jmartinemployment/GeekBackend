using System.Net;
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
        string? xml;
        try
        {
            xml = await GetStringWithValidatedRedirectsAsync(sitemapUrl.AbsoluteUri, ct)
                .ConfigureAwait(false);
            if (xml is null)
            {
                _logger.LogDebug("No sitemap at {SitemapUrl}.", sitemapUrl);
                return [];
            }
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
                    var childXml = await GetStringWithValidatedRedirectsAsync(childSitemap!, ct)
                        .ConfigureAwait(false);
                    if (childXml is null)
                        continue;
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

    /// <summary>
    /// Manual redirect follow with SSRF re-validation at every hop.
    /// HttpClient has AllowAutoRedirect=false for this client.
    /// </summary>
    private async Task<string?> GetStringWithValidatedRedirectsAsync(string url, CancellationToken ct)
    {
        var current = url;
        for (var hop = 0; hop <= GeekCrawlerCaps.MaxRedirectsPerNavigation; hop++)
        {
            if (!GeekCrawlerSeedNormalizer.TryValidateResolvedCrawlUrl(current, out var reject))
            {
                _logger.LogWarning(
                    "Rejected sitemap fetch URL {Url}: {Reason}",
                    current,
                    reject);
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct)
                .ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                    return null;

                current = location.IsAbsoluteUri
                    ? location.AbsoluteUri
                    : new Uri(new Uri(current), location).AbsoluteUri;
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Sitemap fetch {Url} returned HTTP {Status}.",
                    current,
                    (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        _logger.LogWarning(
            "Sitemap fetch exceeded MaxRedirectsPerNavigation={Cap} starting from {Url}.",
            GeekCrawlerCaps.MaxRedirectsPerNavigation,
            url);
        return null;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect
            or HttpStatusCode.MultipleChoices;

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
