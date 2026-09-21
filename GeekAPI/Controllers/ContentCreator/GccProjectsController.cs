using System.Security.Claims;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GeekApplication.Validation;

namespace GeekAPI.Controllers.ContentCreator;

/// <summary>
/// Projects, on the v1 surface. The only HTTP surface for them.
/// </summary>
/// <remarks>
/// A separate file rather than more methods on <c>GccController</c>, which is already 1,747 lines
/// and holds the generate path. Same route prefix, so this is the same surface to a caller.
///
/// Every route requires the content-creator.manage scope. A valid GeekOAuth token is not enough:
/// the authority is shared across Geek apps, and this data is a client's billing-adjacent record.
///
/// The actor on every write is the token's <c>sub</c>, read here and never from the body. A caller
/// that could name the actor could attribute its changes to someone else, which would make the
/// project log worthless as evidence.
/// </remarks>
[ApiController]
[Route("api/geek-content-creator/projects")]
[Authorize(Policy = ContentCreatorAuthConstants.ManagePolicy)]
public class GccProjectsController : ControllerBase
{
    private readonly HttpGccRepository _repo;
    private readonly ILogger<GccProjectsController> _logger;

    public GccProjectsController(HttpGccRepository repo, ILogger<GccProjectsController> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccProjectDto>>> ListByClient(
        [FromQuery] Guid clientId,
        CancellationToken ct)
    {
        if (clientId == Guid.Empty)
            return BadRequest("clientId is required — a project always belongs to one client.");

        return Ok(await _repo.ListProjectsByClientAsync(clientId, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccProjectDto>> GetById(Guid id, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(project);
    }

    [HttpGet("{id:guid}/log")]
    public async Task<ActionResult<IReadOnlyList<GccProjectLogEntryDto>>> GetLog(Guid id, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(await _repo.GetProjectLogAsync(id, ct));
    }

    [HttpPost]
    public async Task<ActionResult<GccProjectDto>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        if (request.ClientId == Guid.Empty)
            return BadRequest("clientId is required.");
        if (request.IdempotencyKey == Guid.Empty)
            return BadRequest("idempotencyKey is required — it is what makes a repeat submit safe.");
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required.");
        if (!string.IsNullOrWhiteSpace(request.SiteUrl) && !GccUrlValidation.IsValid(request.SiteUrl))
            return BadRequest("siteUrl must be an absolute http or https URL.");
        if (GccUrlValidation.FirstInvalid(request.PartnerUrls) is { } badPartner)
            return BadRequest($"partnerUrls contains an invalid URL: '{badPartner}'. Each must be an absolute http or https URL.");
        if (GccUrlValidation.FirstInvalid(request.CompetitorUrls) is { } badCompetitor)
            return BadRequest($"competitorUrls contains an invalid URL: '{badCompetitor}'. Each must be an absolute http or https URL.");

        var result = await _repo.CreateProjectAsync(
            new CreateGccProjectCommand(
                request.ClientId,
                request.IdempotencyKey,
                request.Name,
                actor,
                request.StartDate,
                request.Code,
                request.Description,
                request.SiteUrl,
                request.ProjectSiteRunId,
                request.Department,
                request.PartnerUrls,
                request.CompetitorUrls,
                request.DueDate,
                request.EstimatedHours,
                request.Budget,
                request.BudgetCurrency),
            ct);

        if (result.Conflict)
            return Conflict("That idempotency key already belongs to another client's project.");

        return Ok(result.Project);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GccProjectDto>> Update(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("name is required.");
        if (!string.IsNullOrWhiteSpace(request.SiteUrl) && !GccUrlValidation.IsValid(request.SiteUrl))
            return BadRequest("siteUrl must be an absolute http or https URL.");
        if (GccUrlValidation.FirstInvalid(request.PartnerUrls) is { } badPartner)
            return BadRequest($"partnerUrls contains an invalid URL: '{badPartner}'. Each must be an absolute http or https URL.");
        if (GccUrlValidation.FirstInvalid(request.CompetitorUrls) is { } badCompetitor)
            return BadRequest($"competitorUrls contains an invalid URL: '{badCompetitor}'. Each must be an absolute http or https URL.");

        var project = await _repo.UpdateProjectAsync(
            new UpdateGccProjectCommand(
                id,
                actor,
                request.Name,
                request.StartDate,
                request.Code,
                request.Description,
                request.SiteUrl,
                request.ProjectSiteRunId,
                request.Department,
                request.PartnerUrls,
                request.CompetitorUrls,
                request.DueDate,
                request.EstimatedHours,
                request.Budget,
                request.BudgetCurrency),
            ct);

        return Ok(project);
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult<GccProjectDto>> ChangeStatus(
        Guid id,
        [FromBody] ChangeProjectStatusRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        if (!GccProjectStatuses.IsValid(request.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccProjectStatuses.All)}.");

        var finishing = request.Status == GccProjectStatuses.Finished;
        if (finishing && request.FinishedDate is null)
            return BadRequest("finishedDate is required when status is finished.");
        if (!finishing && request.FinishedDate is not null)
            return BadRequest("finishedDate is only set when status is finished.");

        var project = await _repo.ChangeProjectStatusAsync(
            new ChangeGccProjectStatusCommand(id, actor, request.Status, request.FinishedDate),
            ct);

        return Ok(project);
    }

    /// <summary>
    /// Delete a project. This is a soft delete — the row and its whole log survive underneath — but
    /// it disappears from every list and can no longer be fetched, exactly as a delete should look
    /// from here. A real, permanent DELETE is not reachable: every project carries a project_created
    /// log row that the append-only log can never lose.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        var deleted = await _repo.DeleteProjectAsync(id, actor, ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Delete one History entry, for real — not the project's soft delete above. Permanent: the
    /// deleted entry's content is gone, though its deletion is itself recorded as a new entry, so
    /// the log still shows that something was removed and by whom.
    /// </summary>
    [HttpDelete("{id:guid}/log/{logEntryId:long}")]
    public async Task<IActionResult> DeleteLogEntry(Guid id, long logEntryId, CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        var deleted = await _repo.DeleteProjectLogEntryAsync(id, logEntryId, actor, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{id:guid}/tasks")]
    public async Task<ActionResult<IReadOnlyList<GccTaskDto>>> ListTasks(Guid id, CancellationToken ct) =>
        Ok(await _repo.ListTasksAsync(id, ct));

    [HttpPost("{id:guid}/tasks")]
    public async Task<ActionResult<GccTaskDto>> CreateTask(
        Guid id,
        [FromBody] CreateTaskRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");

        return Ok(await _repo.CreateTaskAsync(
            new CreateGccTaskCommand(
                id,
                request.Name,
                actor,
                request.Description,
                request.AssigneeUserId,
                request.DueDate,
                request.EstimatedHours,
                request.SortOrder,
                request.ContentTypes),
            ct));
    }

    [HttpPut("{id:guid}/tasks/{taskId:guid}")]
    public async Task<ActionResult<GccTaskDto>> UpdateTask(
        Guid id,
        Guid taskId,
        [FromBody] UpdateTaskRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");
        if (!GccTaskStatuses.IsValid(request.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccTaskStatuses.All)}.");

        return Ok(await _repo.UpdateTaskAsync(
            id,
            new UpdateGccTaskCommand(
                taskId,
                actor,
                request.Name,
                request.Status,
                request.Description,
                request.AssigneeUserId,
                request.DueDate,
                request.EstimatedHours,
                request.SortOrder,
                request.ContentTypes),
            ct));
    }

    [HttpGet("{id:guid}/time")]
    public async Task<ActionResult<IReadOnlyList<GccTimeEntryDto>>> ListTime(Guid id, CancellationToken ct) =>
        Ok(await _repo.ListTimeAsync(id, ct));

    [HttpGet("{id:guid}/time/totals")]
    public async Task<ActionResult<GccProjectTimeTotals>> TimeTotals(Guid id, CancellationToken ct) =>
        Ok(await _repo.TimeTotalsAsync(id, ct));

    /// <summary>
    /// Log time against this project.
    /// </summary>
    /// <remarks>
    /// The user is the token subject, never the body: a caller who could name the user could log
    /// someone else's hours. A refusal comes back as 409 with its sentence — "that client has no
    /// rate" is something to read and act on, not a fault.
    /// </remarks>
    [HttpPost("{id:guid}/time")]
    public async Task<ActionResult<GccTimeEntryDto>> LogTime(
        Guid id,
        [FromBody] LogTimeRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (request.Minutes <= 0) return BadRequest("minutes must be greater than zero.");

        var result = await _repo.LogTimeAsync(
            new CreateGccTimeEntryCommand(
                id,
                actor,
                request.WorkDate,
                request.Minutes,
                request.Billable,
                request.TaskId,
                request.Description),
            ct);

        if (result.Reason is not null) return Conflict(result.Reason);
        return Ok(result.Entry);
    }

    [HttpGet("{id:guid}/deliverables")]
    public async Task<ActionResult<IReadOnlyList<GccDeliverableDto>>> ListDeliverables(
        Guid id,
        CancellationToken ct) =>
        Ok(await _repo.ListDeliverablesAsync(id, ct));

    /// <summary>
    /// Record a deliverable on this project.
    /// </summary>
    /// <remarks>
    /// A 409 carries the reason — the create belongs to another client, or it is already a
    /// deliverable somewhere else. Both are the operator's to resolve.
    /// </remarks>
    [HttpPost("{id:guid}/deliverables")]
    public async Task<ActionResult<GccDeliverableDto>> CreateDeliverable(
        Guid id,
        [FromBody] CreateDeliverableRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (request.CreateId == Guid.Empty)
            return BadRequest("createId is required — a deliverable is a create.");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");

        var result = await _repo.CreateDeliverableAsync(
            new CreateGccDeliverableCommand(
                id,
                request.CreateId,
                request.Name,
                actor,
                request.Type ?? "long-form",
                request.DueDate),
            ct);

        if (result.Reason is not null) return Conflict(result.Reason);
        return Ok(result.Deliverable);
    }

    [HttpPut("{id:guid}/deliverables/{deliverableId:guid}/status")]
    public async Task<ActionResult<GccDeliverableDto>> ChangeDeliverableStatus(
        Guid id,
        Guid deliverableId,
        [FromBody] ChangeDeliverableStatusRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (!GccDeliverableStatuses.IsValid(request.Status))
            return BadRequest($"status must be one of: {string.Join(", ", GccDeliverableStatuses.All)}.");

        return Ok(await _repo.ChangeDeliverableStatusAsync(
            id,
            new ChangeGccDeliverableStatusCommand(deliverableId, actor, request.Status),
            ct));
    }

    public sealed record CreateDeliverableRequest(
        Guid CreateId,
        string Name,
        string? Type = null,
        DateOnly? DueDate = null);

    public sealed record ChangeDeliverableStatusRequest(string Status);

    public sealed record CreateTaskRequest(
        string Name,
        string? Description = null,
        string? AssigneeUserId = null,
        DateOnly? DueDate = null,
        decimal? EstimatedHours = null,
        int SortOrder = 0,
        IReadOnlyList<string>? ContentTypes = null);

    public sealed record UpdateTaskRequest(
        string Name,
        string Status,
        string? Description = null,
        string? AssigneeUserId = null,
        DateOnly? DueDate = null,
        decimal? EstimatedHours = null,
        int SortOrder = 0,
        IReadOnlyList<string>? ContentTypes = null);

    /// <summary>What the browser sends. The user is deliberately absent — it comes from the token.</summary>
    public sealed record LogTimeRequest(
        DateOnly WorkDate,
        int Minutes,
        bool Billable,
        Guid? TaskId = null,
        string? Description = null);

    /// <summary>
    /// The token's subject, or null when the token carries none.
    /// </summary>
    /// <remarks>
    /// Both spellings are checked because inbound claim mapping has changed under this service
    /// before: unmapped the claim is "sub", mapped it becomes the WS-Federation nameidentifier URI.
    /// Returned as text, not parsed to a Guid — the subject is GeekOAuth's user id and this service
    /// has no business deciding what shape that is.
    /// </remarks>
    private string? CurrentSubject()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }

    /// <summary>
    /// What the browser sends. The actor is deliberately absent — it comes from the token.
    /// </summary>
    public sealed record CreateProjectRequest(
        Guid ClientId,
        Guid IdempotencyKey,
        string Name,
        DateOnly StartDate,
        string? Code = null,
        string? Description = null,
        string? SiteUrl = null,
        Guid? ProjectSiteRunId = null,
        string? Department = null,
        IReadOnlyList<string>? PartnerUrls = null,
        IReadOnlyList<string>? CompetitorUrls = null,
        DateOnly? DueDate = null,
        decimal? EstimatedHours = null,
        decimal? Budget = null,
        string? BudgetCurrency = null);

    public sealed record UpdateProjectRequest(
        string Name,
        DateOnly StartDate,
        string? Code = null,
        string? Description = null,
        string? SiteUrl = null,
        Guid? ProjectSiteRunId = null,
        string? Department = null,
        IReadOnlyList<string>? PartnerUrls = null,
        IReadOnlyList<string>? CompetitorUrls = null,
        DateOnly? DueDate = null,
        decimal? EstimatedHours = null,
        decimal? Budget = null,
        string? BudgetCurrency = null);

    public sealed record ChangeProjectStatusRequest(string Status, DateOnly? FinishedDate = null);
}
