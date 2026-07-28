using HtmlAgilityPack;

namespace ImportBlogContent;

public static class LinkExtractor
{
    public static IEnumerable<string> ExtractSameHostLinks(string html, string baseUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var baseUri = new Uri(baseUrl);

        foreach (var node in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#'))
                continue;

            if (!Uri.TryCreate(baseUri, href, out var absolute))
                continue;

            if (!string.Equals(absolute.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                continue;

            yield return absolute.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }
    }
}
