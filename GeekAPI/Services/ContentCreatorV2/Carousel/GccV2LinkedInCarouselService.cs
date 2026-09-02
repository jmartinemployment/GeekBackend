using System.Text.Json;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.BrandKit;
using GeekAPI.Services.ContentCreatorV2.ContentTypes;
using GeekAPI.Services.Workflow.Domain.Entities;
using GeekAPI.Services.Workflow.Providers;
using GeekAPI.Services.Workflow.Services;

namespace GeekAPI.Services.ContentCreatorV2.Carousel;

public static class GccV2LinkedInCarouselEligibility
{
    public static bool IsEligibleSource(string? contentType) =>
        GccV2LongFormTypes.IsArticleLike(contentType);
}

public sealed class GccV2LinkedInCarouselService
{
    private static readonly JsonSerializerOptions DocJson = CreateDocJson();

    private static JsonSerializerOptions CreateDocJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ParagraphJsonConverter());
        return options;
    }

    private readonly HttpGccV2Repository _repo;
    private readonly ILogger<GccV2LinkedInCarouselService> _logger;

    public GccV2LinkedInCarouselService(
        HttpGccV2Repository repo,
        ILogger<GccV2LinkedInCarouselService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<GccV2LinkedInCarouselResult> GenerateAsync(
        Guid createId,
        Guid jobId,
        IContentGenerationProvider provider,
        CancellationToken ct)
    {
        var job = await _repo.GetJobAsync(jobId, ct)
            ?? throw new InvalidOperationException("Job not found.");

        if (job.CreateId != createId)
            throw new InvalidOperationException("Job does not belong to this create.");

        if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Job is '{job.Status}' — carousel requires a ready draft.");

        var contentType = (job.ContentType ?? "").Trim().ToLowerInvariant();
        if (!GccV2LinkedInCarouselEligibility.IsEligibleSource(contentType))
        {
            throw new InvalidOperationException(
                $"Content type '{job.ContentType}' cannot be turned into a LinkedIn carousel — use a long-form article tab.");
        }

        if (string.IsNullOrWhiteSpace(job.ResultJson))
            throw new InvalidOperationException("No completed job result — generate content first.");

        var (document, title) = ParseSourceDocument(job.ResultJson);
        var brief = await _repo.GetBriefAsync(job.BriefId, ct);
        var brandKit = await LoadBrandKitAsync(job, ct);

        var request = GccV2LinkedInCarouselPromptBuilder.BuildRequest(
            document,
            title,
            brief?.TargetKeyword,
            brandKit);

        LinkedInCarouselDraft draft;
        try
        {
            var llm = await provider.CompleteAsync(request, ct);
            draft = GccV2LinkedInCarouselParser.Parse(llm.Content ?? "");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogWarning(ex, "Carousel LLM failed for job {JobId}; retrying once.", jobId);
            var retry = await provider.CompleteAsync(request with { Temperature = 0.35 }, ct);
            draft = GccV2LinkedInCarouselParser.Parse(retry.Content ?? "");
        }

        var slug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(draft.SuggestedFilename) ? title : draft.SuggestedFilename.Replace('_', '-'));
        var style = BuildBrandStyle(brandKit);
        var pdfBytes = GccV2LinkedInCarouselPdfService.Render(draft, style);
        var artifact = new LinkedInCarouselArtifact(draft, slug, DateTimeOffset.UtcNow);

        var mergedResultJson = MergeCarouselIntoResultJson(job.ResultJson, artifact);
        await _repo.PatchJobAsync(jobId, new PatchGccV2JobCommand(ResultJson: mergedResultJson), ct);

        _logger.LogInformation(
            "Generated LinkedIn carousel ({SlideCount} slides, {PdfKb} KB) for job {JobId}.",
            draft.Slides.Count,
            pdfBytes.Length / 1024,
            jobId);

        return new GccV2LinkedInCarouselResult(artifact, pdfBytes);
    }

    public static LinkedInCarouselBrandStyle BuildBrandStyle(GccV2BrandKitContent? brandKit) =>
        new(
            PrimaryColor: "#1E3A5F",
            SecondaryColor: "#2563EB",
            TextColor: "#1A1A1A",
            BackgroundColor: "#FFFFFF",
            CompanyName: brandKit?.CompanyName);

    internal static string MergeCarouselIntoResultJson(string existingResultJson, LinkedInCarouselArtifact artifact)
    {
        using var doc = JsonDocument.Parse(existingResultJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "linkedInCarousel", StringComparison.OrdinalIgnoreCase))
                    continue;
                prop.WriteTo(writer);
            }

            writer.WritePropertyName("linkedInCarousel");
            writer.WriteStartObject();
            writer.WriteString("slug", artifact.Slug);
            writer.WriteString("generatedAtUtc", artifact.GeneratedAtUtc.ToString("O"));
            writer.WriteString("caption", artifact.Draft.Caption);
            writer.WritePropertyName("hashtags");
            JsonSerializer.Serialize(writer, artifact.Draft.Hashtags, DocJson);
            writer.WriteString("suggestedFilename", artifact.Draft.SuggestedFilename);
            writer.WritePropertyName("slides");
            JsonSerializer.Serialize(writer, artifact.Draft.Slides.Select(s => new
            {
                index = s.Index,
                role = s.Role,
                title = s.Title,
                subtitle = s.Subtitle,
                bullets = s.Bullets,
            }), DocJson);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static LinkedInCarouselArtifact? TryParseArtifactFromResultJson(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("linkedInCarousel", out var carousel)
                || carousel.ValueKind != JsonValueKind.Object)
                return null;

            var slug = carousel.TryGetProperty("slug", out var slugEl) ? slugEl.GetString() ?? "carousel" : "carousel";
            var generatedAt = carousel.TryGetProperty("generatedAtUtc", out var atEl)
                && DateTimeOffset.TryParse(atEl.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            var caption = carousel.TryGetProperty("caption", out var capEl) ? capEl.GetString() ?? "" : "";
            var hashtags = carousel.TryGetProperty("hashtags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array
                ? tagsEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            var suggestedFilename = carousel.TryGetProperty("suggestedFilename", out var fnEl)
                ? fnEl.GetString() ?? slug
                : slug;

            if (!carousel.TryGetProperty("slides", out var slidesEl) || slidesEl.ValueKind != JsonValueKind.Array)
                return null;

            var slides = new List<CarouselSlide>();
            var i = 0;
            foreach (var slideEl in slidesEl.EnumerateArray())
            {
                var role = slideEl.TryGetProperty("role", out var rEl) ? rEl.GetString() ?? "teach" : "teach";
                var title = slideEl.TryGetProperty("title", out var tEl) ? tEl.GetString() ?? $"Slide {i + 1}" : $"Slide {i + 1}";
                var subtitle = slideEl.TryGetProperty("subtitle", out var stEl) ? stEl.GetString() : null;
                var bullets = slideEl.TryGetProperty("bullets", out var bEl) && bEl.ValueKind == JsonValueKind.Array
                    ? bEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                    : new List<string>();
                slides.Add(new CarouselSlide(i, role, title, bullets, subtitle));
                i++;
            }

            if (slides.Count == 0) return null;

            var draft = new LinkedInCarouselDraft(slides, caption, hashtags, suggestedFilename);
            return new LinkedInCarouselArtifact(draft, slug, generatedAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (ContentDocument Document, string Title) ParseSourceDocument(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("document", out var documentEl))
            throw new InvalidOperationException("Job result has no document body.");

        var document = JsonSerializer.Deserialize<ContentDocument>(documentEl.GetRawText(), DocJson)
            ?? throw new InvalidOperationException("Could not parse source document.");

        var title = root.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String
            ? titleEl.GetString() ?? "Untitled"
            : "Untitled";

        return (document, title);
    }

    private async Task<GccV2BrandKitContent?> LoadBrandKitAsync(GccV2JobDto job, CancellationToken ct)
    {
        if (job.ProjectSiteCrawlRunId is not { } profileId || profileId == Guid.Empty)
            return null;

        var kits = await _repo.ListBrandKitsByProfileAsync(profileId, ct);
        var kit = kits.FirstOrDefault();
        if (kit is null || string.IsNullOrWhiteSpace(kit.KitJson)) return null;

        try
        {
            return JsonSerializer.Deserialize<GccV2BrandKitContent>(kit.KitJson, DocJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record GccV2LinkedInCarouselResult(LinkedInCarouselArtifact Artifact, byte[] PdfBytes);
