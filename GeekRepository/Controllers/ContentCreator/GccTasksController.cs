using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// Tasks and logged time, as rows. Reached only by a caller holding the repo key.
/// </summary>
[ApiController]
[Route("repo/content-creator/projects/{projectId:guid}")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccTasksController : ControllerBase
{
    private readonly IGccTaskRepository _repository;

    public GccTasksController(IGccTaskRepository repository) => _repository = repository;

    [HttpGet("tasks")]
    public async Task<ActionResult<IReadOnlyList<GccTaskDto>>> ListTasks(
        Guid projectId,
        CancellationToken ct) =>
        Ok(await _repository.ListByProjectAsync(projectId, ct));

    [HttpPost("tasks")]
    public async Task<ActionResult<GccTaskDto>> CreateTask(
        Guid projectId,
        [FromBody] CreateGccTaskCommand command,
        CancellationToken ct)
    {
        if (projectId != command.ProjectId)
            return BadRequest("The project id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");

        var task = await _repository.CreateTaskAsync(command, ct);
        if (task is null) return NotFound();
        return Ok(task);
    }

    [HttpPut("tasks/{taskId:guid}")]
    public async Task<ActionResult<GccTaskDto>> UpdateTask(
        Guid projectId,
        Guid taskId,
        [FromBody] UpdateGccTaskCommand command,
        CancellationToken ct)
    {
        if (taskId != command.Id)
            return BadRequest("The task id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("name is required.");
        if (string.IsNullOrWhiteSpace(command.ActorUserId))
            return BadRequest("actorUserId is required — every change is attributed.");
        if (!GccTaskStatuses.IsValid(command.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccTaskStatuses.All)}.");

        var task = await _repository.UpdateTaskAsync(command, ct);
        if (task is null) return NotFound();
        return Ok(task);
    }

    [HttpGet("time")]
    public async Task<ActionResult<IReadOnlyList<GccTimeEntryDto>>> ListTime(
        Guid projectId,
        CancellationToken ct) =>
        Ok(await _repository.ListTimeByProjectAsync(projectId, ct));

    [HttpGet("time/totals")]
    public async Task<ActionResult<GccProjectTimeTotals>> Totals(Guid projectId, CancellationToken ct) =>
        Ok(await _repository.TotalsForProjectAsync(projectId, ct));

    /// <summary>
    /// Log time.
    /// </summary>
    /// <remarks>
    /// A refusal comes back as 409 with its reason, not 500. "That client has no rate" is a
    /// sentence the operator has to read and act on, not a fault.
    /// </remarks>
    [HttpPost("time")]
    public async Task<ActionResult<GccTimeEntryDto>> LogTime(
        Guid projectId,
        [FromBody] CreateGccTimeEntryCommand command,
        CancellationToken ct)
    {
        if (projectId != command.ProjectId)
            return BadRequest("The project id in the route and the body must match.");
        if (string.IsNullOrWhiteSpace(command.UserId))
            return BadRequest("userId is required — logged time is always attributed.");
        if (command.Minutes <= 0)
            return BadRequest("minutes must be greater than zero.");

        var result = await _repository.LogTimeAsync(command, ct);
        if (result.Reason is not null) return Conflict(result.Reason);
        return Ok(result.Entry);
    }
}
