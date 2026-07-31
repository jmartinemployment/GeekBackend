using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing publications. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/publications")]
public class GcwPublicationsController : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "published",
        "failed",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwPublicationsController> _logger;

    public GcwPublicationsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwPublicationsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationDto>> GetById(Guid id, CancellationToken ct)
    {
        var publication = await _repo.GetPublicationByIdAsync(id, ct);
        if (publication is null)
            return NotFound();
        return Ok(publication);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationDto>>> List(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var publications = await _repo.GetPublicationsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(publications);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationDto>> Create(
        [FromBody] CreateGcwPublicationRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        _logger.LogInformation(
            "GCW user {UserId} creating publication for asset version {AssetVersionId}",
            _currentUser.UserId,
            request.AssetVersionId);

        var publication = await _repo.CreatePublicationAsync(
            new CreatePublicationCommand(request.AssetVersionId),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = publication.Id }, publication);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<PublicationDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwPublicationStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        var status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");
        status = AllowedStatuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} updating publication {PublicationId} to {Status}",
            _currentUser.UserId,
            id,
            status);

        try
        {
            var publication = await _repo.UpdatePublicationStatusAsync(id, status, ct);
            return Ok(publication);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwPublicationRequest(Guid AssetVersionId);
    public sealed record UpdateGcwPublicationStatusRequest(string Status);
}

[ApiController]
[Route("api/gcw/publication-events")]
public class GcwPublicationEventsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwPublicationEventsController> _logger;

    public GcwPublicationEventsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwPublicationEventsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationEventDto>>> List(
        [FromQuery] Guid publicationId,
        CancellationToken ct)
    {
        if (publicationId == Guid.Empty)
            return BadRequest("publicationId is required");

        var events = await _repo.GetPublicationEventsByPublicationIdAsync(publicationId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationEventDto>> Create(
        [FromBody] CreateGcwPublicationEventRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.PublicationId == Guid.Empty)
            return BadRequest("publicationId is required");
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        _logger.LogInformation(
            "GCW user {UserId} creating publication event for {PublicationId}",
            _currentUser.UserId,
            request.PublicationId);

        var @event = await _repo.CreatePublicationEventAsync(
            new CreatePublicationEventCommand(
                request.PublicationId,
                _currentUser.UserId,
                request.Status.Trim(),
                string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim()),
            ct);
        return Ok(@event);
    }

    public sealed record CreateGcwPublicationEventRequest(
        Guid PublicationId,
        string Status,
        string? Details = null);
}
