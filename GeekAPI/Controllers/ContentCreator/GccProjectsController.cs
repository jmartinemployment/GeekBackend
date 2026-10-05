using System.Security.Claims;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekApplication.Models.ContentCreator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using GeekApplication.Validation;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.ContentCreator;

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
    private readonly GccDeclaredUrlValidator _declaredUrls;
    private readonly GccGenerateJobRunner _generateRunner;
    private readonly GccMustMentionBlockBuilder _mustMention;
    private readonly ILogger<GccProjectsController> _logger;

    public GccProjectsController(
        HttpGccRepository repo,
        GccDeclaredUrlValidator declaredUrls,
        GccGenerateJobRunner generateRunner,
        GccMustMentionBlockBuilder mustMention,
        ILogger<GccProjectsController> logger)
    {
        _repo = repo;
        _declaredUrls = declaredUrls;
        _generateRunner = generateRunner;
        _mustMention = mustMention;
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

    /// <summary>
    /// Every draft on the project, newest first. The project is the unit: which create a draft is
    /// stored under does not appear in how the page asks for it.
    /// </summary>
    [HttpGet("{id:guid}/artifacts")]
    public async Task<ActionResult<IReadOnlyList<GccArtifactDto>>> ListArtifacts(Guid id, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(id, ct);
        if (project is null) return NotFound();
        return Ok(await _repo.ListProjectArtifactsAsync(id, ct));
    }

    /// <summary>
    /// The project's drafts as a zip of standalone pages, foldered by content type with image prompts
    /// in their own tree -- the same files a create's export gives.
    /// </summary>
    [HttpGet("{id:guid}/export/html")]
    public async Task<IActionResult> ExportHtml(
        Guid id, [FromServices] GccArtifactExportService export, CancellationToken ct)
    {
        var project = await _repo.GetProjectAsync(id, ct);
        if (project is null) return NotFound();

        var documents = await export.ExportProjectAsync(id, ct);
        if (documents.Count == 0)
            return BadRequest("Nothing to export: this project has no generated drafts yet.");

        return File(await GccExportZip.WriteAsync(documents, ct), "application/zip", $"{id}-content-export.zip");
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

        var declared = await _declaredUrls.ForSaveAsync(
            request.SiteUrl, request.ProjectSiteRunId,
            request.PartnerUrls, request.CompetitorUrls, ct);
        if (declared.Refusal is { } refusal)
            return BadRequest(refusal);

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
                declared.PartnerUrls,
                declared.CompetitorUrls,
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

        var declared = await _declaredUrls.ForSaveAsync(
            request.SiteUrl, request.ProjectSiteRunId,
            request.PartnerUrls, request.CompetitorUrls, ct);
        if (declared.Refusal is { } refusal)
            return BadRequest(refusal);

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
                declared.PartnerUrls,
                declared.CompetitorUrls,
                request.DueDate,
                request.EstimatedHours,
                request.Budget,
                request.BudgetCurrency),
            ct);

        if (project.NotFound) return NotFound();
        if (project.Stale) return Conflict(GccProjectWriteResult.StaleMessage);
        return Ok(project.Project);
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

        if (project.NotFound) return NotFound();
        if (project.Stale) return Conflict(GccProjectWriteResult.StaleMessage);
        return Ok(project.Project);
    }

    /// <summary>
    /// Save the project's brief and keyword as one revision (GA1). The brief editor sends back the
    /// <c>version</c> it read; a save from an older read is refused 409 and nothing is written, so a
    /// stale tab can never overwrite a newer brief. Called only when Save is pressed. An incomplete
    /// brief saves (J3); a blank topic leaves the keyword as it is; a save identical to the newest
    /// revision writes nothing and returns that revision.
    /// </summary>
    /// <remarks>
    /// The version is the brief's own counter (brief_version), not the row's: a Profile save in another
    /// tab does not make an open brief stale. Only another brief Save does.
    /// </remarks>
    [HttpPatch("{id:guid}/brief")]
    public async Task<ActionResult<SaveBriefResponse>> SaveBrief(
        Guid id,
        [FromBody] SaveBriefRequest request,
        CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();

        if (request.Topic is { Length: > 1024 })
            return BadRequest("topic is at most 1024 characters.");
        if (request.BriefJson is not null && !IsJson(request.BriefJson))
            return BadRequest("briefJson must be a JSON document.");

        var saved = await _repo.SaveProjectBriefAsync(
            new SaveGccProjectBriefCommand(id, actor, request.BriefJson, request.Topic, request.ExpectedVersion),
            ct);

        if (saved.NotFound) return NotFound();
        if (saved.Stale) return Conflict(GccProjectWriteResult.StaleMessage);
        return Ok(new SaveBriefResponse(
            saved.Project!.Version,
            saved.Revision!.Id,
            saved.Revision.SavedAtUtc,
            saved.Project.Topic));
    }

    /// <summary>
    /// Generate on the project (GA2): the output types are chosen per run, the brief and keyword are the
    /// project's, and the run records the brief revision it read (J7). 202 with the job; progress on
    /// the hub exactly as before (JoinGccGenerate, GccGenerateEvent / GccGenerateTypeEvent /
    /// GccGeneratePreflightEvent).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drafts are still stored under a create while drafts are keyed by create (P0): the project's
    /// newest, or one GeekRepository mints in the same transaction as the run row. The browser never
    /// sends, receives or sees that create's id.
    /// </para>
    /// <para>
    /// The brief and keyword are the project's, read here. Research and the site section are read
    /// from that create, because in P0 their only writers -- the keyword-source upload and the site
    /// section pick -- still write there; GA3 moves those writers to the project, and these reads
    /// with them. One source per field, never a project value with a create value behind it.
    /// </para>
    /// <para>
    /// The brief carries no length band (J6): a lengthBand left in an older brief is dropped from what
    /// the writer is given, so it cannot override each output type's own length target.
    /// </para>
    /// <para>
    /// Refusals: 404 no project; 400 no keyword, an incomplete brief, a missing site crawl, a bad
    /// provider or output type; 409 a run already running on the project, named; 409 the brief was
    /// saved again after this request read it. Every refusal writes nothing.
    /// </para>
    /// </remarks>
    [HttpPost("{id:guid}/generate")]
    public async Task<IActionResult> Generate(Guid id, [FromBody] GenerateRequest? request, CancellationToken ct)
    {
        var actor = CurrentSubject();
        if (actor is null) return Unauthorized();
        if (!Guid.TryParse(actor, out var ownerUserId))
            return BadRequest("The token's subject is not a user id, so the run cannot be attributed.");

        var project = await _repo.GetProjectAsync(id, ct);
        if (project is null) return NotFound();
        if (string.IsNullOrWhiteSpace(project.Topic))
            return BadRequest("keyword required: save the brief with a keyword before generating.");

        if (!GccController.TryParseProvider(request?.Provider, out var provider, out var providerError))
            return BadRequest(providerError);

        var requested = GccGenerationCoordinator.NormalizeRequestedTypes(request?.OutputTypes);
        var typeRefusal = GccGenerationCoordinator.ValidateRequestedTypes(requested);
        if (typeRefusal is not null) return BadRequest(typeRefusal);

        // The declared URLs were validated when they were entered. Asked once more here, before the
        // run starts and before anything is spent, in case the index has lost one since.
        var declared = await _declaredUrls.ForGenerateAsync(project, ct);
        if (declared.Refusal is { } unusable) return Conflict(unusable);

        var backing = await _repo.GetProjectBackingCreateAsync(id, ct);
        var view = ProjectView(project, backing?.Id ?? Guid.Empty, ownerUserId, requested[0], backing);
        var section = GccGenerateService.ParseSiteSection(view.SiteSectionJson);
        try
        {
            GccGenerateService.ValidateSiteSectionGate(view.ProjectSiteRunId, section);
            GccGenerateService.ValidateBriefRequired(view, requireLengthBand: false);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        // Before the run row exists: the keyword and site run it reads are the project's, and anything
        // that fails here must fail with nothing started.
        var mustMentionBlock = await _mustMention.BuildAsync(view, ct);

        var started = await _repo.StartGenerateJobAsync(
            new StartGccGenerateJobCommand(
                Guid.NewGuid(), id, backing?.Id, project.Version, ownerUserId, requested, provider.ToString()),
            ct);
        if (started.ProjectNotFound) return NotFound();
        if (started.AlreadyRunning is { } running)
            return Conflict(
                $"A Generate is already running on this project (job {running.Id}, started "
                + $"{running.StartedAtUtc:u} for {string.Join(", ", running.RequestedTypes)}). "
                + "Nothing was started -- wait for it to finish.");
        if (started.StaleBrief)
            return Conflict("The brief was saved again after this page loaded it. Nothing was started -- "
                + "reload and generate again, so the run uses the brief on screen.");
        if (started.NoSavedBrief)
            return BadRequest("brief required: save the brief before generating.");
        if (started.CreateNotOnProject || started.Job is null)
            return Conflict("The project's drafts changed while this run was being started. Nothing was "
                + "started -- generate again.");

        var row = started.Job;
        var writeUnder = backing is not null && backing.Id == row.CreateId
            ? view
            : ProjectView(project, row.CreateId, ownerUserId, requested[0], backing: null);
        var job = _generateRunner.StartForProject(
            row, writeUnder, section, provider, requested, mustMentionBlock, actor);

        return Accepted(new { jobId = job.Id, projectId = id, status = job.Status });
    }

    /// <summary>
    /// The create the coordinator writes under, as it must see it: the project's brief (without a
    /// length band) and keyword, the project's site run and grounding key, and the backing create's
    /// research and site section. The starting content type is the first requested one -- what a
    /// minted create is stamped with -- and every output type overrides it with its own anyway.
    /// </summary>
    internal static GccCreateDto ProjectView(
        GccProjectDto project, Guid createId, Guid ownerUserId, string firstRequestedType, GccCreateDto? backing) =>
        new(
            createId,
            project.ClientId,
            backing?.OwnerUserId ?? ownerUserId,
            firstRequestedType,
            project.Topic!.Trim(),
            backing?.Notes,
            project.ProjectSiteRunId,
            backing?.SiteSectionJson,
            WithoutLengthBand(project.BriefJson),
            backing?.ResearchJson,
            backing?.Status ?? "draft",
            backing?.CreatedAtUtc ?? project.CreatedAtUtc,
            backing?.UpdatedAtUtc ?? project.UpdatedAtUtc,
            string.IsNullOrWhiteSpace(project.Department) ? "marketing" : project.Department,
            project.Id);

    /// <summary>The brief as the writer gets it on the project path: no lengthBand (J6).</summary>
    internal static string? WithoutLengthBand(string? briefJson)
    {
        if (string.IsNullOrWhiteSpace(briefJson)) return briefJson;
        if (System.Text.Json.Nodes.JsonNode.Parse(briefJson) is not System.Text.Json.Nodes.JsonObject brief)
            return briefJson;
        if (!brief.Remove("lengthBand")) return briefJson;
        return brief.ToJsonString();
    }

    private static bool IsJson(string value)
    {
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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
        if (deleted.NotFound) return NotFound();
        if (deleted.Stale) return Conflict(GccProjectWriteResult.StaleMessage);
        return NoContent();
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
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("name is required.");

        var result = await _repo.CreateDeliverableAsync(
            new CreateGccDeliverableCommand(
                id,
                request.Name,
                actor,
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

    /// <summary>A named, dated promise on the project. Nothing is attached to it.</summary>
    public sealed record CreateDeliverableRequest(
        string Name,
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

    /// <summary>What the brief editor sends. The actor comes from the token.</summary>
    public sealed record SaveBriefRequest(string? BriefJson, string? Topic, int ExpectedVersion);

    /// <summary>The new version to send with the next save, and the revision this save wrote.</summary>
    public sealed record SaveBriefResponse(int Version, Guid RevisionId, DateTime SavedAtUtc, string? Topic);

    /// <summary>What the browser sends to Generate. AcknowledgeStaleGrounding is accepted for the
    /// contract's shape; Generate has no staleness gate that can fire (see GccController).</summary>
    public sealed record GenerateRequest(
        IReadOnlyList<string>? OutputTypes,
        string? Provider,
        bool AcknowledgeStaleGrounding = false);
}
