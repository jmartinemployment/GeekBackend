using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.Rag;
using GeekAPI.Services.ContentCreatorV2.Write;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.SchemaBuilders;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

/// <summary>Shared JSON-LD builder for export, CMS publish, and job ResultJson persistence.</summary>
public sealed class GccV2JsonLdBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly CompanyProfileOptions _company;
    private readonly IArticleSchemaBuilder _articleSchema;
    private readonly IBlogPostingSchemaBuilder _blogSchema;
    private readonly ISoftwareApplicationSchemaBuilder _toolSchema;

    public GccV2JsonLdBuilder(
        IOptions<CompanyProfileOptions> company,
        IArticleSchemaBuilder articleSchema,
        IBlogPostingSchemaBuilder blogSchema,
        ISoftwareApplicationSchemaBuilder toolSchema)
    {
        _company = company.Value;
        _articleSchema = articleSchema;
        _blogSchema = blogSchema;
        _toolSchema = toolSchema;
    }

    public string? BuildForJob(
        string contentType,
        string? toolPageKind,
        string title,
        string metaDescription,
        ContentDocument document,
        DateTimeOffset completedAt,
        IReadOnlyList<string> keywords,
        string? pillarArticleUrl,
        string? slugOverride = null,
        IReadOnlyList<RagCitationDto>? citations = null)
    {
        var slug = string.IsNullOrWhiteSpace(slugOverride) ? SlugHelper.Slugify(title) : slugOverride;
        var canonicalUrl = CanonicalUrlFor(contentType, slug, toolPageKind);
        return Build(contentType, toolPageKind, title, metaDescription, canonicalUrl, document, completedAt, keywords, pillarArticleUrl, citations);
    }

    public string? Build(
        string contentType,
        string? toolPageKind,
        string title,
        string metaDescription,
        string? canonicalUrl,
        ContentDocument document,
        DateTimeOffset completedAt,
        IReadOnlyList<string> keywords,
        string? pillarArticleUrl,
        IReadOnlyList<RagCitationDto>? citations = null)
    {
        if (string.IsNullOrWhiteSpace(canonicalUrl)) return null;

        var metadata = new ContentMetadata(
            title,
            metaDescription,
            _company.AuthorName,
            _company.PublisherName,
            _company.PublisherLogoUrl,
            canonicalUrl,
            _company.PublisherLogoUrl,
            completedAt.UtcDateTime,
            completedAt.UtcDateTime,
            keywords.ToList(),
            ContentDocumentText.CountWords(document));

        var normalized = GccV2LongFormTypes.Normalize(contentType);
        var primary = normalized switch
        {
            GccV2LongFormTypes.Pillar or GccV2LongFormTypes.TechArticle or GccV2LongFormTypes.Comparison
                or GccV2LongFormTypes.CaseStudy or GccV2LongFormTypes.Alternatives
                or GccV2LongFormTypes.Service or GccV2LongFormTypes.Local
                or GccV2LongFormTypes.Whitepaper => _articleSchema.Build(metadata, canonicalUrl),
            GccV2LongFormTypes.Blog or GccV2LongFormTypes.Guide or GccV2LongFormTypes.Listicle =>
                _blogSchema.Build(metadata, relatedArticleUrl: string.Empty),
            GccV2LongFormTypes.Tool when string.Equals(toolPageKind, "overview", StringComparison.OrdinalIgnoreCase) =>
                _articleSchema.Build(metadata, pillarArticleUrl ?? canonicalUrl),
            GccV2LongFormTypes.Tool => _toolSchema.BuildToolPage(
                metadata,
                pillarArticleUrl: pillarArticleUrl ?? string.Empty,
                // canonicalUrl is our page, so PageUrl. It was passed as the product's own url.
                new SoftwareApplicationDescriptor(title, metaDescription, Url: null, PageUrl: canonicalUrl)),
            _ => null,
        };

        if (primary is null) return null;
        var withFaq = MergeFaqPage(primary, ExtractFaqPairs(document));
        return citations is { Count: > 0 } ? MergeCitations(withFaq, citations) : withFaq;
    }

    public string? CanonicalUrlFor(string contentType, string slug, string? toolPageKind)
    {
        var normalized = GccV2LongFormTypes.Normalize(contentType);
        if (GccV2LongFormTypes.IsLongForm(normalized) && normalized is not GccV2LongFormTypes.Tool)
            return CombineUrl(_company.ArticleBaseUrl, GccV2LongFormTypes.ExportFolder(normalized), slug);

        return normalized switch
        {
            GccV2LongFormTypes.Pillar => CombineUrl(_company.ArticleBaseUrl, "marketing", slug),
            GccV2LongFormTypes.Blog => CombineUrl(_company.BlogBaseUrl, "marketing", slug),
            GccV2LongFormTypes.Tool when string.Equals(toolPageKind, "overview", StringComparison.OrdinalIgnoreCase) =>
                $"{_company.ToolBaseUrl.TrimEnd('/')}/{slug}",
            GccV2LongFormTypes.Tool => CombineUrl(_company.ToolBaseUrl, "marketing", slug),
            _ => null,
        };
    }

    /// <summary>
    /// Extract FAQ Q/A pairs from People Also Ask / FAQ sections (child headings = questions).
    /// </summary>
    public static IReadOnlyList<(string Question, string Answer)> ExtractFaqPairs(ContentDocument document)
    {
        var pairs = new List<(string, string)>();
        foreach (var section in document.Sections)
        {
            if (!IsFaqSection(section)) continue;
            CollectFaqPairs(section, pairs);
        }

        return pairs;
    }

    /// <summary>
    /// Emit verified evidence as schema.org <c>citation</c> entries on the primary node, so published
    /// pages carry real external source attribution rather than internal cross-links alone.
    ///
    /// Only citations that are BOTH <see cref="RagCitationDto.Verified"/> == true AND rights-cleared
    /// (<c>sourceRights</c> ∈ {consented, licensed}, master-plan Appendix B) are emitted — unknown /
    /// prohibited / missing rights are dropped silently rather than published.
    ///
    /// v1's schema builders already set <c>citation</c> to the companion-piece cross-link
    /// (relatedBlogPostUrl / pillarArticleUrl). That is internal linking, not source attribution, so
    /// entries are APPENDED to any existing array rather than replacing it, and v1's builders are not
    /// modified: v1 is live production and off-limits.
    /// </summary>
    internal static string MergeCitations(
        string primaryJsonLd,
        IReadOnlyList<RagCitationDto> citations)
    {
        var emittable = citations
            .Where(c => c.Verified == true)
            .Where(c => !string.IsNullOrWhiteSpace(c.Url))
            .Where(c => IsRightsCleared(c.SourceRights))
            .GroupBy(c => c.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (emittable.Count == 0) return primaryJsonLd;

        try
        {
            var root = JsonNode.Parse(primaryJsonLd)?.AsObject();
            if (root is null) return primaryJsonLd;

            // Target the article/primary node: inside @graph it is the first non-FAQPage entry.
            var target = root;
            if (root["@graph"] is JsonArray graph)
            {
                var primaryNode = graph
                    .OfType<JsonObject>()
                    .FirstOrDefault(n => (string?)n["@type"] is not "FAQPage");
                if (primaryNode is null) return primaryJsonLd;
                target = primaryNode;
            }

            var citationArray = target["citation"] as JsonArray ?? [];
            // Preserve v1's existing companion cross-links, then append source attribution.
            var merged = new JsonArray();
            foreach (var existing in citationArray.ToList())
            {
                citationArray.Remove(existing);
                merged.Add(existing);
            }

            foreach (var citation in emittable)
            {
                var node = new JsonObject
                {
                    ["@type"] = "WebPage",
                    ["url"] = citation.Url,
                };
                if (!string.IsNullOrWhiteSpace(citation.Title))
                    node["name"] = citation.Title;
                if (!string.IsNullOrWhiteSpace(citation.Quote))
                    node["description"] = citation.Quote;
                merged.Add(node);
            }

            target["citation"] = merged;
            return root.ToJsonString(JsonOpts);
        }
        catch (JsonException)
        {
            return primaryJsonLd;
        }
    }

    /// <summary>Appendix B: only consented / licensed sources may be displayed.</summary>
    private static bool IsRightsCleared(string? sourceRights) =>
        string.Equals(sourceRights, "consented", StringComparison.OrdinalIgnoreCase)
        || string.Equals(sourceRights, "licensed", StringComparison.OrdinalIgnoreCase);

    internal static string MergeFaqPage(
        string primaryJsonLd,
        IReadOnlyList<(string Question, string Answer)> faqPairs)
    {
        if (faqPairs.Count == 0) return primaryJsonLd;

        var faqNode = new JsonObject
        {
            ["@type"] = "FAQPage",
            ["mainEntity"] = new JsonArray(
                faqPairs.Select(pair => (JsonNode)new JsonObject
                {
                    ["@type"] = "Question",
                    ["name"] = pair.Question,
                    ["acceptedAnswer"] = new JsonObject
                    {
                        ["@type"] = "Answer",
                        ["text"] = pair.Answer,
                    },
                }).ToArray()),
        };

        try
        {
            var root = JsonNode.Parse(primaryJsonLd)?.AsObject();
            if (root is null) return primaryJsonLd;

            if (root["@graph"] is JsonArray graph)
            {
                graph.Add(faqNode);
                return root.ToJsonString(JsonOpts);
            }

            // Single primary node → wrap with FAQ in @graph.
            root.Remove("@context");
            var wrapped = new JsonObject
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = new JsonArray(root, faqNode),
            };
            return wrapped.ToJsonString(JsonOpts);
        }
        catch (JsonException)
        {
            return primaryJsonLd;
        }
    }

    private static bool IsFaqSection(Section section) =>
        section.Heading.Contains("People Also Ask", StringComparison.OrdinalIgnoreCase)
        || section.Heading.Contains("FAQ", StringComparison.OrdinalIgnoreCase)
        || string.Equals(section.Tag, "faq", StringComparison.OrdinalIgnoreCase);

    private static void CollectFaqPairs(Section section, List<(string Question, string Answer)> pairs)
    {
        if (section.Children.Count > 0)
        {
            foreach (var child in section.Children)
            {
                var question = child.Heading.Trim();
                var answer = FlattenParagraphs(child).Trim();
                if (question.Length > 0 && answer.Length > 0)
                    pairs.Add((question, answer));
            }

            return;
        }

        // Flat FAQ: paragraphs that look like Q: / A: are uncommon; prefer question-shaped headings only.
        var body = FlattenParagraphs(section).Trim();
        if (section.Heading.TrimEnd().EndsWith('?') && body.Length > 0)
            pairs.Add((section.Heading.Trim(), body));
    }

    private static string FlattenParagraphs(Section section)
    {
        var parts = new List<string>();
        foreach (var paragraph in section.Paragraphs)
        {
            switch (paragraph)
            {
                case TextParagraph text:
                    parts.Add(string.Join(" ", text.Runs.Select(r => r.Text)));
                    break;
                case ListParagraph list:
                    parts.AddRange(list.Items.Select(item => string.Join(" ", item.Select(r => r.Text))));
                    break;
            }
        }

        foreach (var child in section.Children)
            parts.Add(FlattenParagraphs(child));

        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";
}
