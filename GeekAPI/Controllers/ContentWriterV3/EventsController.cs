using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentWriterV3;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentWriterV3;

[ApiController]
[Route("api/content-writer/v3/review-comments")]
public class ReviewCommentsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ReviewCommentsApiController> _logger;

    public ReviewCommentsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ReviewCommentsApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReviewCommentDto>> GetById(Guid id, CancellationToken ct)
    {
        var comment = await _repo.GetReviewCommentByIdAsync(id, ct);
        if (comment is null)
            return NotFound();

        return Ok(comment);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReviewCommentDto>>> GetByAssetVersionId([FromQuery] Guid assetVersionId, CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        _logger.LogInformation("User {UserId} fetching review comments for asset version {AssetVersionId}",
            _currentUser.UserId, assetVersionId);

        var comments = await _repo.GetReviewCommentsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(comments);
    }

    [HttpPost]
    public async Task<ActionResult<ReviewCommentDto>> Create([FromBody] CreateReviewCommentCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating review comment for asset version {AssetVersionId}",
            _currentUser.UserId, command.AssetVersionId);

        var comment = await _repo.CreateReviewCommentAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = comment.Id }, comment);
    }
}

[ApiController]
[Route("api/content-writer/v3/approval-events")]
public class ApprovalEventsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ApprovalEventsApiController> _logger;

    public ApprovalEventsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<ApprovalEventsApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApprovalEventDto>> GetById(Guid id, CancellationToken ct)
    {
        var @event = await _repo.GetApprovalEventByIdAsync(id, ct);
        if (@event is null)
            return NotFound();

        return Ok(@event);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalEventDto>>> GetByAssetVersionId([FromQuery] Guid assetVersionId, CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        _logger.LogInformation("User {UserId} fetching approval events for asset version {AssetVersionId}",
            _currentUser.UserId, assetVersionId);

        var events = await _repo.GetApprovalEventsByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalEventDto>> Create([FromBody] CreateApprovalEventCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating approval event for asset version {AssetVersionId}",
            _currentUser.UserId, command.AssetVersionId);

        var @event = await _repo.CreateApprovalEventAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = @event.Id }, @event);
    }
}

[ApiController]
[Route("api/content-writer/v3/publication-events")]
public class PublicationEventsApiController : ControllerBase
{
    private readonly HttpContentWriterV3Repository _repo;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<PublicationEventsApiController> _logger;

    public PublicationEventsApiController(
        HttpContentWriterV3Repository repo,
        ICurrentUserContext currentUser,
        ILogger<PublicationEventsApiController> logger)
    {
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationEventDto>> GetById(Guid id, CancellationToken ct)
    {
        var @event = await _repo.GetPublicationEventByIdAsync(id, ct);
        if (@event is null)
            return NotFound();

        return Ok(@event);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationEventDto>>> GetByPublicationId([FromQuery] Guid publicationId, CancellationToken ct)
    {
        if (publicationId == Guid.Empty)
            return BadRequest("publicationId is required");

        _logger.LogInformation("User {UserId} fetching publication events for publication {PublicationId}",
            _currentUser.UserId, publicationId);

        var events = await _repo.GetPublicationEventsByPublicationIdAsync(publicationId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationEventDto>> Create([FromBody] CreatePublicationEventCommand command, CancellationToken ct)
    {
        _logger.LogInformation("User {UserId} creating publication event for publication {PublicationId}",
            _currentUser.UserId, command.PublicationId);

        var @event = await _repo.CreatePublicationEventAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = @event.Id }, @event);
    }
}
