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
    private static readonly Dictionary<string, string[]> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TechnicalArticle"] = ["TechnicalArticle", "TechArticle"],
        ["BlogPosting"] = ["BlogPosting"]
    };

    public static ParsedPage Parse(string html, string postType, string canonicalUrl)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "Untitled";
        title = TitleRegex().Replace(title, string.Empty).Trim();

        var metaDescription = doc.DocumentNode
            .SelectSingleNode("//meta[@name='description']")?
            .GetAttributeValue("content", null);

        var rawJsonLd = ExtractJsonLd(doc, postType);
        var publishedAt = ExtractPublishedDate(doc, rawJsonLd);
        var body = ExtractBodyHtml(doc);

        var schema = BuildImportSchema(rawJsonLd, postType, title, metaDescription, canonicalUrl, publishedAt);

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
                var match = FindMatchingEntity(json.RootElement, postType);
                if (match is not null)
                    return match;
            }
            catch
            {
                // try next block
            }
        }

        return null;
    }

    private static string? FindMatchingEntity(JsonElement root, string postType)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var match = FindMatchingEntity(item, postType);
                if (match is not null)
                    return match;
            }

            return null;
        }

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (MatchesPostType(root, postType))
            return root.GetRawText();

        if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && MatchesPostType(item, postType))
                    return item.GetRawText();
            }
        }

        return null;
    }

    private static bool MatchesPostType(JsonElement entity, string postType)
    {
        if (!entity.TryGetProperty("@type", out var typeElement))
            return false;

        var aliases = TypeAliases.GetValueOrDefault(postType) ?? [postType];

        if (typeElement.ValueKind == JsonValueKind.String)
            return aliases.Contains(typeElement.GetString(), StringComparer.OrdinalIgnoreCase);

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in typeElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String
                    && aliases.Contains(item.GetString(), StringComparer.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string BuildImportSchema(
        string? rawJsonLd,
        string postType,
        string title,
        string? description,
        string canonicalUrl,
        DateTimeOffset? publishedAt)
    {
        string? headline = null;
        string? metaDescription = description;

        if (rawJsonLd is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(rawJsonLd);
                var root = json.RootElement;

                if (root.TryGetProperty("headline", out var headlineElement))
                    headline = headlineElement.GetString();

                if (root.TryGetProperty("description", out var descriptionElement))
                    metaDescription = descriptionElement.GetString();

                if (publishedAt is null
                    && root.TryGetProperty("datePublished", out var datePublishedElement)
                    && DateTimeOffset.TryParse(datePublishedElement.GetString(), out var parsed))
                    publishedAt = parsed;
            }
            catch
            {
                // fall through to fallback schema
            }
        }

        return BuildFallbackSchema(
            postType,
            headline ?? title,
            metaDescription,
            canonicalUrl,
            publishedAt);
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
