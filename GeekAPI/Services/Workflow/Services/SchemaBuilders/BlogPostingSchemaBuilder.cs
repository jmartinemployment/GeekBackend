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
            ["author"] = SchemaNodes.BuildAuthor(metadata),
            ["publisher"] = SchemaNodes.BuildPublisher(metadata),
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
        // ArticleSchemaBuilder: our own pillar is a sibling in the same cluster, not a
        // work this post cites.
        if (!string.IsNullOrWhiteSpace(relatedArticleUrl))
        {
            schema["relatedLink"] = relatedArticleUrl;
        }

        var faqNode = SchemaNodes.BuildFaqPage(metadata);
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
}
