using System.Net;
using HtmlAgilityPack;

namespace GeekAPI.Services.GeekCrawler;

public sealed record GeekCrawlerExtractedLink(string LinkUrl, bool IsSameOrigin);

/// <summary>Extract every HTTP(S) anchor href — save all; follow same-site only.</summary>
public static class GeekCrawlerLinkExtractor
{
    public static IReadOnlyList<GeekCrawlerExtractedLink> ExtractAllLinks(string html, string pageUrl, string crawlOrigin)
    {
        if (string.IsNullOrWhiteSpace(html)
            || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri)
            || !Uri.TryCreate(crawlOrigin, UriKind.Absolute, out var originUri))
            return [];

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var nodes = doc.DocumentNode.SelectNodes(".//a[@href]");
        if (nodes is null) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<GeekCrawlerExtractedLink>();
        foreach (var anchor in nodes)
        {
            var href = WebUtility.HtmlDecode(anchor.GetAttributeValue("href", "")).Trim();
            if (!TryResolveHttpLink(href, pageUri, out var absolute))
                continue;
            if (!seen.Add(absolute)) continue;

            var sameOrigin = Uri.TryCreate(absolute, UriKind.Absolute, out var linkUri)
                             && string.Equals(linkUri.Host, originUri.Host, StringComparison.OrdinalIgnoreCase)
                             && linkUri.Scheme == originUri.Scheme;
            links.Add(new GeekCrawlerExtractedLink(absolute, sameOrigin));
        }

        return links;
    }

    public static IReadOnlyList<string> SameOriginLinksForQueue(
        IReadOnlyList<GeekCrawlerExtractedLink> links) =>
        links.Where(l => l.IsSameOrigin).Select(l => l.LinkUrl).ToList();

    private static bool TryResolveHttpLink(string href, Uri pageUri, out string absolute)
    {
        absolute = string.Empty;
        if (string.IsNullOrWhiteSpace(href)) return false;
        if (href.StartsWith('#')
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (!Uri.TryCreate(pageUri, href, out var resolved))
                return false;
            if (resolved.Scheme is not ("http" or "https"))
                return false;

            var builder = new UriBuilder(resolved) { Fragment = "" };
            absolute = builder.Uri.AbsoluteUri;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
