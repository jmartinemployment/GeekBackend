using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Carousel;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.ContentCreatorV2.Jobs;
using GeekAPI.Services.ContentCreatorV2.ToolPages;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.DTOs;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;
using GeekAPI.Services.Workflow.Services.Export;
using Microsoft.Extensions.Options;

namespace GeekAPI.Services.ContentCreatorV2.Publish;

public sealed record GccV2ExportSkippedJob(Guid JobId, string ContentType, string Reason);

public sealed record GccV2ExportSummary(
    int ExportedCount,
    int TotalJobs,
    IReadOnlyList<GccV2ExportSkippedJob> Skipped);

public sealed record GccV2HtmlExportResult(
    IReadOnlyList<ExportedHtmlDocument> Documents,
    GccV2ExportSummary Summary);

public sealed class GccV2HtmlExportService
{
    private static readonly JsonSerializerOptions ResultJsonOpts = CreateResultJsonOpts();

    private static JsonSerializerOptions CreateResultJsonOpts()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly CompanyProfileOptions _company;
    private readonly GccV2JsonLdBuilder _jsonLd;

    public GccV2HtmlExportService(
        HttpGccV2Repository repo,
        IOptions<CompanyProfileOptions> company,
        GccV2JsonLdBuilder jsonLd)
    {
        _repo = repo;
        _company = company.Value;
        _jsonLd = jsonLd;
    }

    public Task<GccV2HtmlExportResult> ExportCreateAsync(Guid createId, CancellationToken ct) =>
        ExportCreateInternalAsync(createId, ct);

    public async Task<IReadOnlyList<ExportedHtmlDocument>> ExportDocumentsAsync(Guid createId, CancellationToken ct)
    {
        var result = await ExportCreateInternalAsync(createId, ct);
        return result.Documents;
    }

