using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Publish;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// Sync CMS publish — pushes a completed v2 job draft into the existing Geek blog CMS via
/// <see cref="GccV2CmsPublishService"/>. Routes are keyed by <c>createId</c> (matching
/// <c>creates/{id}/generate</c> / <c>creates/{id}/canvas</c>) and default to that create's latest job.
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}")]
public class GccV2PublishController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2CmsPublishService _publish;
    private readonly ILogger<GccV2PublishController> _logger;

    public GccV2PublishController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2CmsPublishService publish,
        ILogger<GccV2PublishController> logger)
    {
        _user = user;
        _repo = repo;
        _publish = publish;
        _logger = logger;
    }

    [HttpPost("publish")]
    public async Task<ActionResult<object>> Publish(Guid createId, [FromBody] PublishRequest? request, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var job = request?.JobId is { } explicitJobId
            ? await _repo.GetJobAsync(explicitJobId, ct)
            : await _repo.GetLatestJobByCreateAsync(createId, ct);
        if (job is null || job.CreateId != createId) return NotFound(new { error = "No job found for this create." });
        if (!IsOwner(job.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var hasResult = !string.IsNullOrWhiteSpace(job.ResultJson);
        if (!string.Equals(job.Status, "ready", StringComparison.OrdinalIgnoreCase) && !hasResult)
        {
            return BadRequest(new
            {
                error = $"Job is '{job.Status}' with no completed result yet — nothing to publish.",
            });
        }

        try
        {
            var result = await _publish.PublishAsync(
                new GccV2CmsPublishRequest(
                    create,
                    job,
                    IsPublished: request?.IsPublished ?? false,
                    CategorySlug: request?.CategorySlug,
                    LanguageCode: request?.LanguageCode),
                ct);

            if (!result.Success)
            {
                return UnprocessableEntity(new
                {
                    error = result.Error ?? "Publish failed.",
                    status = result.Status,
                    publishRecordId = result.PublishRecordId,
                });
            }

            return Ok(new
            {
                status = result.Status,
                slug = result.Slug,
                publicUrl = result.PublicUrl,
                externalPostId = result.ExternalPostId,
                isPublished = request?.IsPublished ?? false,
                warning = result.Warning,
                publishRecordId = result.PublishRecordId,
            });
        }
        catch (Exception ex)
        {
            // GccV2CmsPublishService already catches/persists its own failures; this is a last-resort
            // guard so a genuinely unexpected error still returns a normal API response.
            _logger.LogError(ex, "Unhandled error publishing create {CreateId} job {JobId} to CMS.", createId, job.Id);
            return Problem("Publish failed unexpectedly.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("publish-records")]
    public async Task<ActionResult<IReadOnlyList<GccV2PublishRecordDto>>> ListPublishRecords(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var records = await _repo.ListPublishRecordsByCreateAsync(createId, ct);
        return Ok(records);
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);

    /// <summary><see cref="IsPublished"/> defaults to false (draft) — the caller opts into a live
    /// publish explicitly. <see cref="JobId"/> is optional — omit to target the create's latest job.</summary>
    public sealed record PublishRequest(Guid? JobId, bool? IsPublished, string? CategorySlug, string? LanguageCode);
}
