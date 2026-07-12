using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GeekApplication.Blog;

/// <summary>
/// Use-case posts store a short display title in geek_blog.title.
/// SEO suffixes like "| Sales AI Use Cases" belong in page metadata, not the title column.
/// </summary>
public static partial class UseCasePostNormalizer
{
    [GeneratedRegex(@"\s*\|\s*.+ AI Use Cases?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SeoTitleSuffixRegex();

    public static bool IsUseCaseSlug(string slug) =>
        slug.StartsWith("use-cases/", StringComparison.OrdinalIgnoreCase);

    public static string ToDisplayTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return title;

        var decoded = DecodeHtmlEntities(title.Trim());
        return SeoTitleSuffixRegex().Replace(decoded, string.Empty).Trim();
    }

    public static string PatchSchemaHeadlines(string schemaMetadataJson, string displayTitle)
    {
        if (string.IsNullOrWhiteSpace(schemaMetadataJson) || schemaMetadataJson == "{}")
            return schemaMetadataJson;

        try
        {
            var node = JsonNode.Parse(schemaMetadataJson);
            if (node is not JsonObject root)
                return schemaMetadataJson;

            if (root["@graph"] is JsonArray graph)
            {
                foreach (var item in graph)
                    PatchArticleNode(item as JsonObject, displayTitle);
            }
            else
            {
                PatchArticleNode(root, displayTitle);
            }

            return root.ToJsonString();
        }
        catch (JsonException)
        {
            return schemaMetadataJson;
        }
    }

    public static bool NeedsNormalization(string slug, string title, string schemaMetadataJson)
    {
        if (!IsUseCaseSlug(slug))
            return false;

        var displayTitle = ToDisplayTitle(title);
        if (!string.Equals(title.Trim(), displayTitle, StringComparison.Ordinal))
            return true;

        return SchemaHeadlineDiffers(schemaMetadataJson, displayTitle);
    }

    public static (string Title, string SchemaMetadataJson) Normalize(string slug, string title, string schemaMetadataJson)
    {
        if (!IsUseCaseSlug(slug))
            return (title, schemaMetadataJson);

        var displayTitle = ToDisplayTitle(title);
        var schema = PatchSchemaHeadlines(schemaMetadataJson, displayTitle);
        return (displayTitle, schema);
    }

    private static bool SchemaHeadlineDiffers(string schemaMetadataJson, string displayTitle)
    {
        if (string.IsNullOrWhiteSpace(schemaMetadataJson))
            return false;

        try
        {
            var node = JsonNode.Parse(schemaMetadataJson);
            if (node is not JsonObject root)
                return false;

            if (root["@graph"] is JsonArray graph)
            {
                foreach (var item in graph)
                {
                    if (item is JsonObject obj && ArticleHeadlineDiffers(obj, displayTitle))
                        return true;
                }

                return false;
            }

            return ArticleHeadlineDiffers(root, displayTitle);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void PatchArticleNode(JsonObject? obj, string displayTitle)
    {
        if (obj is null || !IsArticleNode(obj))
            return;

        obj["headline"] = displayTitle;
    }

    private static bool ArticleHeadlineDiffers(JsonObject obj, string displayTitle)
    {
        if (!IsArticleNode(obj))
            return false;

        var headline = obj["headline"]?.GetValue<string>();
        return !string.Equals(ToDisplayTitle(headline ?? string.Empty), displayTitle, StringComparison.Ordinal);
    }

    private static bool IsArticleNode(JsonObject obj)
    {
        var type = obj["@type"]?.GetValue<string>();
        return type is "TechnicalArticle" or "TechArticle" or "BlogPosting";
    }

    private static string DecodeHtmlEntities(string value) =>
        value
            .Replace("&amp;", "&", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&#39;", "'", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal);
}
