using System.Text.Json;
using GeekApplication.Models.Blog;

namespace GeekApplication.Blog;

/// <summary>
/// Reads primary article metadata from stored schema.org JSON (flat or @graph).
/// </summary>
public static class SchemaMetadataParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static ArticleMetadata? Parse(string postType, string schemaMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(schemaMetadataJson) || schemaMetadataJson == "{}")
            return null;

        try
        {
            using var doc = JsonDocument.Parse(schemaMetadataJson);
            var node = ResolvePrimaryNode(doc.RootElement, postType);
            if (node is null)
                return null;

            return DeserializeNode(postType, node.Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ResolvePrimaryNode(JsonElement root, string postType)
    {
        if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in graph.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (TypeMatchesPostType(item, postType))
                    return item;
            }

            if (root.ValueKind == JsonValueKind.Object && TypeMatchesPostType(root, postType))
                return root;

            return null;
        }

        if (root.ValueKind == JsonValueKind.Object)
            return root;

        return null;
    }

    private static bool TypeMatchesPostType(JsonElement node, string postType) =>
        postType switch
        {
            "TechnicalArticle" => HasType(node, "TechnicalArticle", "TechArticle"),
            "BlogPosting" => HasType(node, "BlogPosting"),
            "NewsArticle" => HasType(node, "NewsArticle"),
            _ => false,
        };

    private static bool HasType(JsonElement node, params string[] types)
    {
        if (!node.TryGetProperty("@type", out var typeEl))
            return false;

        return typeEl.ValueKind switch
        {
            JsonValueKind.String => types.Contains(typeEl.GetString(), StringComparer.Ordinal),
            JsonValueKind.Array => typeEl.EnumerateArray().Any(
                item => item.ValueKind == JsonValueKind.String
                    && types.Contains(item.GetString(), StringComparer.Ordinal)),
            _ => false,
        };
    }

    private static ArticleMetadata? DeserializeNode(string postType, JsonElement node) =>
        postType switch
        {
            "TechnicalArticle" => JsonSerializer.Deserialize<TechnicalArticleMetadata>(node, JsonOptions),
            "BlogPosting" => JsonSerializer.Deserialize<BlogPostingMetadata>(node, JsonOptions),
            "NewsArticle" => JsonSerializer.Deserialize<NewsArticleMetadata>(node, JsonOptions),
            _ => null,
        };
}
