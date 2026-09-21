using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// Projects, as rows. Reached only by a caller holding the repo key — in practice, GeekAPI.
/// </summary>
/// <remarks>
/// The actor on every write is supplied by the caller and originates in GeekAPI from a validated
/// JWT subject. This tier cannot verify a user token, so it records what it is told and refuses a
/// write that tells it nothing: an unattributed change to a project that carries billing is worth
/// less than no record at all.
/// </remarks>
[ApiController]
[Route("repo/content-creator/projects")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccProjectsController : ControllerBase
{
    private readonly IGccProjectRepository _repository;
    private readonly ILogger<GccProjectsController> _logger;

    public GccProjectsController(IGccProjectRepository repository, ILogger<GccProjectsController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccProjectDto>> GetById(Guid id, CancellationToken ct)
    {
        var project = await _repository.GetByIdAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccProjectDto>>> ListByClient(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required — a project always belongs to one client.");

        return Ok(await _repository.ListByClientIdAsync(clientId, ct));
    }

    [HttpGet("{id:guid}/log")]
    public async Task<ActionResult<IReadOnlyList<GccProjectLogEntryDto>>> GetLog(Guid id, CancellationToken ct)
    {
        var project = await _repository.GetByIdAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(await _repository.ListLogAsync(id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccProjectDto>> Create(
        [FromBody] CreateGccProjectCommand command,
        CancellationToken ct)
    {
        if (command.ClientId == Guid.Empty)
            return BadRequest("clientId is required.");
        if (command.IdempotencyKey == Guid.Empty)
            return BadRequest("idempotencyKey is required — it is what makes a repeat submit safe.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");

        var result = await _repository.CreateAsync(command, ct);
        if (result.Conflict)
        {
            _logger.LogWarning(
                "Project idempotency key {Key} is already in use by another client; refusing.",
                command.IdempotencyKey);
            return Conflict("That idempotency key already belongs to another client's project.");
        }

        // Never null when Conflict is false — the result type only has the two shapes.
        return Ok(result.Project);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GccProjectDto>> Update(
        Guid id,
        [FromBody] UpdateGccProjectCommand command,
        CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest("The id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");

        var project = await _repository.UpdateAsync(command, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<GccProjectDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeGccProjectStatusCommand command,
        CancellationToken ct)
    {
        if (id != command.Id)
            return BadRequest("The id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");
        if (!GccProjectStatuses.IsValid(command.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccProjectStatuses.All)}.");

        // The database enforces this pairing too; saying it here means the caller gets a sentence
        // rather than a constraint-violation stack trace.
        var finishing = command.Status == GccProjectStatuses.Finished;
        if (finishing && command.FinishedDate is null)
            return BadRequest("finishedDate is required when status is finished.");
        if (!finishing && command.FinishedDate is not null)
            return BadRequest("finishedDate is only set when status is finished.");

        var project = await _repository.ChangeStatusAsync(command, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    /// <summary>
    /// Soft-delete. Never a real DELETE — see DeletedAtUtc on GccProject. False (404) covers both
    /// "never existed" and "already deleted"; a caller cannot tell those apart, and does not need to.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromQuery] string actorUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");

        var deleted = await _repository.DeleteAsync(id, actorUserId, ct);
        return deleted ? NoContent() : NotFound();
    }
}