    private async Task<GccV2HtmlExportResult> ExportCreateInternalAsync(Guid createId, CancellationToken ct)
    {
        var create = await _repo.GetCreateAsync(createId, ct)
            ?? throw new ContentGenerationException($"Create {createId} was not found.");

        var jobs = await _repo.ListJobsByCreateAsync(createId, ct);
        var documents = new List<ExportedHtmlDocument>();
        var skipped = new List<GccV2ExportSkippedJob>();

        foreach (var job in jobs.OrderBy(j => j.ContentType).ThenBy(j => j.CreatedAtUtc))
        {
            var skipReason = GccV2ExportSkipEvaluator.TryGetSkipReason(job.ResultJson);
            if (skipReason is not null)
            {
                skipped.Add(new GccV2ExportSkippedJob(job.Id, job.ContentType ?? "unknown", skipReason));
                continue;
            }

            var payload = JsonSerializer.Deserialize<JobResultPayload>(job.ResultJson, ResultJsonOpts)!;
            var document = payload.Document!;

            var contentType = string.IsNullOrWhiteSpace(job.ContentType) ? "blog" : job.ContentType.Trim().ToLowerInvariant();
            var title = string.IsNullOrWhiteSpace(payload.Title) ? create.Title : payload.Title!;
            var toolKind = payload.ToolPageKind?.Trim().ToLowerInvariant();
            var slug = !string.IsNullOrWhiteSpace(payload.Slug)
                ? payload.Slug!
                : contentType == "tool" && toolKind == "overview"
                    ? GccV2ToolSlugHelper.SlugifyKeyword(create.Title)
                    : SlugHelper.Slugify(title);
            var metaDescription = payload.MetaDescription;

            if (GccV2ChannelTypes.IsLinkedInCarousel(contentType))
            {
                var draft = GccV2LinkedInCarouselDocumentConverter.FromContentDocument(document, title);
                var folder = "social/linkedin/carousels";
                var style = GccV2LinkedInCarouselService.BuildBrandStyle(null);
                documents.Add(new ExportedHtmlDocument(
                    $"{folder}/{slug}.pdf",
                    BinaryContent: GccV2LinkedInCarouselPdfService.Render(draft, style)));
                var hashtagLine = draft.Hashtags.Count > 0
                    ? "\n\n" + string.Join(" ", draft.Hashtags.Select(h => $"#{h.TrimStart('#')}"))
                    : "";
                documents.Add(new ExportedHtmlDocument($"{folder}/{slug}-caption.txt", draft.Caption + hashtagLine));
                documents.Add(new ExportedHtmlDocument(
                    $"{folder}/{slug}-slides.json",
                    GccV2LinkedInCarouselParser.SerializeDraft(draft)));
                continue;
            }

            if (contentType == "image-prompt")
            {
                var sectionMeta = ResolveImagePromptSection(payload, job, await _repo.GetBriefAsync(job.BriefId, ct));
                var articleSlug = sectionMeta is null
                    ? slug
                    : await ResolveArticleSlugAsync(sectionMeta.SourceJobId, slug, ct);
                var promptSlug = ImagePromptExportSlug(articleSlug, sectionMeta);
                var folder = ImagePromptFolderFor(sectionMeta?.SourceType);
                var text = !string.IsNullOrWhiteSpace(payload.Prompt)
                    ? payload.Prompt!
                    : PlainTextOf(document);
                documents.Add(new ExportedHtmlDocument($"{folder}/{promptSlug}.txt", text));
                continue;
            }

            if (contentType is "email" or "social" or "ads")
            {
                var folder = FolderFor(contentType);
                documents.Add(new ExportedHtmlDocument($"{folder}/{slug}.txt", PlainTextOf(document)));
                continue;
            }

            var canonicalUrl = _jsonLd.CanonicalUrlFor(contentType, slug, toolKind);
            var keywords = payload.Keywords is { Count: > 0 }
                ? string.Join(", ", payload.Keywords)
                : create.Title;
            var completedAt = job.CompletedAtUtc ?? job.UpdatedAtUtc ?? job.CreatedAtUtc;
            var keywordList = payload.Keywords is { Count: > 0 }
                ? payload.Keywords
                : keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var jsonLd = payload.JsonLdSchema ?? _jsonLd.Build(
                contentType,
                toolKind,
                title,
                metaDescription ?? string.Empty,
                canonicalUrl,
                document,
                completedAt,
                keywordList,
                payload.PillarArticleUrl);

            var meta = new Dictionary<string, string?>
            {
                ["slug"] = slug,
                ["department"] = "marketing",
                ["date"] = completedAt.ToString("O"),
                ["keywords"] = keywords,
                ["tags"] = keywords,
                ["excerpt"] = payload.Excerpt ?? metaDescription,
                ["mainSummary"] = payload.MainSummary ?? metaDescription,
                ["heroSummary"] = payload.HeroSummary ?? metaDescription,
                ["homeSummary"] = payload.HomeSummary ?? metaDescription,
                ["blogSummary"] = payload.BlogSummary ?? metaDescription,
                ["advertisingSummary"] = payload.AdvertisingSummary ?? metaDescription,
            };

            var html = SectionHtmlRenderer.RenderDocument(
                title,
                metaDescription,
                canonicalUrl,
                contentType is "blog" or "pillar" ? "article" : "website",
                _company.PublisherLogoUrl,
                jsonLdSchema: jsonLd,
                additionalMeta: meta,
                body: document,
                gtmContainerId: _company.GtmContainerId,
                siteName: _company.PublisherName,
                authorName: _company.AuthorName,
                faviconUrl: _company.FaviconUrl,
                googleSiteVerification: _company.GoogleSiteVerification,
                yandexVerification: _company.YandexVerification,
                yahooVerification: _company.YahooVerification);

            if (!string.IsNullOrWhiteSpace(payload.SourceAttributionHtml))
                html = GccV2ToolSectionRenderer.InjectBeforeBodyClose(html, payload.SourceAttributionHtml);

            var exportPath = ExportPathFor(contentType, slug, toolKind);
            documents.Add(new ExportedHtmlDocument(exportPath, html));

            AppendLinkedInCarouselExports(documents, job);
        }

        var summary = new GccV2ExportSummary(documents.Count, jobs.Count, skipped);
        if (documents.Count == 0)
        {
            throw new ContentGenerationException(
                "Nothing to export — no completed job documents on this create.");
        }

        return new GccV2HtmlExportResult(documents, summary);
    }

    private static ImagePromptSectionMeta? ResolveImagePromptSection(
        JobResultPayload payload,
        GccV2JobDto job,
        GccV2BriefDto? brief)
    {
        if (payload.ImagePromptSection is { } fromResult)
        {
            return new ImagePromptSectionMeta(
                fromResult.SourceJobId,
                fromResult.SourceType,
                fromResult.Heading,
                fromResult.Order);
        }

        return GccV2ImagePromptSpawnService.ParseImagePromptSection(brief?.RawBriefJson);
    }

    private async Task<string> ResolveArticleSlugAsync(Guid sourceJobId, string fallbackSlug, CancellationToken ct)
    {
        var sourceJob = await _repo.GetJobAsync(sourceJobId, ct);
        if (sourceJob is null || string.IsNullOrWhiteSpace(sourceJob.ResultJson)) return fallbackSlug;
        try
        {
            var payload = JsonSerializer.Deserialize<JobResultPayload>(sourceJob.ResultJson, ResultJsonOpts);
            var title = payload?.Title;
            return string.IsNullOrWhiteSpace(title) ? fallbackSlug : SlugHelper.Slugify(title);
        }
        catch (JsonException)
        {
            return fallbackSlug;
        }
    }

