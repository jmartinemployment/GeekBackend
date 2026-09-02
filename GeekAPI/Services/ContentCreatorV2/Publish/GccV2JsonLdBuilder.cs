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
        return normalized switch
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

    private static string CombineUrl(string baseUrl, string department, string slug) =>
        $"{baseUrl.TrimEnd('/')}/{department}/{slug}";
}
