using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;

namespace GeekAPI.Services.ContentCreatorV2.ToolPages;

/// <summary>Copied JSON-LD builder for standalone partner tool pages (v2-owned).</summary>
public static class GccV2ToolPageSchemaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string BuildToolPage(
        ContentMetadata metadata,
        string pillarArticleUrl,
        SoftwareApplicationDescriptor about)
    {
        var node = BuildNode(about);
        node["@context"] = "https://schema.org";
        node["headline"] = metadata.Headline;
        node["description"] = metadata.Description;
        node["url"] = metadata.CanonicalUrl;
        node["image"] = new[] { metadata.MainImageUrl };
        node["author"] = new Dictionary<string, object?>
        {
            ["@type"] = "Person",
            ["name"] = metadata.AuthorName,
        };
        node["publisher"] = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["name"] = metadata.PublisherName,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = metadata.PublisherLogoUrl,
            },
        };
        node["datePublished"] = metadata.DatePublishedUtc.ToString("O");
        node["dateModified"] = metadata.DateModifiedUtc.ToString("O");
        node["mainEntityOfPage"] = new Dictionary<string, object?>
        {
            ["@type"] = "WebPage",
            ["@id"] = metadata.CanonicalUrl,
        };
        node["keywords"] = string.Join(", ", metadata.Keywords);
        if (!string.IsNullOrWhiteSpace(pillarArticleUrl))
        {
            node["subjectOf"] = new Dictionary<string, object?>
            {
                ["@type"] = "TechArticle",
                ["@id"] = pillarArticleUrl,
            };
        }

        return JsonSerializer.Serialize(node, JsonOptions);
    }

    private static Dictionary<string, object?> BuildNode(SoftwareApplicationDescriptor application)
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = application.Name.Trim(),
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web",
        };

        if (!string.IsNullOrWhiteSpace(application.Description))
            node["description"] = application.Description.Trim();
        if (!string.IsNullOrWhiteSpace(application.Url))
            node["url"] = application.Url;

        return node;
    }
}
