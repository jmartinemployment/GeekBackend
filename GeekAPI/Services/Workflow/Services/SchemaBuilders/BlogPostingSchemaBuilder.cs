using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

public interface IBlogPostingSchemaBuilder
{
    /// <summary>Builds a schema.org BlogPosting JSON+LD document that cites the source TechnicalArticle.</summary>
    string Build(ContentMetadata metadata, string relatedArticleUrl);
}

public class BlogPostingSchemaBuilder : IBlogPostingSchemaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public string Build(ContentMetadata metadata, string relatedArticleUrl)
    {
        var schema = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["@id"] = $"{metadata.CanonicalUrl}#article",
            ["headline"] = metadata.Headline,
            ["description"] = metadata.Description,
            ["image"] = new[] { metadata.MainImageUrl },
            ["author"] = SoftwareApplicationSchemaBuilder.BuildAuthor(metadata),
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
        };

        // Cross-link back to the pillar when one exists. Was "citation" -- see
        // TechnicalArticleSchemaBuilder: our own pillar is a sibling in the same cluster, not a
        // work this post cites.
        if (!string.IsNullOrWhiteSpace(relatedArticleUrl))
        {
            schema["relatedLink"] = relatedArticleUrl;
        }

        var faqNode = BuildFaqPage(metadata);
        if (faqNode is null)
        {
            return JsonSerializer.Serialize(schema, JsonOptions);
        }

        faqNode["@id"] = $"{metadata.CanonicalUrl}#faq";
        schema["hasPart"] = new Dictionary<string, object?> { ["@id"] = faqNode["@id"] };

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new List<Dictionary<string, object?>> { schema, faqNode }
        };
        schema.Remove("@context");

        return JsonSerializer.Serialize(graph, JsonOptions);
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
