using System.Text.Json;
using System.Text.Json.Nodes;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
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
    private readonly ITechnicalArticleSchemaBuilder _articleSchema;
    private readonly IBlogPostingSchemaBuilder _blogSchema;
    private readonly ISoftwareApplicationSchemaBuilder _toolSchema;

    public GccV2JsonLdBuilder(
        IOptions<CompanyProfileOptions> company,
        ITechnicalArticleSchemaBuilder articleSchema,
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
        string? slugOverride = null)
    {
        var slug = string.IsNullOrWhiteSpace(slugOverride) ? SlugHelper.Slugify(title) : slugOverride;
        var canonicalUrl = CanonicalUrlFor(contentType, slug, toolPageKind);
        return Build(contentType, toolPageKind, title, metaDescription, canonicalUrl, document, completedAt, keywords, pillarArticleUrl);
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
        string? pillarArticleUrl)
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
                new SoftwareApplicationDescriptor(title, metaDescription, canonicalUrl)),
            _ => null,
        };

        if (primary is null) return null;
        return MergeFaqPage(primary, ExtractFaqPairs(document));
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
