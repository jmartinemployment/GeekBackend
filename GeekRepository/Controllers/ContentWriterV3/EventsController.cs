using GeekApplication.Interfaces.ContentWriterV3;
using GeekApplication.Models.ContentWriterV3;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentWriterV3;

[ApiController]
[Route("repo/content-writer-v3/review-comments")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ReviewCommentsController : ControllerBase
{
    private readonly IReviewCommentRepository _repository;

    public ReviewCommentsController(IReviewCommentRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReviewCommentDto>> GetById(Guid id, CancellationToken ct)
    {
        var comment = await _repository.GetByIdAsync(id, ct);
        if (comment is null)
            return NotFound();

        return Ok(comment);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReviewCommentDto>>> GetByAssetVersionId([FromQuery] Guid assetVersionId, CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var comments = await _repository.GetByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(comments);
    }

    [HttpPost]
    public async Task<ActionResult<ReviewCommentDto>> Create([FromBody] CreateReviewCommentCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var comment = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = comment.Id }, comment);
    }

    [HttpPatch("{id:guid}/resolve")]
    public async Task<ActionResult<ReviewCommentDto>> Resolve(Guid id, [FromBody] ResolveRequest request, CancellationToken ct)
    {
        var comment = await _repository.ResolveAsync(new ResolveReviewCommentCommand(id, request.Resolution), ct);
        return Ok(comment);
    }

    public record ResolveRequest(string Resolution);
}

[ApiController]
[Route("repo/content-writer-v3/approval-events")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class ApprovalEventsController : ControllerBase
{
    private readonly IApprovalEventRepository _repository;

    public ApprovalEventsController(IApprovalEventRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApprovalEventDto>> GetById(Guid id, CancellationToken ct)
    {
        var @event = await _repository.GetByIdAsync(id, ct);
        if (@event is null)
            return NotFound();

        return Ok(@event);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApprovalEventDto>>> GetByAssetVersionId([FromQuery] Guid assetVersionId, CancellationToken ct)
    {
        if (assetVersionId == Guid.Empty)
            return BadRequest("assetVersionId is required");

        var events = await _repository.GetByAssetVersionIdAsync(assetVersionId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalEventDto>> Create([FromBody] CreateApprovalEventCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var @event = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = @event.Id }, @event);
    }
}

[ApiController]
[Route("repo/content-writer-v3/publication-events")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class PublicationEventsController : ControllerBase
{
    private readonly IPublicationEventRepository _repository;

    public PublicationEventsController(IPublicationEventRepository repository) => _repository = repository;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicationEventDto>> GetById(Guid id, CancellationToken ct)
    {
        var @event = await _repository.GetByIdAsync(id, ct);
        if (@event is null)
            return NotFound();

        return Ok(@event);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicationEventDto>>> GetByPublicationId([FromQuery] Guid publicationId, CancellationToken ct)
    {
        if (publicationId == Guid.Empty)
            return BadRequest("publicationId is required");

        var events = await _repository.GetByPublicationIdAsync(publicationId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<PublicationEventDto>> Create([FromBody] CreatePublicationEventCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var @event = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = @event.Id }, @event);
    }
}