    private static void AppendLinkedInCarouselExports(
        List<ExportedHtmlDocument> documents,
        GccV2JobDto job)
    {
        var artifact = GccV2LinkedInCarouselService.TryParseArtifactFromResultJson(job.ResultJson);
        if (artifact is null) return;

        var slug = artifact.Slug;
        var folder = "social/linkedin/carousels";
        var style = GccV2LinkedInCarouselService.BuildBrandStyle(null);
        var pdfBytes = GccV2LinkedInCarouselPdfService.Render(artifact.Draft, style);

        documents.Add(new ExportedHtmlDocument($"{folder}/{slug}.pdf", BinaryContent: pdfBytes));

        var hashtagLine = artifact.Draft.Hashtags.Count > 0
            ? "\n\n" + string.Join(" ", artifact.Draft.Hashtags.Select(h => h.StartsWith('#') ? h : $"#{h}"))
            : "";
        documents.Add(new ExportedHtmlDocument(
            $"{folder}/{slug}-caption.txt",
            artifact.Draft.Caption + hashtagLine));

        documents.Add(new ExportedHtmlDocument(
            $"{folder}/{slug}-slides.json",
            GccV2LinkedInCarouselParser.SerializeDraft(artifact.Draft)));
    }

    public static string ImagePromptFolderFor(string? sourceType)
    {
        var normalized = (sourceType ?? "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "pillar-hero" => "image-prompts/pillar",
            "blog-hero" => "image-prompts/blog",
            "tool" => "image-prompts/sections",
            "email" => "image-prompts/email",
            "social" => "image-prompts/social/linkedin",
            "ads" => "image-prompts/ads",
            _ when normalized.EndsWith("-hero", StringComparison.Ordinal) =>
                $"image-prompts/{normalized[..^"-hero".Length]}",
            "pillar" or "blog" => "image-prompts/sections",
            _ when GccV2LongFormTypes.IsLongForm(normalized) => $"image-prompts/{GccV2LongFormTypes.ExportFolder(normalized)}",
            _ => "image-prompts/sections",
        };
    }

    public static string ImagePromptExportSlug(string articleSlug, ImagePromptSectionMeta? sectionMeta)
    {
        if (sectionMeta is null) return articleSlug;

        var sourceType = sectionMeta.SourceType.Trim().ToLowerInvariant();
        if (sourceType.EndsWith("-hero", StringComparison.Ordinal)
            || sourceType is "tool" or "email" or "social" or "ads")
            return $"{articleSlug}-{sourceType}";

        if (sourceType is "pillar" or "blog" || GccV2LongFormTypes.IsLongForm(sourceType))
            return $"{articleSlug}-{sourceType}-h2-{SlugHelper.Slugify(sectionMeta.Heading)}";

        return $"{articleSlug}-{sourceType}";
    }

    private static string PlainTextOf(ContentDocument document) =>
        document.Lede.Paragraphs.OfType<TextParagraph>().FirstOrDefault() is { } paragraph
            ? string.Join(" ", paragraph.Runs.Select(r => r.Text))
            : string.Empty;

    private static string ExportPathFor(string contentType, string slug, string? toolPageKind)
    {
        if (contentType == "tool" && string.Equals(toolPageKind, "overview", StringComparison.OrdinalIgnoreCase))
            return $"tools/{slug}.html";
        if (contentType == "tool")
            return $"tools/marketing/{slug}.html";
        return $"{FolderFor(contentType)}/{slug}.html";
    }

    private static string FolderFor(string contentType)
    {
        if (GccV2LongFormTypes.IsLongForm(contentType))
            return GccV2LongFormTypes.ExportFolder(contentType);
        return contentType switch
        {
            "email" => "email",
            "social" => "social/linkedin",
            "ads" => "ads",
            _ => "misc",
        };
    }

    private sealed record JobResultPayload(
        string? Title,
        string? MetaDescription,
        ContentDocument? Document,
        string? Prompt,
        string? JsonLdSchema,
        List<string>? Keywords,
        string? Excerpt,
        string? MainSummary,
        string? HeroSummary,
        string? HomeSummary,
        string? BlogSummary,
        string? AdvertisingSummary,
        string? Slug,
        string? SourceAttributionHtml,
        string? ToolPageKind,
        string? PillarArticleUrl,
        ImagePromptSectionPayload? ImagePromptSection);

    private sealed record ImagePromptSectionPayload(Guid SourceJobId, string SourceType, string Heading, int Order);
}
