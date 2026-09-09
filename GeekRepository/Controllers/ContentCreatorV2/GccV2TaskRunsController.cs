using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/task-runs")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2TaskRunsController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> Terminal = ["succeeded", "failed", "cancelled"];
    private static readonly HashSet<string> States = ["queued", "running", "succeeded", "failed", "cancelled"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2TaskRun>>> List(
        [FromQuery] string ownerUserId, [FromQuery] string? status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId)) return BadRequest("ownerUserId is required.");
        var query = db.GccV2TaskRuns.AsNoTracking().Where(x => x.OwnerUserId == ownerUserId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        return Ok(await query.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(ct));
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<GccV2TaskRun>> Get(
        Guid runId, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        var run = await db.GccV2TaskRuns.AsNoTracking()
            .Include(x => x.Events.OrderBy(e => e.Seq))
            .Include(x => x.Artifacts).ThenInclude(x => x.Versions.OrderBy(v => v.VersionNumber))
                .ThenInclude(x => x.Parents)
            .Include(x => x.Artifacts).ThenInclude(x => x.Versions).ThenInclude(x => x.Children)
            .SingleOrDefaultAsync(x => x.Id == runId && x.OwnerUserId == ownerUserId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2TaskRun>> Create(CreateRunCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Actor))
            return BadRequest("ownerUserId and actor are required.");
        var version = await db.GccV2TaskAgentVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == command.TaskAgentVersionId, ct);
        if (version is null || version.DefinitionId != command.TaskAgentDefinitionId || version.State != "published")
            return Conflict("The exact published task-agent definition/version pin is required.");
        if (!string.Equals(version.VersionDigest, command.TaskAgentVersionDigest, StringComparison.Ordinal))
            return Conflict("Task-agent version digest does not match the pinned version.");
        string input, model, budget, source;
        try
        {
            input = GccV2TaskAgentsController.Canonical(command.InputJson, JsonValueKind.Object);
            model = GccV2TaskAgentsController.Canonical(command.ModelSnapshotJson, JsonValueKind.Object);
            budget = GccV2TaskAgentsController.Canonical(command.BudgetSnapshotJson, JsonValueKind.Object);
            source = GccV2TaskAgentsController.Canonical(command.SourceSnapshotJson, JsonValueKind.Object);
        }
        catch (JsonException ex) { return BadRequest(ex.Message); }
        if (!DigestMatches(input, command.InputDigest)
            || !DigestMatches(model, command.ModelSnapshotDigest)
            || !DigestMatches(budget, command.BudgetSnapshotDigest)
            || !DigestMatches(source, command.SourceSnapshotDigest))
            return BadRequest("One or more snapshot digests are invalid.");
        if (command.ContextManifestId is { } manifestId)
        {
            var manifest = await db.GccV2RunContextManifests.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == manifestId && x.OwnerUserId == command.OwnerUserId, ct);
            if (manifest is null
                || !string.Equals(manifest.Sha256, command.ContextManifestDigest, StringComparison.Ordinal))
                return Conflict("Context manifest must exist, belong to the owner, and match its digest.");
        }

        GccV2TaskRun? retry = null;
        if (command.RetryOfRunId is { } retryId)
        {
            retry = await db.GccV2TaskRuns.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == retryId && x.OwnerUserId == command.OwnerUserId, ct);
            if (retry is null || !Terminal.Contains(retry.Status)
                || retry.TaskAgentVersionId != command.TaskAgentVersionId)
                return Conflict("Retry lineage must reference an owner-scoped terminal run with the same version pin.");
        }
        var run = new GccV2TaskRun
        {
            OwnerUserId = command.OwnerUserId,
            TaskAgentDefinitionId = command.TaskAgentDefinitionId,
            TaskAgentVersionId = command.TaskAgentVersionId,
            TaskAgentVersionDigest = version.VersionDigest,
            InputJson = input,
            InputDigest = command.InputDigest,
            ContextManifestId = command.ContextManifestId,
            ContextManifestDigest = command.ContextManifestDigest,
            ModelSnapshotJson = model,
            ModelSnapshotDigest = command.ModelSnapshotDigest,
            BudgetSnapshotJson = budget,
            BudgetSnapshotDigest = command.BudgetSnapshotDigest,
            SourceSnapshotJson = source,
            SourceSnapshotDigest = command.SourceSnapshotDigest,
            RetryOfRunId = retry?.Id,
            CreatedByActor = command.Actor,
            LastActor = command.Actor,
        };
        run.RootRunId = retry?.RootRunId ?? run.Id;
        run.Events.Add(new GccV2TaskRunEvent
        {
            Seq = 1,
            Type = retry is null ? "queued" : "retry-queued",
            Actor = command.Actor,
            PayloadJson = JsonSerializer.Serialize(new { run.TaskAgentVersionId, run.RetryOfRunId }),
        });
        db.Add(run);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { runId = run.Id, ownerUserId = run.OwnerUserId }, run);
    }

    [HttpPost("claim-next")]
    public async Task<ActionResult<GccV2TaskRun>> ClaimNext(
        [FromQuery] string instanceId, [FromQuery] int leaseSeconds = 120,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var candidateIds = await db.GccV2TaskRuns.AsNoTracking()
            .Where(x => x.Status == "queued"
                || (x.Status == "running" && x.LeaseUntilUtc != null && x.LeaseUntilUtc < now))
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.Id)
            .Take(10)
            .ToListAsync(ct);
        foreach (var candidateId in candidateIds)
        {
            var claimed = await Claim(candidateId, instanceId, leaseSeconds, ct);
            if (claimed.Result is OkObjectResult) return claimed;
        }
        return NoContent();
    }

    [HttpPost("{runId:guid}/claim")]
    public async Task<ActionResult<GccV2TaskRun>> Claim(
        Guid runId, [FromQuery] string instanceId, [FromQuery] int leaseSeconds = 120,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return BadRequest("instanceId is required.");
        var now = DateTimeOffset.UtcNow;
        var lease = now.AddSeconds(Math.Max(10, leaseSeconds));
        if (!db.Database.IsRelational())
        {
            var run = await db.GccV2TaskRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
            if (run is null) return NotFound();
            if (run.Status != "queued" && !(run.Status == "running" && run.LeaseUntilUtc < now))
                return Conflict(new { claimed = false });
            if (run.Status == "running") run.RecoveryCount++;
            ApplyClaim(run, instanceId, now, lease);
            await AppendEvent(run, run.RecoveryCount > 0 ? "recovered" : "claimed", instanceId, "{}", ct);
            return Ok(run);
        }
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE content_creator_v2.gcc_v2_task_runs
            SET ""Status"" = 'running', ""Phase"" = 'starting',
                ""ClaimedByInstanceId"" = {instanceId}, ""ClaimedAtUtc"" = {now},
                ""LeaseUntilUtc"" = {lease}, ""UpdatedAtUtc"" = {now},
                ""AttemptCount"" = ""AttemptCount"" + 1,
                ""RecoveryCount"" = ""RecoveryCount"" + CASE WHEN ""Status"" = 'running' THEN 1 ELSE 0 END
            WHERE ""Id"" = {runId}
              AND (""Status"" = 'queued'
                   OR (""Status"" = 'running' AND ""LeaseUntilUtc"" IS NOT NULL AND ""LeaseUntilUtc"" < {now}))", ct);
        if (rows == 0)
            return await db.GccV2TaskRuns.AnyAsync(x => x.Id == runId, ct)
                ? Conflict(new { claimed = false }) : NotFound();
        var claimed = await db.GccV2TaskRuns.SingleAsync(x => x.Id == runId, ct);
        await AppendEvent(claimed, claimed.RecoveryCount > 0 ? "recovered" : "claimed", instanceId, "{}", ct);
        return Ok(claimed);
    }

    [HttpPost("{runId:guid}/transition")]
    public async Task<ActionResult<GccV2TaskRun>> Transition(
        Guid runId, TransitionRunCommand command, CancellationToken ct)
    {
        var run = await db.GccV2TaskRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null) return NotFound();
        if (Terminal.Contains(run.Status)) return Conflict("Task run is terminal.");
        if (!States.Contains(command.Status)) return BadRequest("Invalid task run status.");
        if (command.Status == "queued" || (command.Status == "running" && run.Status != "running"))
            return Conflict("Transition cannot requeue or claim a run.");
        if (!string.IsNullOrWhiteSpace(command.ExpectedClaimedBy)
            && !string.Equals(run.ClaimedByInstanceId, command.ExpectedClaimedBy, StringComparison.Ordinal))
            return Conflict("Run lease is owned by another instance.");
        run.Status = command.Status;
        run.Phase = string.IsNullOrWhiteSpace(command.Phase) ? run.Phase : command.Phase.Trim();
        run.ProgressPercent = Math.Clamp(command.ProgressPercent, 0, 100);
        run.LastActor = command.Actor;
        run.UpdatedAtUtc = DateTimeOffset.UtcNow;
        run.TerminalError = command.TerminalError;
        if (command.CancellationRequested && run.CancellationRequestedAtUtc is null)
            run.CancellationRequestedAtUtc = run.UpdatedAtUtc;
        if (Terminal.Contains(run.Status))
        {
            run.CompletedAtUtc = run.UpdatedAtUtc;
            run.ProgressPercent = 100;
            run.LeaseUntilUtc = null;
            run.ClaimedByInstanceId = null;
            if (run.Status == "cancelled") run.CancelledAtUtc = run.UpdatedAtUtc;
        }
        else if (command.LeaseUntilUtc is not null) run.LeaseUntilUtc = command.LeaseUntilUtc;
        await AppendEvent(run, command.EventType, command.Actor, command.EventPayloadJson, ct);
        return Ok(run);
    }

    [HttpGet("{runId:guid}/events")]
    public async Task<ActionResult<IReadOnlyList<GccV2TaskRunEvent>>> Events(
        Guid runId, [FromQuery] int afterSeq = 0, CancellationToken ct = default) =>
        Ok(await db.GccV2TaskRunEvents.AsNoTracking()
            .Where(x => x.RunId == runId && x.Seq > afterSeq).OrderBy(x => x.Seq).ToListAsync(ct));

    [HttpPost("{runId:guid}/events")]
    public async Task<ActionResult<GccV2TaskRunEvent>> AddEvent(
        Guid runId, AddEventCommand command, CancellationToken ct)
    {
        var run = await db.GccV2TaskRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null) return NotFound();
        var evt = await AppendEvent(run, command.Type, command.Actor, command.PayloadJson, ct);
        return CreatedAtAction(nameof(Events), new { runId }, evt);
    }

    [HttpPost("{runId:guid}/artifacts")]
    public async Task<ActionResult<GccV2TaskArtifact>> AddArtifact(
        Guid runId, CreateArtifactCommand command, CancellationToken ct)
    {
        var run = await db.GccV2TaskRuns.SingleOrDefaultAsync(
            x => x.Id == runId && x.OwnerUserId == command.OwnerUserId, ct);
        if (run is null) return NotFound();
        if (run.Status != "running" || run.CancellationRequestedAtUtc is not null)
            return Conflict("Artifacts can only be written while the task run is actively running.");
        if (!string.IsNullOrWhiteSpace(command.ExpectedClaimedBy)
            && !string.Equals(run.ClaimedByInstanceId, command.ExpectedClaimedBy, StringComparison.Ordinal))
            return Conflict("Task run lease is owned by another instance.");
        if (!ValidArtifactType(command.ArtifactType))
            return BadRequest("artifactType must be a versioned identifier such as gapReport.v1.");
        var artifact = new GccV2TaskArtifact
        {
            OwnerUserId = command.OwnerUserId,
            RunId = runId,
            ArtifactType = command.ArtifactType.Trim(),
        };
        db.Add(artifact);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { runId, ownerUserId = command.OwnerUserId }, artifact);
    }

    [HttpPost("artifacts/{artifactId:guid}/versions")]
    public async Task<ActionResult<GccV2TaskArtifactVersion>> AddArtifactVersion(
        Guid artifactId, CreateArtifactVersionCommand command, CancellationToken ct)
    {
        var artifact = await db.GccV2TaskArtifacts.Include(x => x.Versions).Include(x => x.Run)
            .SingleOrDefaultAsync(x => x.Id == artifactId && x.OwnerUserId == command.OwnerUserId, ct);
        if (artifact is null) return NotFound();
        if (artifact.Run.Status != "running" || artifact.Run.CancellationRequestedAtUtc is not null)
            return Conflict("Artifact versions can only be written while the task run is actively running.");
        if (!string.IsNullOrWhiteSpace(command.ExpectedClaimedBy)
            && !string.Equals(artifact.Run.ClaimedByInstanceId, command.ExpectedClaimedBy, StringComparison.Ordinal))
            return Conflict("Task run lease is owned by another instance.");
        if (command.ValidationState is not ("pending" or "valid" or "invalid"))
            return BadRequest("validationState must be pending, valid, or invalid.");
        string payload, evidence, citations, validation;
        try
        {
            payload = GccV2TaskAgentsController.Canonical(command.PayloadJson, JsonValueKind.Object);
            evidence = GccV2TaskAgentsController.Canonical(command.EvidenceJson, JsonValueKind.Array);
            citations = GccV2TaskAgentsController.Canonical(command.CitationsJson, JsonValueKind.Array);
            validation = GccV2TaskAgentsController.Canonical(command.ValidationJson, JsonValueKind.Object);
        }
        catch (JsonException ex) { return BadRequest(ex.Message); }
        var parentIds = command.ParentArtifactVersionIds.Distinct().ToList();
        if (parentIds.Contains(command.Id ?? Guid.Empty)) return BadRequest("An artifact version cannot parent itself.");
        var parents = await db.GccV2TaskArtifactVersions.AsNoTracking()
            .Where(x => parentIds.Contains(x.Id) && x.Artifact.OwnerUserId == command.OwnerUserId)
            .Select(x => x.Id).ToListAsync(ct);
        if (parents.Count != parentIds.Count) return Conflict("Every parent version must exist and belong to the owner.");
        var versionNumber = artifact.Versions.Count == 0 ? 1 : artifact.Versions.Max(x => x.VersionNumber) + 1;
        var versionId = command.Id ?? Guid.NewGuid();
        var digest = GccV2TaskAgentsController.Hash(GccV2TaskAgentsController.Canonical(
            JsonSerializer.Serialize(new
            {
                artifactId,
                versionNumber,
                artifact.ArtifactType,
                payload,
                evidence,
                citations,
                validationState = command.ValidationState,
                validation,
                parentIds = parentIds.Order(),
            }), JsonValueKind.Object));
        var version = new GccV2TaskArtifactVersion
        {
            Id = versionId,
            ArtifactId = artifactId,
            VersionNumber = versionNumber,
            PayloadJson = payload,
            EvidenceJson = evidence,
            CitationsJson = citations,
            ValidationState = command.ValidationState,
            ValidationJson = validation,
            Digest = digest,
            CreatedByActor = command.Actor,
            Parents = parentIds.Select(id => new GccV2TaskArtifactLineage
            {
                ParentArtifactVersionId = id,
                ChildArtifactVersionId = versionId,
                Relationship = string.IsNullOrWhiteSpace(command.Relationship)
                    ? "derived-from" : command.Relationship.Trim(),
            }).ToList(),
        };
        db.Add(version);
        artifact.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { runId = artifact.RunId, ownerUserId = command.OwnerUserId }, version);
    }

    private async Task<GccV2TaskRunEvent> AppendEvent(
        GccV2TaskRun run, string type, string actor, string? payloadJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(type)) throw new InvalidOperationException("Event type is required.");
        var payload = GccV2TaskAgentsController.Canonical(payloadJson ?? "{}", JsonValueKind.Object);
        var seq = (await db.GccV2TaskRunEvents.Where(x => x.RunId == run.Id)
            .Select(x => (int?)x.Seq).MaxAsync(ct) ?? 0) + 1;
        var evt = new GccV2TaskRunEvent
        { RunId = run.Id, Seq = seq, Type = type.Trim(), Actor = actor, PayloadJson = payload };
        db.Add(evt);
        await db.SaveChangesAsync(ct);
        return evt;
    }

    private static bool DigestMatches(string canonical, string supplied) =>
        string.Equals(GccV2TaskAgentsController.Hash(canonical), supplied, StringComparison.Ordinal);
    private static bool ValidArtifactType(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        var marker = value.LastIndexOf(".v", StringComparison.Ordinal);
        return marker > 0 && int.TryParse(value[(marker + 2)..], out var version) && version > 0
            && value[..marker].All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_');
    }

    private static void ApplyClaim(
        GccV2TaskRun run, string instanceId, DateTimeOffset now, DateTimeOffset lease)
    {
        run.Status = "running";
        run.Phase = "starting";
        run.ClaimedByInstanceId = instanceId;
        run.ClaimedAtUtc = now;
        run.LeaseUntilUtc = lease;
        run.AttemptCount++;
        run.UpdatedAtUtc = now;
        run.LastActor = instanceId;
    }

    public sealed record CreateRunCommand(
        string OwnerUserId, Guid TaskAgentDefinitionId, Guid TaskAgentVersionId,
        string TaskAgentVersionDigest, string InputJson, string InputDigest,
        Guid? ContextManifestId, string? ContextManifestDigest,
        string ModelSnapshotJson, string ModelSnapshotDigest,
        string BudgetSnapshotJson, string BudgetSnapshotDigest,
        string SourceSnapshotJson, string SourceSnapshotDigest,
        Guid? RetryOfRunId, string Actor);
    public sealed record TransitionRunCommand(
        string Status, string Phase, int ProgressPercent, string EventType,
        string? EventPayloadJson, string Actor, string? ExpectedClaimedBy,
        DateTimeOffset? LeaseUntilUtc, string? TerminalError, bool CancellationRequested = false);
    public sealed record AddEventCommand(string Type, string? PayloadJson, string Actor);
    public sealed record CreateArtifactCommand(
        string OwnerUserId, string ArtifactType, string Actor, string? ExpectedClaimedBy = null);
    public sealed record CreateArtifactVersionCommand(
        string OwnerUserId, string PayloadJson, string EvidenceJson, string CitationsJson,
        string ValidationState, string ValidationJson, IReadOnlyList<Guid> ParentArtifactVersionIds,
        string Actor, string? Relationship = null, Guid? Id = null, string? ExpectedClaimedBy = null);
}
