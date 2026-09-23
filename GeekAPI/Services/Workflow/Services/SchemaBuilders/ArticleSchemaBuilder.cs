using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

public interface IArticleSchemaBuilder
{
    /// <summary>
    /// Builds the schema.org Article JSON+LD for a pillar page, linking the companion blog post and
    /// any applications the page names.
    /// </summary>
    string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null);
}

public class ArticleSchemaBuilder : IArticleSchemaBuilder
{
    private readonly ISoftwareApplicationSchemaBuilder _softwareApplicationSchemaBuilder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public ArticleSchemaBuilder(ISoftwareApplicationSchemaBuilder softwareApplicationSchemaBuilder)
    {
        _softwareApplicationSchemaBuilder = softwareApplicationSchemaBuilder;
    }

    public string Build(
        ContentMetadata metadata,
        string relatedBlogPostUrl,
        IReadOnlyList<SoftwareApplicationDescriptor>? softwareApplications = null)
    {
        var articleNode = BuildArticleNode(metadata, relatedBlogPostUrl);
        var apps = softwareApplications ?? [];
        var softwareNodes = apps.Count > 0
            ? _softwareApplicationSchemaBuilder.BuildNodes(apps)
            : [];
        var faqNode = BuildFaqPage(metadata);

        if (softwareNodes.Count == 0 && faqNode is null)
        {
            return JsonSerializer.Serialize(articleNode, JsonOptions);
        }

        // The graph said nothing about how its nodes relate: an article and four applications side
        // by side, no @id on any of them, nothing connecting one to another. A reader of that
        // markup learns the page contains an article and, separately, that four products exist.
        // "mentions" by @id is the statement the page was actually making (Jeff, 2026-09-23).
        var mentioned = apps
            .Select(_softwareApplicationSchemaBuilder.NodeId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new Dictionary<string, object?> { ["@id"] = id })
            .ToList();
        if (mentioned.Count > 0)
        {
            articleNode["mentions"] = mentioned;
        }

        var graphNodes = new List<Dictionary<string, object?>>([articleNode, ..softwareNodes]);
        if (faqNode is not null)
        {
            faqNode["@id"] = $"{metadata.CanonicalUrl}#faq";
            articleNode["hasPart"] = new Dictionary<string, object?> { ["@id"] = faqNode["@id"] };
            graphNodes.Add(faqNode);
        }

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = graphNodes
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    private static Dictionary<string, object?> BuildArticleNode(ContentMetadata metadata, string relatedBlogPostUrl)
    {
        var node = new Dictionary<string, object?>
        {
            // TechArticle is schema.org's type for technical documentation -- how-to tasks,
            // step-by-step procedures, troubleshooting, specifications. A commercial pillar page
            // written for buyers is none of those, and it was published as one on every page
            // (Jeff, 2026-09-23, on being told the type was a judgement call rather than an error:
            // "Either it is wrong or it is not?" -- it is wrong).
            //
            // proficiencyLevel went with it. It is defined only on TechArticle, so it is invalid
            // on this type, and it was a hardcoded "Beginner" on every page regardless -- an
            // assertion with nothing behind it even where the type had allowed it.
            ["@type"] = "Article",
            // An @id so other nodes in the graph can point at this one, and so this one can point
            // back. Without it every node is anonymous and the graph carries no relationships.
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

        // Our own companion post, when there is one.
        //
        // This was "citation", which means a work this page cites -- an external source it drew on.
        // Our own blog is not that; it is a sibling page in the same cluster, and saying we cite it
        // both misstates the relationship and quietly claims external corroboration we do not have.
        // relatedLink says what is true (Jeff, 2026-09-23). Citation policy here is unchanged and
        // deliberate: the goal is content others cite.
        //
        // Still guarded. Emitted unconditionally it produced a link pointing nowhere, and callers
        // pass an empty string legitimately -- the orchestrator when RelatedArticleUrl is unset,
        // and the Create path always, since a pillar there has no companion blog beside it.
        if (!string.IsNullOrWhiteSpace(relatedBlogPostUrl))
        {
            node["relatedLink"] = relatedBlogPostUrl;
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
