using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.ContentCreatorV2.Validate;
using GeekAPI.Services.Workflow.DTOs;
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
    private readonly GccV2JsonLdBuilder _jsonLd;
    private readonly ILogger<GccV2CmsPublishService> _logger;

    public GccV2CmsPublishService(
        HttpGccV2Repository repo,
        IBlogRepository blog,
        GccV2JobEventWriter events,
        IOptions<CompanyProfileOptions> company,
        GccV2JsonLdBuilder jsonLd,
        ILogger<GccV2CmsPublishService> logger)
    {
        _repo = repo;
        _blog = blog;
        _events = events;
        _company = company.Value;
        _jsonLd = jsonLd;
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
        var toolKind = payload.ToolPageKind?.Trim().ToLowerInvariant();
        var slug = !string.IsNullOrWhiteSpace(payload.Slug)
            ? payload.Slug!
            : contentType == "tool" && toolKind == "overview"
                ? GccV2ToolSlugHelper.SlugifyKeyword(create.Title)
                : SlugHelper.Slugify(title);
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
            var summary = !string.IsNullOrWhiteSpace(payload.MainSummary)
                ? payload.MainSummary!.Trim()
                : !string.IsNullOrWhiteSpace(payload.MetaDescription)
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

            if (!string.IsNullOrWhiteSpace(payload.SourceAttributionHtml))
            {
                sections.Add(new PostSectionInput(
                    sections.Count,
                    "div",
                    "Sources",
                    payload.SourceAttributionHtml,
                    null,
                    null));
            }

            var completedAt = job.CompletedAtUtc ?? job.UpdatedAtUtc ?? DateTimeOffset.UtcNow;
            var canonicalUrl = BuildPublicUrl(contentType, languageCode, slug);
            var keywords = payload.Keywords ?? [];
            var jsonLd = payload.JsonLdSchema ?? _jsonLd.Build(
                contentType,
                toolKind,
                title,
                payload.MetaDescription ?? summary,
                canonicalUrl,
                document,
                completedAt,
                keywords,
                payload.PillarArticleUrl);

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
                MainSummary = payload.MainSummary ?? summary,
                HeroSummary = payload.HeroSummary ?? summary,
                HomeSummary = payload.HomeSummary ?? summary,
                BlogSummary = payload.BlogSummary ?? summary,
                AdvertisingSummary = payload.AdvertisingSummary ?? summary,
                JsonLdOverride = jsonLd,
                CategorySlug = categorySlug,
                AuthorId = authorId,
                CwJobId = job.Id.ToString("D"),
                Sections = sections,
            };

            var existingPostId = await ResolveExistingPostIdAsync(create, job, slug, languageCode, ct);
            int postId;
            if (existingPostId is { } existingId)
            {
                var updated = await _blog.UpdatePostAsync(existingId, command, ct);
                if (!updated)
                {
                    return await FailAsync(create, job, ownerUserId, title, slug, "CMS post could not be updated.", ct, documentJson);
                }

                postId = existingId;
            }
            else
            {
                postId = await _blog.CreatePostAsync(command, ct);
            }
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

    /// <summary>Long-form article-like types map to Pillar/TechnicalArticle; guide/listicle/blog to Blog/BlogPosting.</summary>
    private static (string PostType, string SchemaType) MapContentType(string contentType) => contentType switch
    {
        GccV2LongFormTypes.Pillar or GccV2LongFormTypes.Comparison or GccV2LongFormTypes.CaseStudy
            or GccV2LongFormTypes.Alternatives or GccV2LongFormTypes.TechArticle
            or GccV2LongFormTypes.Service or GccV2LongFormTypes.Local => ("Pillar", "TechnicalArticle"),
        GccV2LongFormTypes.Tool => ("Tool", "TechnicalArticle"),
        _ => ("Blog", "BlogPosting"),
    };

    private static string DefaultCategorySlug(string contentType) => contentType switch
    {
        GccV2LongFormTypes.Pillar or GccV2LongFormTypes.Tool
            or GccV2LongFormTypes.Comparison or GccV2LongFormTypes.CaseStudy
            or GccV2LongFormTypes.Alternatives or GccV2LongFormTypes.TechArticle
            or GccV2LongFormTypes.Service or GccV2LongFormTypes.Local => "use-cases",
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
        var normalized = GccV2LongFormTypes.Normalize(contentType);
        var baseUrl = normalized switch
        {
            GccV2LongFormTypes.Blog or GccV2LongFormTypes.Guide or GccV2LongFormTypes.Listicle =>
                _company.BlogBaseUrl,
            _ => _company.ArticleBaseUrl,
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

    private async Task<int?> ResolveExistingPostIdAsync(
        GccV2CreateDto create,
        GccV2JobDto job,
        string slug,
        string languageCode,
        CancellationToken ct)
    {
        var contentType = NormalizeContentType(job.ContentType);
        var records = await _repo.ListPublishRecordsByCreateAsync(create.Id, ct);
        var successful = records
            .Where(r => r.ExternalPostId is > 0 && !string.Equals(r.Status, "failed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();

        var sameJob = successful.FirstOrDefault(r => r.JobId == job.Id);
        if (sameJob?.ExternalPostId is { } sameJobPostId) return sameJobPostId;

        foreach (var record in successful)
        {
            var recordJob = await _repo.GetJobAsync(record.JobId, ct);
            if (recordJob is not null && NormalizeContentType(recordJob.ContentType) == contentType)
                return record.ExternalPostId;
        }

        try
        {
            var bySlug = await _blog.GetPostBySlugAsync(slug, languageCode, ct);
            if (bySlug is not null)
            {
                if (string.Equals(bySlug.CwJobId, job.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    return bySlug.PostId;

                if (string.IsNullOrWhiteSpace(bySlug.CwJobId))
                    return bySlug.PostId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve CMS post by slug {Slug} during upsert lookup.", slug);
        }

        return null;
    }

    private static string NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "blog" : contentType.Trim().ToLowerInvariant();

    private static Guid ParseOwner(string ownerUserId) => Guid.TryParse(ownerUserId, out var id) ? id : Guid.Empty;

    private sealed record JobResultPayload(
        string? Title,
        string? MetaDescription,
        ContentDocument? Document,
        bool? ShipReady,
        bool? OutstandingIssues,
        string? JsonLdSchema,
        List<string>? Keywords,
        string? MainSummary,
        string? HeroSummary,
        string? HomeSummary,
        string? BlogSummary,
        string? AdvertisingSummary,
        string? Slug,
        string? SourceAttributionHtml,
        string? ToolPageKind,
        string? PillarArticleUrl);
}
