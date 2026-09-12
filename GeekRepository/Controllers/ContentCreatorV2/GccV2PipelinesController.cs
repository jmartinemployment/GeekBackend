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
        var definition = await LoadOwned(id, ownerUserId, ct);
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

        var runsController = new GccV2TaskRunsController(db);
        var history = new List<object>();
        var anyFailed = false;
        Guid? canvasProjectId = null;
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
                else if (stage.Kind == "task-agent"
                    && string.Equals(stage.CapabilityId, "roi-business-calculator", StringComparison.Ordinal))
                {
                    attempt.Status = "succeeded";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.OutputJson = GccV2PipelineStageOutputs.ForRoiProjection(index, stage.Lifecycle);
                }
                else if (stage.Kind == "task-agent")
                {
                    var agent = await ResolvePublishedAgentAsync(stage.CapabilityId, ct);
                    if (agent is null)
                    {
                        attempt.Status = "failed";
                        attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                        attempt.Error = $"No published task agent for capability '{stage.CapabilityId}'.";
                        failed = true;
                    }
                    else
                    {
                        try
                        {
                            var executed = await CompleteTaskRunForStageAsync(
                                runsController,
                                agent,
                                definition.OwnerUserId,
                                workItemInputs[index],
                                run.ActorUserId,
                                run.Id,
                                stage.Key,
                                ct);
                            attempt.Status = "succeeded";
                            attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                            attempt.TaskRunId = executed.TaskRunId;
                            attempt.ArtifactVersionId = executed.ArtifactVersionId;
                            attempt.OutputJson = GccV2PipelineStageOutputs.ForTaskRun(
                                stage.CapabilityId,
                                stage.DisplayName,
                                stage.Lifecycle,
                                index,
                                executed.TaskRunId,
                                executed.ArtifactVersionId,
                                agent.ArtifactType,
                                executed.Preview,
                                executed.Payload);
                        }
                        catch (Exception ex)
                        {
                            attempt.Status = "failed";
                            attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                            attempt.Error = ex.Message;
                            failed = true;
                        }
                    }
                }
                else if (stage.Kind == "handoff"
                    && string.Equals(stage.Handoff, "canvas", StringComparison.Ordinal))
                {
                    try
                    {
                        var source = workItem.StageAttempts
                            .Where(a => a.Status == "succeeded"
                                && a.TaskRunId is not null
                                && a.ArtifactVersionId is not null)
                            .OrderByDescending(a => a.CompletedAtUtc)
                            .FirstOrDefault();
                        if (source?.TaskRunId is null || source.ArtifactVersionId is null)
                        {
                            throw new InvalidOperationException(
                                "Canvas handoff requires a prior succeeded task-agent artifact.");
                        }

                        canvasProjectId ??= await EnsurePipelineCanvasProjectAsync(
                            definition.OwnerUserId,
                            definition.Name,
                            run.Id,
                            run.ActorUserId,
                            ct);

                        var attached = await AttachArtifactToCanvasAsync(
                            canvasProjectId.Value,
                            definition.OwnerUserId,
                            run.ActorUserId,
                            source.TaskRunId.Value,
                            source.ArtifactVersionId.Value,
                            stage.DisplayName,
                            ct);

                        attempt.Status = "succeeded";
                        attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                        attempt.OutputJson = GccV2PipelineStageOutputs.ForCanvasHandoff(
                            stage.DisplayName,
                            stage.Lifecycle,
                            index,
                            canvasProjectId.Value,
                            attached.AssetId,
                            attached.AssetVersionId,
                            source.TaskRunId.Value,
                            source.ArtifactVersionId.Value,
                            attached.ArtifactType,
                            attached.Title);
                    }
                    catch (Exception ex)
                    {
                        attempt.Status = "failed";
                        attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                        attempt.Error = ex.Message;
                        failed = true;
                    }
                }
                else if (stage.Kind == "handoff"
                    && string.Equals(stage.Handoff, "publish", StringComparison.Ordinal))
                {
                    var canvasAttempt = workItem.StageAttempts
                        .LastOrDefault(a => a.Status == "succeeded"
                            && string.Equals(a.Handoff, "canvas", StringComparison.Ordinal));
                    Guid? projectId = null;
                    Guid? assetId = null;
                    string? title = null;
                    if (canvasAttempt?.OutputJson is { } canvasJson)
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(canvasJson);
                            if (doc.RootElement.TryGetProperty("projectId", out var projectEl)
                                && Guid.TryParse(projectEl.GetString(), out var parsedProject))
                                projectId = parsedProject;
                            if (doc.RootElement.TryGetProperty("assetId", out var assetEl)
                                && Guid.TryParse(assetEl.GetString(), out var parsedAsset))
                                assetId = parsedAsset;
                            if (doc.RootElement.TryGetProperty("title", out var titleEl)
                                && titleEl.ValueKind == JsonValueKind.String)
                                title = titleEl.GetString();
                        }
                        catch (JsonException)
                        {
                            // Fall through with null refs.
                        }
                    }

                    attempt.Status = "succeeded";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.OutputJson = GccV2PipelineStageOutputs.ForPublishHandoff(
                        stage.DisplayName, stage.Lifecycle, index, projectId, assetId, title);
                }
                else
                {
                    attempt.Status = "succeeded";
                    attempt.CompletedAtUtc = DateTimeOffset.UtcNow;
                    attempt.OutputJson = GccV2PipelineStageOutputs.ForHandoff(
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
                    taskRunId = attempt.TaskRunId,
                });
            }

            workItem.Status = failed ? "failed" : "succeeded";
            workItem.Error = failed ? $"Work item failed at stage '{failStageKey ?? attemptStageKey(workItem)}'." : null;
            workItem.UpdatedAtUtc = DateTimeOffset.UtcNow;
            anyFailed |= failed;
        }

        run.Status = anyFailed ? "failed" : "succeeded";
        run.Error = anyFailed ? "One or more work items failed; later stages on failed items were skipped." : null;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        run.HistoryJson = JsonSerializer.Serialize(history, JsonOpts);
        db.Add(run);
        definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(await ReloadGraph(definition.Id, definition.OwnerUserId, ct) ?? ToGraph(definition));

        static string attemptStageKey(GccV2PipelineWorkItem item) =>
            item.StageAttempts.LastOrDefault(a => a.Status == "failed")?.StageKey ?? "unknown";
    }

    private sealed record ResolvedTaskAgent(
        Guid DefinitionId, Guid VersionId, string VersionDigest,
        string CapabilityId, string Endpoint, string ArtifactType);

    private sealed record CompletedStageTaskRun(
        Guid TaskRunId, Guid ArtifactVersionId, JsonElement Payload, string Preview);

    private sealed record AttachedCanvasAsset(
        Guid AssetId, Guid AssetVersionId, string ArtifactType, string Title);

    private async Task<Guid> EnsurePipelineCanvasProjectAsync(
        string ownerUserId,
        string pipelineName,
        Guid pipelineRunId,
        string actorUserId,
        CancellationToken ct)
    {
        var canvas = new GccV2CanvasProjectsController(db);
        var created = Unwrap<GccV2CanvasProject>(
            (await canvas.Create(new(
                ownerUserId,
                $"{pipelineName} · pipeline handoffs",
                $"Assets attached from pipeline run {pipelineRunId:D}.",
                "in-progress",
                JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = Guid.NewGuid().ToString("D"),
                        kind = "handoff",
                        actor = actorUserId,
                        occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                        message = $"Created for pipeline run {pipelineRunId:D}.",
                    },
                }, JsonOpts)), ct)).Result,
            "create canvas project");
        return created.Id;
    }

    private async Task<AttachedCanvasAsset> AttachArtifactToCanvasAsync(
        Guid projectId,
        string ownerUserId,
        string actorUserId,
        Guid taskRunId,
        Guid artifactVersionId,
        string stageDisplayName,
        CancellationToken ct)
    {
        var version = await db.GccV2TaskArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .SingleOrDefaultAsync(v =>
                v.Id == artifactVersionId
                && v.Artifact.RunId == taskRunId
                && v.Artifact.OwnerUserId == ownerUserId, ct);
        if (version is null)
            throw new InvalidOperationException("Source task artifact version was not found.");

        var artifactType = version.Artifact.ArtifactType;
        var title = $"{stageDisplayName} · {artifactType}";
        var canvas = new GccV2CanvasProjectsController(db);
        var asset = Unwrap<GccV2CanvasAsset>(
            (await canvas.CreateAsset(projectId, new(ownerUserId, title, "report"), ct)).Result,
            "create canvas asset");

        var provenanceJson = JsonSerializer.Serialize(new
        {
            origin = "pipeline",
            note = $"Attached from pipeline stage {stageDisplayName}.",
            sourceRunId = taskRunId.ToString("D"),
            sourceArtifactVersionId = artifactVersionId.ToString("D"),
            artifactType,
            digest = version.Digest,
        }, JsonOpts);
        var summary = string.IsNullOrWhiteSpace(version.PayloadJson)
            ? $"Pipeline handoff for {artifactType}."
            : version.PayloadJson.Length > 400
                ? version.PayloadJson[..400]
                : version.PayloadJson;

        var assetVersion = Unwrap<GccV2CanvasAssetVersion>(
            (await canvas.AppendVersion(projectId, asset.Id, new(
                ownerUserId,
                actorUserId,
                "draft",
                summary,
                string.IsNullOrWhiteSpace(version.EvidenceJson) ? "[]" : version.EvidenceJson,
                provenanceJson), ct)).Result,
            "append canvas asset version");

        return new AttachedCanvasAsset(asset.Id, assetVersion.Id, artifactType, title);
    }

    private async Task<ResolvedTaskAgent?> ResolvePublishedAgentAsync(
        string? capability, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(capability)) return null;
        var needle = capability.Trim();
        var candidates = await db.GccV2TaskAgentDefinitions.AsNoTracking()
            .Include(d => d.Versions)
            .Where(d => d.LifecycleState != "revoked")
            .ToListAsync(ct);

        foreach (var definition in candidates)
        {
            var published = definition.Versions
                .Where(v => v.State == "published")
                .OrderByDescending(v => v.PublishedAtUtc ?? v.CreatedAtUtc)
                .FirstOrDefault();
            if (published is null) continue;

            string? endpoint = null;
            string artifactType = "customAgentOutput.v1";
            try
            {
                using var doc = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(published.WorkflowJson) ? "{}" : published.WorkflowJson);
                if (doc.RootElement.TryGetProperty("endpoint", out var endpointEl))
                    endpoint = endpointEl.GetString();
                if (doc.RootElement.TryGetProperty("artifactType", out var artifactEl)
                    && !string.IsNullOrWhiteSpace(artifactEl.GetString()))
                    artifactType = artifactEl.GetString()!;
            }
            catch (JsonException)
            {
                // Ignore malformed workflow; capability id match may still apply.
            }

            var capabilityMatch = string.Equals(
                definition.CapabilityId, needle, StringComparison.OrdinalIgnoreCase);
            var endpointMatch = !string.IsNullOrWhiteSpace(endpoint)
                && string.Equals(endpoint, needle, StringComparison.OrdinalIgnoreCase);
            if (!capabilityMatch && !endpointMatch) continue;

            return new ResolvedTaskAgent(
                definition.Id, published.Id, published.VersionDigest,
                definition.CapabilityId, endpoint ?? definition.CapabilityId, artifactType);
        }

        return null;
    }

    private static async Task<CompletedStageTaskRun> CompleteTaskRunForStageAsync(
        GccV2TaskRunsController runs,
        ResolvedTaskAgent agent,
        string ownerUserId,
        string inputJson,
        string actor,
        Guid pipelineRunId,
        string stageKey,
        CancellationToken ct)
    {
        var inputCanonical = GccV2TaskAgentsController.Canonical(
            string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson, JsonValueKind.Object);
        var emptyCanonical = GccV2TaskAgentsController.Canonical("{}", JsonValueKind.Object);
        var emptyDigest = GccV2TaskAgentsController.Hash(emptyCanonical);
        var inputDigest = GccV2TaskAgentsController.Hash(inputCanonical);
        var instanceId = $"pipeline-run:{pipelineRunId:N}:{stageKey}";

        var created = Unwrap<GccV2TaskRun>(
            (await runs.Create(new GccV2TaskRunsController.CreateRunCommand(
                ownerUserId, agent.DefinitionId, agent.VersionId, agent.VersionDigest,
                inputCanonical, inputDigest, null, null,
                emptyCanonical, emptyDigest, emptyCanonical, emptyDigest, emptyCanonical, emptyDigest,
                null, actor), ct)).Result,
            "create task run");

        Unwrap<GccV2TaskRun>(
            (await runs.Claim(created.Id, instanceId, 120, ct)).Result,
            "claim task run");

        var artifact = Unwrap<GccV2TaskArtifact>(
            (await runs.AddArtifact(created.Id, new GccV2TaskRunsController.CreateArtifactCommand(
                ownerUserId, agent.ArtifactType, actor, instanceId), ct)).Result,
            "create task artifact");

        var topic = ReadTopicOrFirstInput(inputJson);
        var payloadJson = BuildDeterministicArtifactPayload(agent.ArtifactType, topic);
        var payloadCanonical = GccV2TaskAgentsController.Canonical(payloadJson, JsonValueKind.Object);
        using var payloadDoc = JsonDocument.Parse(payloadCanonical);

        var version = Unwrap<GccV2TaskArtifactVersion>(
            (await runs.AddArtifactVersion(artifact.Id, new GccV2TaskRunsController.CreateArtifactVersionCommand(
                ownerUserId, payloadCanonical, "[]", "[]", "valid", "{}", [],
                actor, ExpectedClaimedBy: instanceId), ct)).Result,
            "write task artifact version");

        Unwrap<GccV2TaskRun>(
            (await runs.Transition(created.Id, new GccV2TaskRunsController.TransitionRunCommand(
                "succeeded", "complete", 100, "succeeded",
                JsonSerializer.Serialize(new
                {
                    source = "pipeline",
                    pipelineRunId,
                    stageKey,
                    artifactSource = "deterministic",
                }),
                actor, instanceId, null, null), ct)).Result,
            "succeed task run");

        return new CompletedStageTaskRun(
            created.Id,
            version.Id,
            payloadDoc.RootElement.Clone(),
            BuildPreview(agent.ArtifactType, topic, payloadDoc.RootElement));
    }

    private static string BuildDeterministicArtifactPayload(string artifactType, string topic)
    {
        if (string.Equals(artifactType, "faqSet.v1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                artifactType = "faqSet.v1",
                methodology = "pipeline-sync.v1",
                pairs = new[]
                {
                    new
                    {
                        question = topic,
                        answer = $"Grounded FAQ draft for “{topic}”.",
                    },
                },
            }, JsonOpts);
        }

        if (string.Equals(artifactType, "queryPlan.v1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                artifactType = "queryPlan.v1",
                methodology = "pipeline-sync.v1",
                topic,
                queries = new[]
                {
                    new { query = topic, intent = "informational", priority = 1 },
                    new { query = $"How does {topic} work?", intent = "informational", priority = 2 },
                },
                summary = $"Query plan for “{topic}”.",
            }, JsonOpts);
        }

        if (string.Equals(artifactType, "readinessScore.v1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                artifactType = "readinessScore.v1",
                methodology = "pipeline-sync.v1",
                topic,
                score = 78,
                summary = $"Directional AI readiness score for “{topic}”.",
                dimensions = new[]
                {
                    new { id = "answerFirst", label = "Answer-first", score = 80 },
                    new { id = "evidence", label = "Evidence density", score = 74 },
                    new { id = "structure", label = "Structure", score = 80 },
                },
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            artifactType,
            methodology = "pipeline-sync.v1",
            summary = $"Deterministic pipeline output for “{topic}”.",
            topic,
        }, JsonOpts);
    }

    private static string BuildPreview(string artifactType, string topic, JsonElement payload)
    {
        if (string.Equals(artifactType, "faqSet.v1", StringComparison.OrdinalIgnoreCase)
            && payload.TryGetProperty("pairs", out var pairs)
            && pairs.ValueKind == JsonValueKind.Array
            && pairs.GetArrayLength() > 0)
        {
            var first = pairs[0];
            if (first.TryGetProperty("question", out var q)
                && q.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(q.GetString()))
                return $"FAQ draft for: {q.GetString()}";
        }

        if (string.Equals(artifactType, "queryPlan.v1", StringComparison.OrdinalIgnoreCase))
            return $"Query plan for: {topic}";
        if (string.Equals(artifactType, "readinessScore.v1", StringComparison.OrdinalIgnoreCase))
        {
            if (payload.TryGetProperty("score", out var score) && score.TryGetInt32(out var value))
                return $"AI readiness {value} for: {topic}";
            return $"AI readiness for: {topic}";
        }

        return $"TaskRun output for: {topic}";
    }

    private static string ReadTopicOrFirstInput(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            if (doc.RootElement.TryGetProperty("topic", out var topic)
                && topic.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(topic.GetString()))
                return topic.GetString()!;
            if (doc.RootElement.TryGetProperty("query", out var query)
                && query.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(query.GetString()))
                return query.GetString()!;
        }
        catch (JsonException)
        {
            // Fall through.
        }

        return "pipeline work item";
    }

    private static T Unwrap<T>(IActionResult? result, string action)
    {
        if (result is ObjectResult { Value: T value })
            return value;
        if (result is CreatedAtActionResult { Value: T created })
            return created;
        var status = result is ObjectResult obj ? obj.StatusCode : null;
        var error = result is ObjectResult { Value: string message } ? message : result?.GetType().Name;
        throw new InvalidOperationException($"Could not {action} (HTTP {status}): {error}");
    }

    [HttpPost("runs/{runId:guid}/{transition:regex(^pause|resume|cancel$)}")]
    public async Task<ActionResult<PipelineGraph>> TransitionRun(
        Guid runId, string transition, [FromBody] ActorCommand command, CancellationToken ct)
    {
        var run = await db.GccV2PipelineRuns
            .Include(x => x.PipelineDefinition)
            .FirstOrDefaultAsync(x =>
                x.Id == runId && x.PipelineDefinition.OwnerUserId == command.OwnerUserId, ct);
        if (run is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        switch (transition)
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
        var definition = await db.GccV2PipelineDefinitions
            .Include(x => x.Runs)
                .ThenInclude(r => r.WorkItems)
                    .ThenInclude(w => w.StageAttempts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (definition is null) return null;
        definition.Runs = definition.Runs
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(20)
            .ToList();
        return definition;
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
            definition.Runs
                .OrderByDescending(r => r.StartedAtUtc)
                .Take(20)
                .Select(run => new PipelineRunDto(
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
                            attempt.CompletedAtUtc,
                            attempt.TaskRunId,
                            attempt.ArtifactVersionId)).ToList())).ToList())).ToList());

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
        string? OutputJson, string? Error, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc,
        Guid? TaskRunId = null, Guid? ArtifactVersionId = null);

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
