using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

public sealed record SoftwareApplicationDescriptor(string Name, string? Description, string? Url = null);

public interface ISoftwareApplicationSchemaBuilder
{
    IReadOnlyList<Dictionary<string, object?>> BuildNodes(IReadOnlyList<SoftwareApplicationDescriptor> applications);
    string BuildGraph(IReadOnlyList<SoftwareApplicationDescriptor> applications);

    /// <summary>Full JSON+LD for a standalone tool overview page (primary node: SoftwareApplication).</summary>
    string BuildToolPage(ContentMetadata metadata, string pillarArticleUrl, SoftwareApplicationDescriptor about);
}

public class SoftwareApplicationSchemaBuilder : ISoftwareApplicationSchemaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public IReadOnlyList<Dictionary<string, object?>> BuildNodes(IReadOnlyList<SoftwareApplicationDescriptor> applications)
    {
        return applications
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .Select(BuildNode)
            .ToList();
    }

    public string BuildGraph(IReadOnlyList<SoftwareApplicationDescriptor> applications)
    {
        var nodes = BuildNodes(applications);
        if (nodes.Count == 0)
        {
            return string.Empty;
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = nodes
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    public string BuildToolPage(ContentMetadata metadata, string pillarArticleUrl, SoftwareApplicationDescriptor about)
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
            ["name"] = metadata.AuthorName
        };
        node["publisher"] = BuildPublisher(metadata);
        node["datePublished"] = metadata.DatePublishedUtc.ToString("O");
        node["dateModified"] = metadata.DateModifiedUtc.ToString("O");
        node["mainEntityOfPage"] = new Dictionary<string, object?>
        {
            ["@type"] = "WebPage",
            ["@id"] = metadata.CanonicalUrl
        };
        node["keywords"] = string.Join(", ", metadata.Keywords);
        node["subjectOf"] = new Dictionary<string, object?>
        {
            ["@type"] = "TechArticle",
            ["@id"] = pillarArticleUrl
        };

        return JsonSerializer.Serialize(node, JsonOptions);
    }

    private static Dictionary<string, object?> BuildNode(SoftwareApplicationDescriptor application)
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = application.Name.Trim(),
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web"
        };

        if (!string.IsNullOrWhiteSpace(application.Description))
        {
            node["description"] = application.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(application.Url))
        {
            node["url"] = application.Url;
        }

        return node;
    }

    /// <summary>
    /// The publisher node. Its <c>@type</c> mirrors what the client's own markup declares — never
    /// inferred — and <c>areaServed</c> appears only when the site actually declares service areas.
    /// An empty array would assert "serves nowhere", so absent means absent.
    /// </summary>
    private static Dictionary<string, object?> BuildPublisher(ContentMetadata metadata)
    {
        var publisher = new Dictionary<string, object?>
        {
            ["@type"] = string.IsNullOrWhiteSpace(metadata.PublisherType)
                ? "Organization"
                : metadata.PublisherType,
            ["name"] = metadata.PublisherName,
            ["logo"] = new Dictionary<string, object?>
            {
                ["@type"] = "ImageObject",
                ["url"] = metadata.PublisherLogoUrl
            }
        };

        var areas = metadata.AreaServed?
            .Where(area => !string.IsNullOrWhiteSpace(area))
            .Select(area => area.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (areas is { Count: > 0 })
        {
            publisher["areaServed"] = areas;
        }

        return publisher;
    }

    /// <summary>
    /// The <c>FAQPage</c> node for questions the page actually contains.
    /// </summary>
    /// <remarks>
    /// Google restricted FAQ <i>rich results</i> to authoritative government and health sites in
    /// August 2023, so this does not render as a SERP feature for most sites. The markup is emitted
    /// for machine consumption — answer engines and entity understanding — where
    /// question-to-answer adjacency is the point. Never emit an entry the page does not answer.
    /// </remarks>
    private static Dictionary<string, object?>? BuildFaqPage(ContentMetadata metadata)
    {
        var entries = metadata.Faq?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Question)
                            && !string.IsNullOrWhiteSpace(entry.Answer))
            .ToList();
        if (entries is not { Count: > 0 })
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["@type"] = "FAQPage",
            ["mainEntity"] = entries.Select(entry => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = entry.Question.Trim(),
                ["acceptedAnswer"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Answer",
                    ["text"] = entry.Answer.Trim()
                }
            }).ToList()
        };
    }

}
