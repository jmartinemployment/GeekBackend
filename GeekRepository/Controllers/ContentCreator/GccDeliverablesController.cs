using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// Deliverables, as rows. Reached only by a caller holding the repo key.
/// </summary>
[ApiController]
[Route("repo/content-creator/projects/{projectId:guid}/deliverables")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccDeliverablesController : ControllerBase
{
    private readonly IGccDeliverableRepository _repository;

    public GccDeliverablesController(IGccDeliverableRepository repository) => _repository = repository;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccDeliverableDto>>> List(
        Guid projectId,
        CancellationToken ct) =>
        Ok(await _repository.ListByProjectAsync(projectId, ct));

    /// <summary>
    /// Record a deliverable.
    /// </summary>
    /// <remarks>
    /// A refusal is 409 with its reason: the create belongs to another client, or it is already a
    /// deliverable somewhere. Both are decisions the operator has to make, not faults.
    /// </remarks>
    [HttpPost]
    public async Task<ActionResult<GccDeliverableDto>> Create(
        Guid projectId,
        [FromBody] CreateGccDeliverableCommand command,
        CancellationToken ct)
    {
        if (projectId != command.ProjectId)
            return BadRequest("The project id in the route and the body must match.");
        if (command.CreateId == Guid.Empty)
            return BadRequest("createId is required — a deliverable is a create.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");

        var result = await _repository.CreateAsync(command, ct);
        if (result.Reason is not null) return Conflict(result.Reason);
        return Ok(result.Deliverable);
    }

    [HttpPut("{deliverableId:guid}/status")]
    public async Task<ActionResult<GccDeliverableDto>> ChangeStatus(
        Guid projectId,
        Guid deliverableId,
        [FromBody] ChangeGccDeliverableStatusCommand command,
        CancellationToken ct)
    {
        if (deliverableId != command.Id)
            return BadRequest("The deliverable id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");
        if (!GccDeliverableStatuses.IsValid(command.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccDeliverableStatuses.All)}.");

        var deliverable = await _repository.ChangeStatusAsync(command, ct);
        if (deliverable is null) return NotFound();
        return Ok(deliverable);
    }
}
