using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Pipelines;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Geek Content Pipelines — Plan → Create → Adapt → Activate → Optimize.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/pipelines")]
public sealed class GccV2PipelinesController(
    ICurrentUserContext user,
    HttpGccV2Repository repo) : ControllerBase
{
    private const string ContractVersion = "gcc-content-pipelines.v1";

    private string Owner => user.UserId.ToString("D");

    [HttpGet]
    public async Task<ActionResult<object>> List(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var items = await repo.ListPipelinesAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            lifecycleStages = GccV2PipelineStagePolicy.LifecycleStages,
            pipelines = items.Select(Summary),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var pipeline = await repo.GetPipelineAsync(id, Owner, ct);
        if (pipeline is null) return NotFound();
        return Ok(new
        {
            contractVersion = ContractVersion,
            lifecycleStages = GccV2PipelineStagePolicy.LifecycleStages,
            pipeline = Detail(pipeline),
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(
        [FromBody] CreatePublicPipelineRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var useTemplate = request.UseAeoTemplate != false;
        var stagesJson = useTemplate
            ? GccV2PipelineStagePolicy.DefaultAeoTemplateStagesJson()
            : (request.StagesJson ?? "[]");
        var validation = GccV2PipelineStagePolicy.ValidateStagesJson(stagesJson, out _);
        if (validation is not null) return BadRequest(new { error = validation });

        var policyJson = string.IsNullOrWhiteSpace(request.PolicyJson) ? "{}" : request.PolicyJson;
        var digest = GccV2PipelineStagePolicy.Digest(stagesJson, policyJson);
        var name = string.IsNullOrWhiteSpace(request.Name)
            ? "AEO content pipeline"
            : request.Name.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description)
            ? "Plan → Create → Adapt → Activate → Optimize using Query Planner, FAQ Generator, Canvas, publish, and AI Readiness."
            : request.Description.Trim();

        var pipeline = await repo.CreatePipelineAsync(new(
            Owner, name, description, stagesJson, digest, policyJson, request.Publish != false), ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            pipeline = Detail(pipeline),
        });
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<object>> Publish(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var pipeline = await repo.PublishPipelineAsync(id, new(Owner, Owner), ct);
        return Ok(new { contractVersion = ContractVersion, pipeline = Detail(pipeline) });
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<ActionResult<object>> StartRun(
        Guid id, [FromBody] StartPublicPipelineRunRequest? request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var inputJson = request?.Input is null
            ? "{}"
            : JsonSerializer.Serialize(request.Input);
        IReadOnlyList<string>? workItemInputsJson = null;
        if (request?.WorkItems is { Count: > 0 })
        {
            workItemInputsJson = request.WorkItems
                .Select(item => JsonSerializer.Serialize(item))
                .ToArray();
        }
        var pipeline = await repo.StartPipelineRunAsync(id, new(
            Owner, Owner, inputJson, request?.FailStageKey, workItemInputsJson), ct);
        return Ok(new { contractVersion = ContractVersion, pipeline = Detail(pipeline) });
    }

    [HttpPost("runs/{runId:guid}/{action:regex(^pause|resume|cancel$)}")]
    public async Task<ActionResult<object>> TransitionRun(
        Guid runId, string action, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var pipeline = await repo.TransitionPipelineRunAsync(runId, action, new(Owner, Owner), ct);
        return Ok(new { contractVersion = ContractVersion, pipeline = Detail(pipeline) });
    }

    private static object Summary(GccV2PipelineListItemDto item) => new
    {
        id = item.Id,
        name = item.Name,
        description = item.Description,
        status = item.Status,
        versionNumber = item.VersionNumber,
        digest = item.Digest,
        updatedAtUtc = item.UpdatedAtUtc,
        runCount = item.RunCount,
    };

    private static object Detail(GccV2PipelineDto pipeline)
    {
        using var stagesDoc = JsonDocument.Parse(
            string.IsNullOrWhiteSpace(pipeline.StagesJson) ? "[]" : pipeline.StagesJson);
        return new
        {
            id = pipeline.Id,
            name = pipeline.Name,
            description = pipeline.Description,
            status = pipeline.Status,
            versionNumber = pipeline.VersionNumber,
            digest = pipeline.Digest,
            stages = stagesDoc.RootElement.Clone(),
            policyJson = pipeline.PolicyJson,
            createdAtUtc = pipeline.CreatedAtUtc,
            updatedAtUtc = pipeline.UpdatedAtUtc,
            runs = pipeline.Runs.Select(run => new
            {
                id = run.Id,
                definitionVersionNumber = run.DefinitionVersionNumber,
                definitionDigest = run.DefinitionDigest,
                status = run.Status,
                actorUserId = run.ActorUserId,
                startedAtUtc = run.StartedAtUtc,
                completedAtUtc = run.CompletedAtUtc,
                pausedAtUtc = run.PausedAtUtc,
                error = run.Error,
                history = ParseJson(run.HistoryJson),
                workItems = run.WorkItems.Select(item => new
                {
                    id = item.Id,
                    workItemIndex = item.WorkItemIndex,
                    status = item.Status,
                    error = item.Error,
                    updatedAtUtc = item.UpdatedAtUtc,
                    stageAttempts = item.StageAttempts.Select(attempt => new
                    {
                        id = attempt.Id,
                        stageKey = attempt.StageKey,
                        lifecycleStage = attempt.LifecycleStage,
                        kind = attempt.Kind,
                        displayName = attempt.DisplayName,
                        capabilityId = attempt.CapabilityId,
                        handoff = attempt.Handoff,
                        attemptNumber = attempt.AttemptNumber,
                        status = attempt.Status,
                        output = ParseJson(attempt.OutputJson),
                        error = attempt.Error,
                        startedAtUtc = attempt.StartedAtUtc,
                        completedAtUtc = attempt.CompletedAtUtc,
                    }),
                }),
            }),
        };
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    public sealed record CreatePublicPipelineRequest(
        string? Name = null,
        string? Description = null,
        bool? UseAeoTemplate = true,
        string? StagesJson = null,
        string? PolicyJson = null,
        bool? Publish = true);

    public sealed record StartPublicPipelineRunRequest(
        JsonElement? Input = null,
        string? FailStageKey = null,
        IReadOnlyList<JsonElement>? WorkItems = null);
}
