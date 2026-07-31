using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Gcw;

/// <summary>
/// GCW-facing content assets. Reuses Content Writer persistence via Repository —
/// not exposed under /api/content-writer/v3/*.
/// </summary>
[ApiController]
[Route("api/gcw/assets")]
public class GcwAssetsController : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pillar",
        "companion",
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft",
        "readyForApproval",
        "approved",
        "published",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwAssetsController> _logger;

    public GcwAssetsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwAssetsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetDto>> GetById(Guid id, CancellationToken ct)
    {
        var asset = await _repo.GetAssetByIdAsync(id, ct);
        if (asset is null)
            return NotFound();
        return Ok(asset);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetDto>>> List(
        [FromQuery] Guid campaignId,
        CancellationToken ct)
    {
        if (campaignId == Guid.Empty)
            return BadRequest("campaignId is required");

        var assets = await _repo.GetAssetsByCampaignIdAsync(campaignId, ct);
        return Ok(assets);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetDto>> Create(
        [FromBody] CreateGcwAssetRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.CampaignId == Guid.Empty)
            return BadRequest("campaignId is required");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required");
        if (string.IsNullOrWhiteSpace(request.Type))
            return BadRequest("type is required");

        var type = request.Type.Trim();
        if (!AllowedTypes.Contains(type))
            return BadRequest($"type must be one of: {string.Join(", ", AllowedTypes)}");
        type = AllowedTypes.First(t => t.Equals(type, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating asset for campaign {CampaignId}",
            _currentUser.UserId,
            request.CampaignId);

        var asset = await _repo.CreateAssetAsync(
            new CreateContentAssetCommand(request.CampaignId, type, request.Name.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = asset.Id }, asset);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<ContentAssetDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateGcwAssetStatusRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest("status is required");

        var status = request.Status.Trim();
        if (!AllowedStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", AllowedStatuses)}");
        status = AllowedStatuses.First(s => s.Equals(status, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} updating asset {AssetId} status to {Status}",
            _currentUser.UserId,
            id,
            status);

        try
        {
            var asset = await _repo.UpdateAssetStatusAsync(id, status, ct);
            return Ok(asset);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwAssetRequest(Guid CampaignId, string Type, string Name);
    public sealed record UpdateGcwAssetStatusRequest(string Status);
}

[ApiController]
[Route("api/gcw/asset-versions")]
public class GcwAssetVersionsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwAssetVersionsController> _logger;

    public GcwAssetVersionsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwAssetVersionsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> GetById(Guid id, CancellationToken ct)
    {
        var version = await _repo.GetAssetVersionByIdAsync(id, ct);
        if (version is null)
            return NotFound();
        return Ok(version);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ContentAssetVersionDto>>> List(
        [FromQuery] Guid assetId,
        CancellationToken ct)
    {
        if (assetId == Guid.Empty)
            return BadRequest("assetId is required");

        var versions = await _repo.GetAssetVersionsByAssetIdAsync(assetId, ct);
        return Ok(versions);
    }

    [HttpPost]
    public async Task<ActionResult<ContentAssetVersionDto>> Create(
        [FromBody] CreateGcwAssetVersionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetId == Guid.Empty)
            return BadRequest("assetId is required");
        if (string.IsNullOrWhiteSpace(request.BodyDocumentJson))
            return BadRequest("bodyDocumentJson is required");

        // Validate JSON shape
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(request.BodyDocumentJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest("bodyDocumentJson must be valid JSON");
        }

        _logger.LogInformation(
            "GCW user {UserId} creating asset version for asset {AssetId}",
            _currentUser.UserId,
            request.AssetId);

        var version = await _repo.CreateAssetVersionAsync(
            new CreateContentAssetVersionCommand(request.AssetId, request.BodyDocumentJson.Trim()),
            ct);
        return CreatedAtAction(nameof(GetById), new { id = version.Id }, version);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ContentAssetVersionDto>> Update(
        Guid id,
        [FromBody] UpdateGcwAssetVersionRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (string.IsNullOrWhiteSpace(request.BodyDocumentJson))
            return BadRequest("bodyDocumentJson is required");

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(request.BodyDocumentJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest("bodyDocumentJson must be valid JSON");
        }

        _logger.LogInformation(
            "GCW user {UserId} updating asset version {VersionId}",
            _currentUser.UserId,
            id);

        try
        {
            var version = await _repo.UpdateAssetVersionAsync(
                new UpdateContentAssetVersionCommand(id, request.BodyDocumentJson.Trim()),
                ct);
            return Ok(version);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwAssetVersionRequest(Guid AssetId, string BodyDocumentJson);
    public sealed record UpdateGcwAssetVersionRequest(string BodyDocumentJson);
}

[ApiController]
[Route("api/gcw/review-comments")]
public class GcwReviewCommentsController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwReviewCommentsController> _logger;

    public GcwReviewCommentsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwReviewCommentsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReviewCommentDto>>> List(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var comments = await _repo.GetReviewCommentsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(comments);
    }

    [HttpPost]
    public async Task<ActionResult<ReviewCommentDto>> Create(
        [FromBody] CreateGcwReviewCommentRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("content is required");

        _logger.LogInformation(
            "GCW user {UserId} creating review comment on version {VersionId}",
            _currentUser.UserId,
            request.AssetVersionId);

        var comment = await _repo.CreateReviewCommentAsync(
            new CreateReviewCommentCommand(
                request.AssetVersionId,
                _currentUser.UserId,
                string.IsNullOrWhiteSpace(request.SectionPath) ? null : request.SectionPath.Trim(),
                request.Content.Trim()),
            ct);
        return Ok(comment);
    }

    [HttpPatch("{id:guid}/resolve")]
    public async Task<ActionResult<ReviewCommentDto>> Resolve(
        Guid id,
        [FromBody] ResolveGcwReviewCommentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Resolution))
            return BadRequest("resolution is required");

        _logger.LogInformation(
            "GCW user {UserId} resolving review comment {CommentId}",
            _currentUser.UserId,
            id);

        try
        {
            var comment = await _repo.ResolveReviewCommentAsync(id, request.Resolution.Trim(), ct);
            return Ok(comment);
        }
        catch (HttpRequestException)
        {
            return NotFound();
        }
    }

    public sealed record CreateGcwReviewCommentRequest(
        Guid AssetVersionId,
        string Content,
        string? SectionPath = null);

    public sealed record ResolveGcwReviewCommentRequest(string Resolution);
}

[ApiController]
[Route("api/gcw/approval-events")]
public class GcwApprovalEventsController : ControllerBase
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "submitted",
        "approved",
        "rejected",
        "changes-requested",
    };

    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<GcwApprovalEventsController> _logger;

    public GcwApprovalEventsController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<GcwApprovalEventsController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalEventDto>>> List(
        [FromQuery] Guid assetVersionId,
        CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var events = await _repo.GetApprovalEventsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalEventDto>> Create(
        [FromBody] CreateGcwApprovalEventRequest request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest("Body is required");
        if (request.AssetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");
        if (string.IsNullOrWhiteSpace(request.Action))
            return BadRequest("action is required");

        var action = request.Action.Trim();
        if (!AllowedActions.Contains(action))
            return BadRequest($"action must be one of: {string.Join(", ", AllowedActions)}");
        action = AllowedActions.First(a => a.Equals(action, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "GCW user {UserId} creating approval event {Action} on version {VersionId}",
            _currentUser.UserId,
            action,
            request.AssetVersionId);

        var @event = await _repo.CreateApprovalEventAsync(
            new CreateApprovalEventCommand(
                request.AssetVersionId,
                _currentUser.UserId,
                action,
                string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()),
            ct);
        return Ok(@event);
    }

    public sealed record CreateGcwApprovalEventRequest(
        Guid AssetVersionId,
        string Action,
        string? Notes = null);
}
