using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Carousel;
using GeekAPI.Services.ContentCreatorV2.Transforms;
using GeekAPI.Services.Workflow.Providers;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}/transform")]
public class GccV2TransformController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2RepurposeTransformService _transform;
    private readonly GccV2LinkedInCarouselService _carousel;
    private readonly IContentProviderFactory _providers;

    public GccV2TransformController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2RepurposeTransformService transform,
        GccV2LinkedInCarouselService carousel,
        IContentProviderFactory providers)
    {
        _user = user;
        _repo = repo;
        _transform = transform;
        _carousel = carousel;
        _providers = providers;
    }

    /// <summary>
    /// Sync Re-Purpose: any ready generate job (<c>pillar</c>, <c>blog</c>, <c>tool</c>, <c>email</c>,
    /// <c>social</c>, <c>ads</c>) → the same channel variant pack. Pass <c>jobId</c> for the active
    /// draft tab. Image prompts are spawned separately per §3.1 — never via this endpoint.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<object>> Transform(Guid createId, [FromBody] TransformRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        if (request?.JobId is not { } requestedJobId || requestedJobId == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "jobId is required — Re-Purpose runs on the active draft tab, not the latest job on the create.",
            });
        }

        var job = await _repo.GetJobAsync(requestedJobId, ct);
        if (job is null || job.CreateId != createId || !IsOwner(job.OwnerUserId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(job.ResultJson))
            return BadRequest(new { error = "No completed job result to re-purpose — generate content first." });

        if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = $"Job is '{job.Status}' — Re-Purpose requires a ready draft.",
            });
        }

        var contentType = (job.ContentType ?? "").Trim().ToLowerInvariant();
        if (!GccV2RepurposeSourceTypes.IsAllowed(contentType))
        {
            return BadRequest(new
            {
                error = contentType is "image-prompt"
                    ? "Image-prompt jobs cannot be re-purposed — switch to a generate draft tab (pillar, blog, tool, email, social, or ads)."
                    : $"Content type '{job.ContentType}' cannot be re-purposed.",
                allowedTypes = GccV2RepurposeSourceTypes.Allowed.OrderBy(t => t).ToArray(),
            });
        }

        var sourceJson = ExtractSourceDocumentJson(job.ResultJson);
        if (string.IsNullOrWhiteSpace(sourceJson))
            return BadRequest(new { error = "Job result has no document body to re-purpose." });

        try
        {
            var provider = _providers.GetDefault();
            var result = await _transform.ApplyAsync(
                contentType,
                sourceJson,
                request?.Channels,
                provider,
                ct);
            return Ok(new
            {
                createId,
                jobId = job.Id,
                contentType = job.ContentType,
                variants = result.Variants.Select(v => new
                {
                    v.Channel,
                    v.Title,
                    v.Headline,
                    v.Body,
                    v.Cta,
                    v.Hashtags,
                    v.ContentDocumentJson,
                }),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Generate a LinkedIn document carousel PDF from a ready long-form draft tab.
    /// Persists slide structure on the source job's <c>ResultJson.linkedInCarousel</c>.
    /// </summary>
    [HttpPost("linkedin-carousel")]
    public async Task<ActionResult<object>> LinkedInCarousel(Guid createId, [FromBody] TransformRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound();
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        if (request?.JobId is not { } requestedJobId || requestedJobId == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "jobId is required — carousel runs on the active long-form draft tab.",
            });
        }

        var job = await _repo.GetJobAsync(requestedJobId, ct);
        if (job is null || job.CreateId != createId || !IsOwner(job.OwnerUserId))
            return NotFound();

        try
        {
            var provider = _providers.GetDefault();
            var result = await _carousel.GenerateAsync(createId, job.Id, provider, ct);
            var draft = result.Artifact.Draft;
            return Ok(new
            {
                createId,
                jobId = job.Id,
                contentType = job.ContentType,
                slug = result.Artifact.Slug,
                slideCount = draft.Slides.Count,
                caption = draft.Caption,
                hashtags = draft.Hashtags,
                suggestedFilename = draft.SuggestedFilename,
                pdfBase64 = Convert.ToBase64String(result.PdfBytes),
                slides = draft.Slides.Select(s => new
                {
                    s.Index,
                    s.Role,
                    s.Title,
                    s.Subtitle,
                    s.Bullets,
                }),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static string ExtractSourceDocumentJson(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        if (root.TryGetProperty("document", out var document))
            return JsonSerializer.Serialize(document, JsonOpts);

        return resultJson;
    }

    private bool IsOwner(string ownerUserId) =>
        _user.UserId != Guid.Empty && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);
}

public sealed record TransformRequest(IReadOnlyList<string>? Channels, Guid? JobId = null);
