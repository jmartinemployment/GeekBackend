using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Geo;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>
/// AI-visibility readiness — dual SEO/GEO scores + published CMS URLs for a create, snapshotted via
/// <see cref="GccV2AiVisibilityService"/>. Not a live ChatGPT/Perplexity citation tracker; routes are
/// keyed by <c>createId</c> (matching <c>creates/{id}/generate</c> / <c>creates/{id}/publish</c>).
/// </summary>
[ApiController]
[Route("api/geek-content-creator-v2/creates/{createId:guid}/ai-visibility")]
public class GccV2AiVisibilityController : ControllerBase
{
    private readonly ICurrentUserContext _user;
    private readonly HttpGccV2Repository _repo;
    private readonly GccV2AiVisibilityService _service;
    private readonly ILogger<GccV2AiVisibilityController> _logger;

    public GccV2AiVisibilityController(
        ICurrentUserContext user,
        HttpGccV2Repository repo,
        GccV2AiVisibilityService service,
        ILogger<GccV2AiVisibilityController> logger)
    {
        _user = user;
        _repo = repo;
        _service = service;
        _logger = logger;
    }

    /// <summary>Returns the latest snapshot if one exists; otherwise builds and persists one on the
    /// fly from the create's latest completed job.</summary>
    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var existing = await _service.GetLatestAsync(createId, ct);
        if (existing is not null) return Ok(ToResponse(existing));

        try
        {
            var built = await _service.BuildAndPersistAsync(create, ct);
            return Ok(ToResponse(built));
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new { ready = false, createId, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error building AI-visibility snapshot for create {CreateId}.", createId);
            return Problem("Could not build an AI-visibility snapshot.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Force-rebuilds the snapshot from the create's current latest job (e.g. after a new
    /// generate/repair or a fresh CMS publish) and persists it as a new history row.</summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<object>> Refresh(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        try
        {
            var built = await _service.BuildAndPersistAsync(create, ct);
            return Ok(ToResponse(built));
        }
        catch (InvalidOperationException ex)
        {
            // Soft — same shape as GET when no scorable draft exists yet (multi-job race, shorts
            // without a document, etc.). Avoids noisy 422s on Canvas auto-refresh after JobCompleted.
            return Ok(new { ready = false, createId, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error refreshing AI-visibility snapshot for create {CreateId}.", createId);
            return Problem("Refresh failed unexpectedly.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<IReadOnlyList<object>>> History(Guid createId, CancellationToken ct)
    {
        if (!_user.IsAuthenticated) return Unauthorized();

        var create = await _repo.GetCreateAsync(createId, ct);
        if (create is null) return NotFound(new { error = "Create not found." });
        if (!IsOwner(create.OwnerUserId)) return StatusCode(StatusCodes.Status403Forbidden);

        var snapshots = await _service.ListAsync(createId, ct);
        return Ok(snapshots.Select(ToResponse).ToList());
    }

    private static object ToResponse(GccV2AiVisibilitySnapshotDto snapshot)
    {
        JsonElement? report = null;
        try
        {
            report = JsonSerializer.Deserialize<JsonElement>(snapshot.ReportJson);
        }
        catch (JsonException)
        {
            // Leave report null — snapshotId/score/createdAtUtc are still useful without it.
        }

        return new
        {
            ready = true,
            snapshotId = snapshot.Id,
            createId = snapshot.CreateId,
            jobId = snapshot.JobId,
            score = snapshot.Score,
            createdAtUtc = snapshot.CreatedAtUtc,
            report,
        };
    }

    private bool IsOwner(string ownerUserId) =>
        _user.IsAuthenticated && string.Equals(ownerUserId, _user.UserId.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
