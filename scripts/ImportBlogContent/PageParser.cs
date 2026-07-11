using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace ImportBlogContent;

public sealed record ParsedPage(
    string Title,
    string BodyHtml,
    string? MetaDescription,
    DateTimeOffset? PublishedAt,
    string SchemaMetadataJson);

public static partial class PageParser
{
    public static ParsedPage Parse(string html, string postType, string canonicalUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "Untitled";
        title = TitleRegex().Replace(title, string.Empty).Trim();

        var metaDescription = doc.DocumentNode
            .SelectSingleNode("//meta[@name='description']")?
            .GetAttributeValue("content", null);

        var jsonLd = ExtractJsonLd(doc, postType);
        var publishedAt = ExtractPublishedDate(doc, jsonLd);
        var body = ExtractBodyHtml(doc);

        var schema = jsonLd ?? BuildFallbackSchema(postType, title, metaDescription, canonicalUrl, publishedAt);

        return new ParsedPage(title, body, metaDescription, publishedAt, schema);
    }

    private static string? ExtractJsonLd(HtmlDocument doc, string postType)
    {
        foreach (var node in doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']") ?? Enumerable.Empty<HtmlNode>())
        {
            var raw = node.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;

            try
            {
                using var json = JsonDocument.Parse(raw);
                var type = json.RootElement.TryGetProperty("@type", out var t) ? t.GetString() : null;
                if (string.Equals(type, postType, StringComparison.Ordinal))
                    return raw;
            }
            catch
            {
                // try next block
            }
        }

        return null;
    }

    private static DateTimeOffset? ExtractPublishedDate(HtmlDocument doc, string? jsonLd)
    {
        if (jsonLd is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(jsonLd);
                if (json.RootElement.TryGetProperty("datePublished", out var dp)
                    && DateTimeOffset.TryParse(dp.GetString(), out var parsed))
                    return parsed;
            }
            catch { /* ignore */ }
        }

        var timeNode = doc.DocumentNode.SelectSingleNode("//time[@datetime]");
        if (timeNode is not null
            && DateTimeOffset.TryParse(timeNode.GetAttributeValue("datetime", string.Empty), out var fromTime))
            return fromTime;

        return null;
    }

    private static string ExtractBodyHtml(HtmlDocument doc)
    {
        var article = doc.DocumentNode.SelectSingleNode("//article")
            ?? doc.DocumentNode.SelectSingleNode("//main")
            ?? doc.DocumentNode.SelectSingleNode("//body");

        if (article is null) return string.Empty;

        var clone = article.Clone();
        RemoveNoise(clone);
        return clone.InnerHtml.Trim();
    }

    private static void RemoveNoise(HtmlNode node)
    {
        var removeSelectors = new[]
        {
            "//nav", "//header", "//footer", "//script", "//style", "//noscript",
            "//*[contains(@class,'breadcrumb')]", "//*[contains(@class,'related')]"
        };

        foreach (var selector in removeSelectors)
        {
            foreach (var n in node.SelectNodes(selector)?.ToList() ?? [])
                n.Remove();
        }
    }

    private static string BuildFallbackSchema(
        string postType,
        string title,
        string? description,
        string canonicalUrl,
        DateTimeOffset? publishedAt)
    {
        var payload = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = postType,
            ["headline"] = title,
            ["description"] = description,
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = canonicalUrl
            }
        };

        if (publishedAt is not null)
            payload["datePublished"] = publishedAt.Value.ToString("O");

        return JsonSerializer.Serialize(payload);
    }

    [GeneratedRegex(@"\s*\|\s*Geek.*$", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();
}
