using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Services;
using GeekApplication.Interfaces;
using GeekApplication.Models.Blog;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

public sealed record GccV2CmsPublishRequest(
    GccV2CreateDto Create,
    GccV2JobDto Job,
    bool IsPublished,
    string? CategorySlug,
    string? LanguageCode);

public sealed record GccV2CmsPublishResult(
    bool Success,
    string Status,
    string? Slug,
    string? PublicUrl,
    int? ExternalPostId,
    string? Error,
    string? Warning,
    Guid? PublishRecordId);

/// <summary>
/// Syncs a completed v2 job draft (its <c>ResultJson</c>'s <c>ContentDocument</c>) into the existing
/// Geek blog CMS via <see cref="IBlogRepository"/> — never talks to <c>geek_blog</c> directly.
/// Every attempt (success or failure) is recorded as a <c>GccV2PublishRecord</c> for audit/history,
/// and a <c>CmsPublished</c> job event is emitted on success so Canvas updates without polling.
/// </summary>
public sealed class GccV2CmsPublishService
{
    private static readonly JsonSerializerOptions ResultJsonOpts = CreateResultJsonOpts();

    private static JsonSerializerOptions CreateResultJsonOpts()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly IBlogRepository _blog;
    private readonly GccV2JobEventWriter _events;
    private readonly CompanyProfileOptions _company;
    private readonly ILogger<GccV2CmsPublishService> _logger;

    public GccV2CmsPublishService(
        HttpGccV2Repository repo,
        IBlogRepository blog,
        GccV2JobEventWriter events,
        IOptions<CompanyProfileOptions> company,
        ILogger<GccV2CmsPublishService> logger)
    {
        _repo = repo;
        _blog = blog;
        _events = events;
        _company = company.Value;
        _logger = logger;
    }

