using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

public interface ITechnicalArticleSchemaBuilder
{
    /// <summary>Builds a schema.org TechnicalArticle JSON+LD document that cites the companion blog post.</summary>
    string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null);
}

public class TechnicalArticleSchemaBuilder : ITechnicalArticleSchemaBuilder
{
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public TechnicalArticleSchemaBuilder(ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
    }

    public string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null)
    {
        var articleNode = BuildArticleNode(metadata, relatedBlogPostUrl);
        var softwareNodes = softwareApplications is { Count: > 0 }
            ? _softwareApplicationSchemaBuilder.BuildNodes(softwareApplications)
            : [];

        if (softwareNodes.Count == 0)
        {
            return JsonSerializer.Serialize(articleNode, JsonOptions);
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new List<Dictionary<string, object?>>([articleNode, ..softwareNodes])
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    private static Dictionary<string, object?> BuildArticleNode(ContentMetadata metadata, string relatedBlogPostUrl)
    {
        return new Dictionary<string, object?>
        {
            // "TechArticle" is the real schema.org type — "TechnicalArticle" doesn't exist there
            // (confirmed: schema.org/TechnicalArticle 404s; schema.org/TechArticle is real and is
            // the only type "proficiencyLevel" below is actually defined on).
            ["@type"] = "TechArticle",
            ["headline"] = metadata.Headline,
            ["description"] = metadata.Description,
            ["image"] = new[] { metadata.MainImageUrl },
            ["author"] = new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = metadata.AuthorName
            },
            ["publisher"] = BuildPublisher(metadata),
            ["datePublished"] = metadata.DatePublishedUtc.ToString("O"),
            ["dateModified"] = metadata.DateModifiedUtc.ToString("O"),
            ["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = metadata.CanonicalUrl
            },
            ["keywords"] = string.Join(", ", metadata.Keywords),
            ["wordCount"] = metadata.WordCount,
            ["proficiencyLevel"] = "Beginner",
            ["citation"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["@type"] = "BlogPosting",
                    ["url"] = relatedBlogPostUrl
                }
            }
        };
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
