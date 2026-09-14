using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.ContentCreatorV2.Partner;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using GeekApplication.Models.ContentCreator;

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
        SoftwareApplicationDescriptor about,
        GccPartnerExtractionDocument? partnerExtraction = null,
        IReadOnlyList<GccQuoteablePage>? partnerPages = null)
    {
        Dictionary<string, object?> node;
        var libraryNode = partnerExtraction is not null && partnerPages is { Count: > 0 }
            ? GccV2PartnerSoftwareApplicationJsonLd.TryBuild(partnerExtraction, partnerPages)
            : null;

        if (libraryNode is { Count: > 0 })
        {
            GccV2PartnerSoftwareApplicationJsonLd.EnsureShipReadyOrThrow(libraryNode, partnerExtraction!);
            node = new Dictionary<string, object?>(libraryNode)
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "SoftwareApplication",
            };
            if (!string.IsNullOrWhiteSpace(about.Name))
                node["name"] = about.Name.Trim();
            if (!string.IsNullOrWhiteSpace(about.Url))
                node["url"] = about.Url;
        }
        else
        {
            node = BuildNode(about);
            node["@context"] = "https://schema.org";
        }

        node["headline"] = metadata.Headline;
        if (!node.TryGetValue("description", out var desc)
            || desc is null
            || string.IsNullOrWhiteSpace(desc.ToString()))
        {
            node["description"] = metadata.Description;
        }

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
