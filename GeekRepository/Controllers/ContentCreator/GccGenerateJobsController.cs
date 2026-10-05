using GeekApplication.Interfaces.ContentCreator;
using GeekApplication.Models.ContentCreator;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.ContentCreator;

/// <summary>
/// Generate runs on a project, as rows. Reached only by a caller holding the repo key -- GeekAPI.
/// </summary>
[ApiController]
[Route("repo/content-creator/generate-jobs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public class GccGenerateJobsController : ControllerBase
{
    private readonly IGccGenerateJobRepository _jobs;

    public GccGenerateJobsController(IGccGenerateJobRepository jobs) => _jobs = jobs;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccGenerateJobDto>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _jobs.GetByIdAsync(id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>The project's newest run, running or ended. 404 when the project has never run.</summary>
    [HttpGet("~/repo/content-creator/projects/{projectId:guid}/generate-jobs/latest")]
    public async Task<ActionResult<GccGenerateJobDto>> GetLatestForProject(Guid projectId, CancellationToken ct)
    {
        var job = await _jobs.GetLatestForProjectAsync(projectId, ct);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>The create the project's drafts are stored under. 404 when the project has none yet.</summary>
    [HttpGet("~/repo/content-creator/projects/{projectId:guid}/backing-create")]
    public async Task<ActionResult<GccCreateDto>> GetBackingCreate(Guid projectId, CancellationToken ct)
    {
        var create = await _jobs.GetBackingCreateAsync(projectId, ct);
        return create is null ? NotFound() : Ok(create);
    }

    /// <summary>
    /// Start a run. Always 200 with the result once the body is valid: a refusal (no project, a stale or
    /// unsaved brief, a run already running, a create from another project) is an answer the caller
    /// acts on, and each needs a different sentence.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GccGenerateJobStartResult>> Start(
        [FromBody] StartGccGenerateJobCommand command,
        CancellationToken ct)
    {
        if (command.Id == Guid.Empty) return BadRequest("id is required -- GeekAPI mints it.");
        if (command.OwnerUserId == Guid.Empty) return BadRequest("ownerUserId is required.");
        if (command.RequestedTypes is not { Count: > 0 }) return BadRequest("requestedTypes is required.");
        if (string.IsNullOrWhiteSpace(command.Provider)) return BadRequest("provider is required.");

        return Ok(await _jobs.StartAsync(command, ct));
    }

    [HttpPut("{id:guid}/complete")]
    public async Task<ActionResult<GccGenerateJobDto>> Complete(
        Guid id,
        [FromBody] CompleteGccGenerateJobCommand command,
        CancellationToken ct)
    {
        var job = await _jobs.CompleteAsync(id, command.ResultJson, ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPut("{id:guid}/fail")]
    public async Task<ActionResult<GccGenerateJobDto>> Fail(
        Guid id,
        [FromBody] FailGccGenerateJobCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Error)) return BadRequest("error is required -- a failure says why.");
        var job = await _jobs.FailAsync(id, command.Error, ct);
        return job is null ? NotFound() : Ok(job);
    }

    /// <summary>Called once by GeekAPI at startup. Returns how many running jobs it failed.</summary>
    [HttpPost("fail-interrupted")]
    public async Task<ActionResult<int>> FailInterrupted(
        [FromBody] FailInterruptedGccGenerateJobsCommand command,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Error)) return BadRequest("error is required -- a failure says why.");
        return Ok(await _jobs.FailInterruptedAsync(command.Error, ct));
    }
}
