using System.Text.Json;
using System.Text.Json.Serialization;
using GeekAPI.Services.Workflow.DTOs;

namespace GeekAPI.Services.Workflow.Services.SchemaBuilders;

/// <summary>
/// One application the markup names.
/// </summary>
/// <param name="Url">
/// The application's OWN home -- the vendor's domain. This is what schema.org means by
/// <c>url</c> on a SoftwareApplication: the canonical location of the thing itself.
/// </param>
/// <param name="PageUrl">
/// Our page about it, which is a different fact and belongs in <c>mainEntityOfPage</c>.
///
/// Both used to go in <paramref name="Url"/>, and what went in was ours -- so published markup
/// asserted that Tipalti, Medius, Basware and Rillion are each located at geekatyourspot.com
/// (Jeff, 2026-09-23, reading the live document). A false claim about four real companies, two of
/// them partners.
/// </param>
public sealed record SoftwareApplicationDescriptor(
    string Name,
    string? Description,
    string? Url = null,
    string? PageUrl = null);

public interface ISoftwareApplicationSchemaBuilder
{
    IReadOnlyList<Dictionary<string, object?>> BuildNodes(IReadOnlyList<SoftwareApplicationDescriptor> applications);

    /// <summary>
    /// The <c>@id</c> a node will carry, so another node in the same graph can reference it. Null
    /// when the application has neither its own URL nor a page of ours -- an unidentified node
    /// cannot be referenced, and inventing an id for it would be a link to nothing.
    /// </summary>
    string? NodeId(SoftwareApplicationDescriptor application);
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
        // node["url"] is the product's own home, set by BuildNode. It used to be overwritten with
        // metadata.CanonicalUrl here -- our page -- directly beside the subjectOf below that was
        // already saying the same thing correctly.
        node["image"] = new[] { metadata.MainImageUrl };
        node["author"] = BuildAuthor(metadata);
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
            // Matches the pillar's own @type, corrected from TechArticle 2026-09-23. A subjectOf
                // naming a type the target does not have is a dangling reference.
                ["@type"] = "Article",
            ["@id"] = pillarArticleUrl
        };

        var faqNode = BuildFaqPage(metadata);
        if (faqNode is null)
        {
            return JsonSerializer.Serialize(node, JsonOptions);
        }

        node.Remove("@context");
        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new List<Dictionary<string, object?>> { node, faqNode }
        };

        return JsonSerializer.Serialize(graph, JsonOptions);
    }

    public string? NodeId(SoftwareApplicationDescriptor application) =>
        !string.IsNullOrWhiteSpace(application.Url) ? application.Url!.Trim()
        : !string.IsNullOrWhiteSpace(application.PageUrl) ? $"{application.PageUrl!.Trim()}#application"
        : null;

    private Dictionary<string, object?> BuildNode(SoftwareApplicationDescriptor application)
    {
        var node = new Dictionary<string, object?>
        {
            ["@type"] = "SoftwareApplication",
            ["name"] = application.Name.Trim(),
            ["applicationCategory"] = "BusinessApplication",
            ["operatingSystem"] = "Web"
        };

        var id = NodeId(application);
        if (id is not null)
        {
            node["@id"] = id;
        }

        if (!string.IsNullOrWhiteSpace(application.Description))
        {
            node["description"] = application.Description.Trim();
        }

        // The application's own home. Omitted rather than guessed: a SoftwareApplication with no
        // url is incomplete, one whose url is our page is wrong.
        if (!string.IsNullOrWhiteSpace(application.Url))
        {
            node["url"] = application.Url;
        }

        // Our page about it -- a separate fact from where the product lives.
        if (!string.IsNullOrWhiteSpace(application.PageUrl))
        {
            node["mainEntityOfPage"] = new Dictionary<string, object?>
            {
                ["@type"] = "WebPage",
                ["@id"] = application.PageUrl
            };
        }

        return node;
    }

    /// <summary>
    /// The author node. Organization, not Person: the configured author is the editorial team, and
    /// typing a team as a Person sends a search engine looking for a human of that name and finding
    /// nobody, so the authorship signal resolves to no entity at all (Jeff, 2026-09-23:
    /// "organization").
    /// </summary>
    internal static Dictionary<string, object?> BuildAuthor(ContentMetadata metadata) =>
        new()
        {
            ["@type"] = "Organization",
            ["name"] = metadata.AuthorName,
        };

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
