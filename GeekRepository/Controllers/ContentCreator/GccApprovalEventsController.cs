using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

[ApiController]
[Route("repo/content-creator/approval-events")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccApprovalEventsController : ControllerBase
{
    private readonly IGccApprovalEventRepository _repository;
    private readonly ILogger<GccApprovalEventsController> _logger;

    public GccApprovalEventsController(IGccApprovalEventRepository repository, ILogger<GccApprovalEventsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccApprovalEventDto>> GetById(Guid id, CancellationToken ct)
    {
        var @event = await _repository.GetByIdAsync(id, ct);
        if (@event is null)
            return NotFound();

        return Ok(@event);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccApprovalEventDto>>> GetByArtifactVersionId(
        [FromQuery] Guid artifactVersionId,
        CancellationToken ct)
    {
        if (artifactVersionId == Guid.Empty)
            return BadRequest("artifactVersionId is required");

        var events = await _repository.GetByArtifactVersionIdAsync(artifactVersionId, ct);
        return Ok(events);
    }

    [HttpPost]
    public async Task<ActionResult<GccApprovalEventDto>> Create([FromBody] CreateGccApprovalEventCommand command, CancellationToken ct)
    {
        if (command is null)
            return BadRequest("Command is required");

        var @event = await _repository.CreateAsync(command, ct);
        return CreatedAtAction(nameof(GetById), new { id = @event.Id }, @event);
    }
}