    public async Task<GccV2CmsPublishResult> PublishAsync(GccV2CmsPublishRequest request, CancellationToken ct)
    {
        var create = request.Create;
        var job = request.Job;
        var ownerUserId = ParseOwner(job.OwnerUserId);
        var languageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? "en" : request.LanguageCode.Trim();
        var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? "blog" : job.ContentType.Trim().ToLowerInvariant();

        JobResultPayload? payload;
        try
        {
            payload = string.IsNullOrWhiteSpace(job.ResultJson)
                ? null
                : JsonSerializer.Deserialize<JobResultPayload>(job.ResultJson, ResultJsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not parse ResultJson for job {JobId} during CMS publish.", job.Id);
            return await FailAsync(create, job, ownerUserId, create.Title, "", "Job result could not be parsed.", ct);
        }

        if (payload is not { Document: { } document })
        {
            return await FailAsync(create, job, ownerUserId, create.Title, "", "Job has no completed document to publish.", ct);
        }

        var title = string.IsNullOrWhiteSpace(payload.Title) ? create.Title : payload.Title;
        var slug = SlugHelper.Slugify(title);
        var documentJson = JsonSerializer.Serialize(document, ResultJsonOpts);

        try
        {
            var (postType, schemaType) = MapContentType(contentType);
            var categorySlug = string.IsNullOrWhiteSpace(request.CategorySlug)
                ? DefaultCategorySlug(contentType)
                : request.CategorySlug.Trim();
            categorySlug = await ResolveCategorySlugAsync(categorySlug, ct);

            var authorId = _company.DefaultBlogAuthorId > 0 ? _company.DefaultBlogAuthorId : (int?)null;

            var lede = FlattenPlainText(document.Lede);
            var summary = !string.IsNullOrWhiteSpace(payload.MetaDescription)
                ? payload.MetaDescription!.Trim()
                : Truncate(lede, 155);

            var sections = FlattenSections(document)
                .Select((section, index) => new PostSectionInput(
                    index,
                    section.Tag,
                    section.Heading,
                    RenderParagraphsHtml(section.Paragraphs),
                    null,
                    null))
                .ToList();

            var command = new UpsertBlogPostCommand
            {
                PostType = postType,
                SchemaType = schemaType,
                IsPublished = request.IsPublished,
                LanguageCode = languageCode,
                Slug = slug,
                Title = title,
                Summary = summary,
                MetaDescription = payload.MetaDescription,
                MainSummary = summary,
                HeroSummary = summary,
                HomeSummary = summary,
                BlogSummary = summary,
                AdvertisingSummary = summary,
                CategorySlug = categorySlug,
                AuthorId = authorId,
                CwJobId = job.Id.ToString("D"),
                Sections = sections,
            };

            var postId = await _blog.CreatePostAsync(command, ct);
            var publicUrl = BuildPublicUrl(contentType, languageCode, slug);
            var status = request.IsPublished ? "published" : "draft";

            var record = await _repo.CreatePublishRecordAsync(new CreateGccV2PublishRecordCommand(
                create.Id,
                job.Id,
                job.OwnerUserId,
                Channel: "blog",
                Status: status,
                ExternalPostId: postId,
                Slug: slug,
                PublicUrl: publicUrl,
                Title: title,
                MetaDescription: payload.MetaDescription,
                Error: null,
                BodyDocumentJson: documentJson,
                IsPublished: request.IsPublished,
                PublishedAtUtc: request.IsPublished ? DateTimeOffset.UtcNow : null), ct);

            await _events.AppendAsync(job.Id, ownerUserId, "CmsPublished", new
            {
                createId = create.Id,
                jobId = job.Id,
                publishRecordId = record.Id,
                status,
                externalPostId = postId,
                slug,
                publicUrl,
                isPublished = request.IsPublished,
            }, ct: ct);

            var warning = payload.OutstandingIssues == true
                ? "This draft had outstanding validation issues at publish time (operator override)."
                : null;

            return new GccV2CmsPublishResult(true, status, slug, publicUrl, postId, null, warning, record.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CMS publish failed for create {CreateId} job {JobId}.", create.Id, job.Id);
            return await FailAsync(create, job, ownerUserId, title, slug, ex.Message, ct, documentJson);
        }
    }

    private async Task<GccV2CmsPublishResult> FailAsync(
        GccV2CreateDto create,
        GccV2JobDto job,
        Guid ownerUserId,
        string title,
        string slug,
        string error,
        CancellationToken ct,
        string? documentJson = null)
    {
        Guid? recordId = null;
        try
        {
            var record = await _repo.CreatePublishRecordAsync(new CreateGccV2PublishRecordCommand(
                create.Id,
                job.Id,
                job.OwnerUserId,
                Channel: "blog",
                Status: "failed",
                ExternalPostId: null,
                Slug: slug,
                PublicUrl: null,
                Title: title,
                MetaDescription: null,
                Error: error,
                BodyDocumentJson: documentJson,
                IsPublished: false,
                PublishedAtUtc: null), ct);
            recordId = record.Id;

            await _events.AppendAsync(job.Id, ownerUserId, "CmsPublishFailed", new
            {
                createId = create.Id,
                jobId = job.Id,
                publishRecordId = record.Id,
                error,
            }, ct: ct);
        }
        catch (Exception persistEx)
        {
            _logger.LogError(persistEx, "Could not persist failed publish record for job {JobId}.", job.Id);
        }

        return new GccV2CmsPublishResult(false, "failed", null, null, null, error, null, recordId);
    }

    /// <summary>blog → geek_blog.post_type_enum "Blog"/"BlogPosting"; pillar/tool → "Pillar"/"Tool"
    /// with schema "TechnicalArticle" (matches <c>IBlogRepository.GetTechnicalArticlesOnlyAsync</c>,
    /// which filters on that schema type). Any other v2 content type (email/social/ads/…) falls back
    /// to Blog/BlogPosting since the CMS has no other post type for it.</summary>
    private static (string PostType, string SchemaType) MapContentType(string contentType) => contentType switch
    {
        "pillar" => ("Pillar", "TechnicalArticle"),
        "tool" => ("Tool", "TechnicalArticle"),
        _ => ("Blog", "BlogPosting"),
    };

    private static string DefaultCategorySlug(string contentType) => contentType switch
    {
        "pillar" or "tool" => "use-cases",
        _ => "blog",
    };

    /// <summary>If the preferred slug is missing from the CMS taxonomy, fall back to a blog-like
    /// category (or the first category) so publish fails with a clear message only when the
    /// taxonomy is empty.</summary>
    private async Task<string> ResolveCategorySlugAsync(string preferredSlug, CancellationToken ct)
    {
        IReadOnlyList<CategoryDto> categories;
        try
        {
            categories = await _blog.GetCategoriesAsync("en", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list blog categories; using preferred slug {Slug}.", preferredSlug);
            return preferredSlug;
        }

        if (categories.Count == 0)
            throw new InvalidOperationException(
                "geek_blog.categories is empty — seed at least one category before publishing.");

        if (categories.Any(c => string.Equals(c.Slug, preferredSlug, StringComparison.OrdinalIgnoreCase)))
            return preferredSlug;

        var blogLike = categories.FirstOrDefault(c =>
            c.Slug.Contains("blog", StringComparison.OrdinalIgnoreCase)
            || (c.Name?.Contains("blog", StringComparison.OrdinalIgnoreCase) ?? false));
        if (blogLike is not null)
        {
            _logger.LogWarning(
                "Category slug '{Preferred}' not found; falling back to '{Fallback}'.",
                preferredSlug, blogLike.Slug);
            return blogLike.Slug;
        }

        _logger.LogWarning(
            "Category slug '{Preferred}' not found; falling back to first category '{Fallback}'.",
            preferredSlug, categories[0].Slug);
        return categories[0].Slug;
    }

    private string BuildPublicUrl(string contentType, string languageCode, string slug)
    {
        var baseUrl = contentType switch
        {
            "pillar" or "tool" => _company.ArticleBaseUrl,
            _ => _company.BlogBaseUrl,
        };

        if (string.IsNullOrWhiteSpace(baseUrl)) return string.Empty;
        return $"{baseUrl.TrimEnd('/')}/{languageCode}/{slug}";
    }

    /// <summary>Depth-first: lede, then each top-level section and its children in document order —
    /// mirrors how <c>SectionHtmlRenderer</c> walks the tree, just flattened for the CMS's flat
    /// <c>post_sections</c> table instead of nested markup.</summary>
    private static IEnumerable<Section> FlattenSections(ContentDocument document)
    {
        yield return document.Lede;
        foreach (var section in document.Sections)
        {
            foreach (var flattened in FlattenSectionTree(section))
            {
                yield return flattened;
            }
        }
    }

    private static IEnumerable<Section> FlattenSectionTree(Section section)
    {
        yield return section;
        foreach (var child in section.Children)
        {
            foreach (var flattened in FlattenSectionTree(child))
            {
                yield return flattened;
            }
        }
    }

    private static string FlattenPlainText(Section section) => GccV2OverlapGate.FlattenPlainText(section);

    private static string Truncate(string text, int maxLength)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
    }

    /// <summary>Paragraph-only HTML (no heading) — heading is already carried separately as
    /// <c>PostSectionInput.HeadingTag</c>/<c>HeadingText</c>, so re-emitting it here via
    /// <c>SectionHtmlRenderer.RenderFragment</c> would duplicate it in the CMS's rendered body.</summary>
    private static string RenderParagraphsHtml(IReadOnlyList<Paragraph> paragraphs)
    {
        var doc = new HtmlDocument();
        var container = doc.CreateElement("div");
        doc.DocumentNode.AppendChild(container);

        foreach (var paragraph in paragraphs)
        {
            AppendParagraph(doc, container, paragraph);
        }

        return container.InnerHtml;
    }

    private static void AppendParagraph(HtmlDocument doc, HtmlNode parent, Paragraph paragraph)
    {
        switch (paragraph)
        {
            case TextParagraph text:
                var p = doc.CreateElement("p");
                AppendRuns(doc, p, text.Runs);
                parent.AppendChild(p);
                break;

            case ListParagraph list:
                var listNode = doc.CreateElement(list.Ordered ? "ol" : "ul");
                foreach (var item in list.Items)
                {
                    var li = doc.CreateElement("li");
                    AppendRuns(doc, li, item);
                    listNode.AppendChild(li);
                }
                parent.AppendChild(listNode);
                break;
        }
    }

    private static void AppendRuns(HtmlDocument doc, HtmlNode parent, IReadOnlyList<Run> runs)
    {
        foreach (var run in runs)
        {
            HtmlNode textHost = parent;

            if (!string.IsNullOrWhiteSpace(run.Href))
            {
                var anchor = doc.CreateElement("a");
                anchor.SetAttributeValue("href", System.Net.WebUtility.HtmlEncode(run.Href));
                parent.AppendChild(anchor);
                textHost = anchor;
            }

            if (run.Bold)
            {
                var strong = doc.CreateElement("strong");
                textHost.AppendChild(strong);
                textHost = strong;
            }

            if (run.Italic)
            {
                var em = doc.CreateElement("em");
                textHost.AppendChild(em);
                textHost = em;
            }

            textHost.AppendChild(doc.CreateTextNode(System.Net.WebUtility.HtmlEncode(run.Text)));
        }
    }

    private static Guid ParseOwner(string ownerUserId) => Guid.TryParse(ownerUserId, out var id) ? id : Guid.Empty;

    private sealed record JobResultPayload(
        string? Title,
        string? MetaDescription,
        ContentDocument? Document,
        bool? ShipReady,
        bool? OutstandingIssues);
}
