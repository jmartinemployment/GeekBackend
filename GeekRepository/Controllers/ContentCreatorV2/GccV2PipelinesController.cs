using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Internal persistence API for Geek Content Pipelines.</summary>
[ApiController]
[Route("repo/content-creator-v2/pipelines")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2PipelinesController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> DefinitionStatuses =
        ["draft", "published", "deprecated"];
    private static readonly HashSet<string> RunStatuses =
        ["queued", "running", "paused", "succeeded", "failed", "cancelled"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PipelineListItem>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");
        var items = await db.GccV2PipelineDefinitions.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new PipelineListItem(
                x.Id, x.Name, x.Description, x.Status, x.VersionNumber, x.Digest,
                x.UpdatedAtUtc, x.Runs.Count))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PipelineGraph>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");
        var definition = await db.GccV2PipelineDefinitions
            .Include(x => x.Runs.OrderByDescending(r => r.StartedAtUtc).Take(20))
                .ThenInclude(r => r.WorkItems)
                    .ThenInclude(w => w.StageAttempts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (definition is null) return NotFound();
        return Ok(ToGraph(definition));
    }

    [HttpPost]
    public async Task<ActionResult<PipelineGraph>> Create(
        [FromBody] CreatePipelineCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("OwnerUserId is required.");
        if (string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("Name is required.");
        if (string.IsNullOrWhiteSpace(command.StagesJson))
            return BadRequest("StagesJson is required.");
        if (string.IsNullOrWhiteSpace(command.Digest) || command.Digest.Length != 64)
            return BadRequest("Digest must be a 64-char sha256 hex.");

        var now = DateTimeOffset.UtcNow;
        var definition = new GccV2PipelineDefinition
        {
            OwnerUserId = command.OwnerUserId.Trim(),
            Name = command.Name.Trim(),
            Description = command.Description?.Trim() ?? "",
            Status = command.Publish == true ? "published" : "draft",
            VersionNumber = 1,
            Digest = command.Digest.Trim().ToLowerInvariant(),
            StagesJson = command.StagesJson,
            PolicyJson = string.IsNullOrWhiteSpace(command.PolicyJson) ? "{}" : command.PolicyJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.GccV2PipelineDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);
        return Ok(ToGraph(definition));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<PipelineGraph>> Publish(
        Guid id, [FromBody] ActorCommand command, CancellationToken ct)
    {
        var definition = await LoadOwned(id, command.OwnerUserId, ct);
        if (definition is null) return NotFound();
        if (definition.Status == "deprecated")
            return Conflict(new { error = "Deprecated pipelines cannot be published." });
        definition.Status = "published";
        definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToGraph(definition));
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<ActionResult<PipelineGraph>> StartRun(
        Guid id, [FromBody] StartPipelineRunCommand command, CancellationToken ct)
    {
        var definition = await LoadOwned(id, command.OwnerUserId, ct);
        if (definition is null) return NotFound();
        if (definition.Status != "published")
            return Conflict(new { error = "Only published pipelines can be run." });

        var stages = JsonSerializer.Deserialize<List<StageSeed>>(definition.StagesJson, JsonOpts) ?? [];
        if (stages.Count == 0)
            return BadRequest(new { error = "Pipeline has no stages." });

        var now = DateTimeOffset.UtcNow;
        var failStageKey = command.FailStageKey?.Trim();
        var workItemInputs = (command.WorkItemInputsJson ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();
        if (workItemInputs.Count == 0)
        {
            workItemInputs.Add(string.IsNullOrWhiteSpace(command.InputJson) ? "{}" : command.InputJson.Trim());
        }

        var run = new GccV2PipelineRun
        {
            PipelineDefinitionId = definition.Id,
            DefinitionVersionNumber = definition.VersionNumber,
            DefinitionDigest = definition.Digest,
            Status = "running",
            ActorUserId = command.ActorUserId?.Trim() ?? command.OwnerUserId.Trim(),
            InputJson = workItemInputs[0],
            HistoryJson = "[]",
            StartedAtUtc = now,
        };

        var history = new List<object>();
        var anyFailed = false;
        for (var index = 0; index < workItemInputs.Count; index++)
        {
            var workItem = new GccV2PipelineWorkItem
            {
                WorkItemIndex = index,
                InputJson = workItemInputs[index],
                Status = "running",
                UpdatedAtUtc = now,
            };
            run.WorkItems.Add(workItem);

            var failed = false;
            foreach (var stage in stages)
            {
                var attempt = new GccV2PipelineStageAttempt
                {
                    StageKey = stage.Key,
                    LifecycleStage = stage.Lifecycle,
                    Kind = stage.Kind,
                    DisplayName = stage.DisplayName,
                    CapabilityId = stage.CapabilityId,
                    Handoff = stage.Handoff,
                    AttemptNumber = 1,
                    StartedAtUtc = DateTimeOffset.UtcNow,
                };
                if (failed)
                {
                    attempt.Status = "skipped";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.Error = "Skipped after earlier stage failure.";
                }
                else if (!string.IsNullOrWhiteSpace(failStageKey)
                    && string.Equals(failStageKey, stage.Key, StringComparison.Ordinal)
                    && index == 0)
                {
                    attempt.Status = "failed";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.Error = $"Injected isolation failure at stage '{stage.Key}'.";
                    failed = true;
                }
                else
                {
                    attempt.Status = "succeeded";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.OutputJson = stage.Kind == "task-agent"
                        ? GccV2PipelineStageOutputs.ForTaskAgent(
                            stage.CapabilityId, stage.DisplayName, stage.Lifecycle, index)
                        : GccV2PipelineStageOutputs.ForHandoff(
                            stage.Handoff, stage.DisplayName, stage.Lifecycle, index);
                }
                workItem.StageAttempts.Add(attempt);
                history.Add(new
                {
                    atUtc = attempt.CompletedAtUtc,
                    workItemIndex = index,
                    stageKey = stage.Key,
                    lifecycle = stage.Lifecycle,
                    status = attempt.Status,
                    actor = run.ActorUserId,
                });
            }

            workItem.Status = failed ? "failed" : "succeeded";
            workItem.Error = failed ? $"Work item failed at stage '{failStageKey}'." : null;
            workItem.UpdatedAtUtc = DateTimeOffset.UtcNow;
            anyFailed |= failed;
        }

        run.Status = anyFailed ? "failed" : "succeeded";
        run.Error = anyFailed ? "One or more work items failed; later stages on failed items were skipped." : null;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        run.HistoryJson = JsonSerializer.Serialize(history, JsonOpts);
        definition.Runs.Add(run);
        definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(await ReloadGraph(definition.Id, definition.OwnerUserId, ct) ?? ToGraph(definition));
    }

    [HttpPost("runs/{runId:guid}/{action:regex(^pause|resume|cancel$)}")]
    public async Task<ActionResult<PipelineGraph>> TransitionRun(
        Guid runId, string action, [FromBody] ActorCommand command, CancellationToken ct)
    {
        var run = await db.GccV2PipelineRuns
            .Include(x => x.PipelineDefinition)
            .FirstOrDefaultAsync(x =>
                x.Id == runId && x.PipelineDefinition.OwnerUserId == command.OwnerUserId, ct);
        if (run is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        switch (action)
        {
            case "pause":
                if (run.Status is not ("queued" or "running"))
                    return Conflict(new { error = "Only queued/running runs can pause." });
                run.Status = "paused";
                run.PausedAtUtc = now;
                break;
            case "resume":
                if (run.Status != "paused")
                    return Conflict(new { error = "Only paused runs can resume." });
                run.Status = "running";
                run.PausedAtUtc = null;
                break;
            case "cancel":
                if (run.Status is "succeeded" or "failed" or "cancelled")
                    return Conflict(new { error = "Run is already terminal." });
                run.Status = "cancelled";
                run.CompletedAtUtc = now;
                run.Error = "Cancelled by operator.";
                break;
        }
        run.PipelineDefinition.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Ok(await ReloadGraph(run.PipelineDefinitionId, command.OwnerUserId, ct)
            ?? throw new InvalidOperationException("Pipeline disappeared after transition."));
    }

    private async Task<GccV2PipelineDefinition?> LoadOwned(
        Guid id, string? ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return null;
        return await db.GccV2PipelineDefinitions
            .Include(x => x.Runs.OrderByDescending(r => r.StartedAtUtc).Take(20))
                .ThenInclude(r => r.WorkItems)
                    .ThenInclude(w => w.StageAttempts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
    }

    private async Task<PipelineGraph?> ReloadGraph(Guid id, string ownerUserId, CancellationToken ct)
    {
        var definition = await LoadOwned(id, ownerUserId, ct);
        return definition is null ? null : ToGraph(definition);
    }

    private static PipelineGraph ToGraph(GccV2PipelineDefinition definition) =>
        new(
            definition.Id,
            definition.OwnerUserId,
            definition.Name,
            definition.Description,
            definition.Status,
            definition.VersionNumber,
            definition.Digest,
            definition.StagesJson,
            definition.PolicyJson,
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc,
            definition.Runs.Select(run => new PipelineRunDto(
                run.Id,
                run.DefinitionVersionNumber,
                run.DefinitionDigest,
                run.Status,
                run.ActorUserId,
                run.InputJson,
                run.HistoryJson,
                run.StartedAtUtc,
                run.CompletedAtUtc,
                run.PausedAtUtc,
                run.Error,
                run.WorkItems.OrderBy(w => w.WorkItemIndex).Select(item => new PipelineWorkItemDto(
                    item.Id,
                    item.WorkItemIndex,
                    item.InputJson,
                    item.Status,
                    item.Error,
                    item.UpdatedAtUtc,
                    item.StageAttempts.OrderBy(a => a.StartedAtUtc).Select(attempt =>
                        new PipelineStageAttemptDto(
                            attempt.Id,
                            attempt.StageKey,
                            attempt.LifecycleStage,
                            attempt.Kind,
                            attempt.DisplayName,
                            attempt.CapabilityId,
                            attempt.Handoff,
                            attempt.AttemptNumber,
                            attempt.Status,
                            attempt.OutputJson,
                            attempt.Error,
                            attempt.StartedAtUtc,
                            attempt.CompletedAtUtc)).ToList())).ToList())).ToList());

    public sealed record PipelineListItem(
        Guid Id, string Name, string Description, string Status, int VersionNumber,
        string Digest, DateTimeOffset UpdatedAtUtc, int RunCount);

    public sealed record PipelineGraph(
        Guid Id, string OwnerUserId, string Name, string Description, string Status,
        int VersionNumber, string Digest, string StagesJson, string PolicyJson,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<PipelineRunDto> Runs);

    public sealed record PipelineRunDto(
        Guid Id, int DefinitionVersionNumber, string DefinitionDigest, string Status,
        string ActorUserId, string InputJson, string HistoryJson,
        DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? PausedAtUtc,
        string? Error, IReadOnlyList<PipelineWorkItemDto> WorkItems);

    public sealed record PipelineWorkItemDto(
        Guid Id, int WorkItemIndex, string InputJson, string Status, string? Error,
        DateTimeOffset UpdatedAtUtc, IReadOnlyList<PipelineStageAttemptDto> StageAttempts);

    public sealed record PipelineStageAttemptDto(
        Guid Id, string StageKey, string LifecycleStage, string Kind, string DisplayName,
        string? CapabilityId, string? Handoff, int AttemptNumber, string Status,
        string? OutputJson, string? Error, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);

    public sealed record CreatePipelineCommand(
        string OwnerUserId, string Name, string? Description, string StagesJson,
        string Digest, string? PolicyJson = null, bool? Publish = true);

    public sealed record StartPipelineRunCommand(
        string OwnerUserId, string? ActorUserId = null, string? InputJson = null,
        string? FailStageKey = null, IReadOnlyList<string>? WorkItemInputsJson = null);

    public sealed record ActorCommand(string OwnerUserId, string? ActorUserId = null);

    private sealed record StageSeed(
        string Key, string Lifecycle, string Kind, string DisplayName,
        string? CapabilityId = null, string? Handoff = null);
}
