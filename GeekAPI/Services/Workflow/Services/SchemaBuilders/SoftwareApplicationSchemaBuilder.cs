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
        node["author"] = SchemaNodes.BuildAuthor(metadata);
        node["publisher"] = SchemaNodes.BuildPublisher(metadata);
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

        var faqNode = SchemaNodes.BuildFaqPage(metadata);
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
}
