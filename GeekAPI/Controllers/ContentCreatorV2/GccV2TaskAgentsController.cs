using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Owner-scoped task-agent catalog and durable run/result shell.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/task-agents")]
public sealed class GccV2TaskAgentsController(
    ICurrentUserContext user,
    GccV2SkillAdminPolicy admin,
    HttpGccV2Repository repo) : ControllerBase
{
    private string Owner => user.UserId.ToString("D");

    [HttpGet]
    public async Task<ActionResult<object>> Catalog(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definitions = await repo.ListTaskAgentsAsync("published", ct);
        return Ok(new
        {
            contractVersion = "gcc-task-agent-catalog.v1",
            agents = definitions
                .Select(x => (definition: x, version: LatestPublished(x)))
                .Where(x => x.version is not null
                    && GccV2StudioTemplateRenderer.IsVisibleInCatalog(x.version!, Owner))
                .Select(x => Summary(x.definition, x.version)),
        });
    }

    [HttpGet("{idOrCapability}")]
    public async Task<ActionResult<object>> Detail(string idOrCapability, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        var version = LatestPublished(definition);
        if (version is null) return NotFound();
        if (!GccV2StudioTemplateRenderer.CanAccessPublishedStudio(version, Owner))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Private Studio agents are owner-only." });
        return Ok(Detail(definition, version));
    }

    [HttpPost("{idOrCapability}/runs")]
    public async Task<ActionResult<object>> Run(
        string idOrCapability, CreateRunRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        var version = request.VersionId is { } versionId
            ? definition.Versions.SingleOrDefault(x => x.Id == versionId && x.State == "published")
            : LatestPublished(definition);
        if (version is null) return Conflict(new { error = "A published task-agent version is required." });
        if (!GccV2StudioTemplateRenderer.CanAccessPublishedStudio(version, Owner))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Private Studio agents are owner-only." });
        if (request.Input.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "input must be a JSON object." });
        var validationErrors = GccV2TaskInputSchemaValidator.Validate(request.Input, version.InputSchemaJson);
        if (validationErrors.Count > 0) return BadRequest(new { error = "Input schema validation failed.", validationErrors });

        var input = GccV2CanonicalJson.Serialize(request.Input);
        var model = CanonicalObject(request.ModelSnapshot, new
        {
            policyVersion = "task-agent-model-policy.v1",
            allowedModels = JsonSerializer.Deserialize<JsonElement>(version.AllowedModelsJson),
        });
        var budget = CanonicalObject(request.BudgetSnapshot, new { maxTokens = 0, maxCostUsd = 0m });
        var source = CanonicalObject(request.SourceSnapshot, new
        {
            capturedAtUtc = DateTimeOffset.UtcNow,
            contextManifestId = request.ContextManifestId,
        });
        var run = await repo.CreateTaskRunAsync(new(
            Owner, definition.Id, version.Id, version.VersionDigest,
            input, GccV2CanonicalJson.Sha256(input),
            request.ContextManifestId, request.ContextManifestDigest,
            model, GccV2CanonicalJson.Sha256(model),
            budget, GccV2CanonicalJson.Sha256(budget),
            source, GccV2CanonicalJson.Sha256(source),
            request.RetryOfRunId, Owner), ct);
        return AcceptedAtAction(nameof(GetRun), new { runId = run.Id }, new
        {
            contractVersion = "gcc-task-run.v1",
            run.Id,
            run.Status,
            run.Phase,
            run.ProgressPercent,
            taskAgent = new { definition.CapabilityId, definition.Id, versionId = version.Id, version.VersionDigest },
        });
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<ActionResult<GccV2TaskRunDto>> GetRun(Guid runId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var run = await repo.GetTaskRunAsync(runId, Owner, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<GccV2TaskRunDto>>> ListRuns(
        [FromQuery] string? status,
        CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        return Ok(await repo.ListTaskRunsAsync(Owner, status, ct));
    }

    [HttpGet("runs/{runId:guid}/result")]
    public async Task<ActionResult<object>> Result(Guid runId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var run = await repo.GetTaskRunAsync(runId, Owner, ct);
        if (run is null) return NotFound();
        var definition = await repo.GetTaskAgentAsync(run.TaskAgentDefinitionId.ToString("D"), ct);
        var version = definition?.Versions.SingleOrDefault(x => x.Id == run.TaskAgentVersionId);
        if (definition is null || version is null) return Problem("Pinned task-agent contract is unavailable.");
        return Ok(new
        {
            contractVersion = "gcc-task-result-shell.v1",
            identity = new { definition.CapabilityId, definition.DisplayName, objective = definition.Description },
            taskInputs = JsonSerializer.Deserialize<JsonElement>(run.InputJson),
            sharedContext = new { run.ContextManifestId, run.ContextManifestDigest },
            sourceReadinessAndProvenance = JsonSerializer.Deserialize<JsonElement>(run.SourceSnapshotJson),
            progress = new { run.Status, run.Phase, run.ProgressPercent, events = run.Events ?? [] },
            renderer = JsonSerializer.Deserialize<JsonElement>(version.ResultRendererJson),
            findingsAndEvidence = (run.Artifacts ?? []).SelectMany(x => x.Versions).Select(x => new
            {
                artifactVersionId = x.Id,
                evidence = JsonSerializer.Deserialize<JsonElement>(x.EvidenceJson),
                citations = JsonSerializer.Deserialize<JsonElement>(x.CitationsJson),
                x.ValidationState,
            }),
            artifacts = run.Artifacts ?? [],
            snapshot = new
            {
                run.TaskAgentVersionId,
                run.TaskAgentVersionDigest,
                run.InputDigest,
                run.ModelSnapshotDigest,
                run.BudgetSnapshotDigest,
                run.SourceSnapshotDigest,
                run.RootRunId,
                run.RetryOfRunId,
            },
            rerun = new { capabilityId = definition.CapabilityId, versionId = version.Id, retryOfRunId = run.Id },
            compatibleNextActions = JsonSerializer.Deserialize<JsonElement>(version.CompatibleArtifactTypesJson),
        });
    }

    [HttpPost("runs/{runId:guid}/cancel")]
    public async Task<ActionResult<object>> Cancel(Guid runId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetTaskRunAsync(runId, Owner, ct);
        if (existing is null) return NotFound();
        if (existing.Status is "succeeded" or "failed" or "cancelled")
            return Conflict(new { error = "Task run is already terminal." });
        var cancelled = await repo.TransitionTaskRunAsync(runId, new(
            "cancelled", "cancelled", 100, "cancelled",
            GccV2CanonicalJson.Serialize(new { requestedBy = Owner }), Owner,
            null, null, null, true), ct);
        return Ok(cancelled);
    }

    [HttpGet("admin")]
    public async Task<ActionResult<IReadOnlyList<GccV2TaskAgentDefinitionDto>>> Admin(CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.ListTaskAgentsAsync(ct: ct));
    }

    [HttpPost("admin")]
    public async Task<ActionResult<GccV2TaskAgentDefinitionDto>> CreateDefinition(
        CreateDefinitionRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.CreateTaskAgentAsync(new(
            request.CapabilityId, request.DisplayName, request.Description, Owner), ct));
    }

    [HttpPatch("admin/{definitionId:guid}")]
    public async Task<ActionResult<GccV2TaskAgentDefinitionDto>> PatchDefinition(
        Guid definitionId, PatchDefinitionRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.PatchTaskAgentAsync(definitionId, new(
            request.DisplayName, request.Description, Owner), ct));
    }

    [HttpPost("admin/{definitionId:guid}/versions")]
    public async Task<ActionResult<GccV2TaskAgentVersionDto>> CreateVersion(
        Guid definitionId, CreateVersionRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        if (request.InputSchema.ValueKind != JsonValueKind.Object
            || request.OutputSchema.ValueKind != JsonValueKind.Object
            || request.Workflow.ValueKind != JsonValueKind.Object
            || request.ContextPolicy.ValueKind != JsonValueKind.Object
            || request.ResultRenderer.ValueKind != JsonValueKind.Object
            || request.CompatibleArtifactTypes.ValueKind != JsonValueKind.Object
            || request.EvaluationThresholds.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "Contract schemas and policies must be JSON objects." });
        return Ok(await repo.CreateTaskAgentVersionAsync(definitionId, new(
            request.SemanticVersion, request.WorkflowGroup,
            CanonicalOrDefault(request.Facets, "{}"), GccV2CanonicalJson.Serialize(request.InputSchema),
            GccV2CanonicalJson.Serialize(request.OutputSchema), GccV2CanonicalJson.Serialize(request.Workflow),
            GccV2CanonicalJson.Serialize(request.ContextPolicy), GccV2CanonicalJson.Serialize(request.ResultRenderer),
            GccV2CanonicalJson.Serialize(request.CompatibleArtifactTypes),
            CanonicalOrDefault(request.AllowedTools, "[]"), CanonicalOrDefault(request.AllowedModels, "[]"),
            CanonicalOrDefault(request.SkillVersionIds, "[]"),
            GccV2CanonicalJson.Serialize(request.EvaluationThresholds), Owner), ct));
    }

    [HttpPost("admin/versions/{versionId:guid}/{transition:regex(^publish|deprecate|revoke$)}")]
    public async Task<ActionResult<GccV2TaskAgentVersionDto>> TransitionVersion(
        Guid versionId, string transition, TransitionRequest? request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.TransitionTaskAgentVersionAsync(
            versionId, transition, new(Owner, request?.Reason), ct));
    }

    private static object? Summary(
        GccV2TaskAgentDefinitionDto definition, GccV2TaskAgentVersionDto? version) =>
        version is null ? null : new
        {
            id = definition.CapabilityId,
            definitionId = definition.Id,
            definition.DisplayName,
            definition.Description,
            versionId = version.Id,
            version = version.SemanticVersion,
            digest = version.VersionDigest,
            version.WorkflowGroup,
            facets = JsonSerializer.Deserialize<JsonElement>(version.FacetsJson),
        };

    private static object Detail(GccV2TaskAgentDefinitionDto definition, GccV2TaskAgentVersionDto version) => new
    {
        contractVersion = "gcc-task-agent-detail.v1",
        agent = Summary(definition, version),
        inputSchema = JsonSerializer.Deserialize<JsonElement>(version.InputSchemaJson),
        outputSchema = JsonSerializer.Deserialize<JsonElement>(version.OutputSchemaJson),
        workflow = JsonSerializer.Deserialize<JsonElement>(version.WorkflowJson),
        contextPolicy = JsonSerializer.Deserialize<JsonElement>(version.ContextPolicyJson),
        resultRenderer = JsonSerializer.Deserialize<JsonElement>(version.ResultRendererJson),
        compatibleArtifactTypes = JsonSerializer.Deserialize<JsonElement>(version.CompatibleArtifactTypesJson),
        evaluationThresholds = JsonSerializer.Deserialize<JsonElement>(version.EvaluationThresholdsJson),
        digests = new
        {
            version.InputSchemaDigest,
            version.OutputSchemaDigest,
            version.WorkflowDigest,
            version.ContextPolicyDigest,
            version.ResultRendererDigest,
            version.EvaluationThresholdsDigest,
            version.VersionDigest,
        },
    };

    private static GccV2TaskAgentVersionDto? LatestPublished(GccV2TaskAgentDefinitionDto definition) =>
        definition.Versions.Where(x => x.State == "published")
            .OrderByDescending(x => Version.TryParse(x.SemanticVersion, out var parsed) ? parsed : new Version())
            .FirstOrDefault();
    private static string CanonicalObject(JsonElement? element, object fallback) =>
        element is { ValueKind: JsonValueKind.Object } value
            ? GccV2CanonicalJson.Serialize(value)
            : GccV2CanonicalJson.Serialize(fallback);
    private static string CanonicalOrDefault(JsonElement element, string fallback) =>
        element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? fallback : GccV2CanonicalJson.Serialize(element);
    private bool IsAdmin(out ActionResult denied)
    {
        denied = user.IsAuthenticated
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator authorization is required." })
            : Unauthorized();
        return admin.IsAuthorized(user);
    }

    public sealed record CreateRunRequest(
        JsonElement Input, Guid? VersionId = null, Guid? ContextManifestId = null,
        string? ContextManifestDigest = null, JsonElement? ModelSnapshot = null,
        JsonElement? BudgetSnapshot = null, JsonElement? SourceSnapshot = null,
        Guid? RetryOfRunId = null);
    public sealed record CreateDefinitionRequest(string CapabilityId, string DisplayName, string Description);
    public sealed record PatchDefinitionRequest(string? DisplayName, string? Description);
    public sealed record CreateVersionRequest(
        string SemanticVersion, string WorkflowGroup, JsonElement Facets, JsonElement InputSchema,
        JsonElement OutputSchema, JsonElement Workflow, JsonElement ContextPolicy,
        JsonElement ResultRenderer, JsonElement CompatibleArtifactTypes, JsonElement AllowedTools,
        JsonElement AllowedModels, JsonElement SkillVersionIds, JsonElement EvaluationThresholds);
    public sealed record TransitionRequest(string? Reason);
}

internal static class GccV2TaskInputSchemaValidator
{
    public static IReadOnlyList<string> Validate(JsonElement input, string schemaJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        var errors = new List<string>();
        ValidateValue(input, schema.RootElement, "$", errors);
        return errors;
    }

    private static void ValidateValue(
        JsonElement value, JsonElement schema, string path, List<string> errors)
    {
        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && !Matches(value, type.GetString()!))
            errors.Add($"{path} must be {type.GetString()}.");
        if (value.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var item in required.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && !value.TryGetProperty(item.GetString()!, out _))
                    errors.Add($"{path}.{item.GetString()} is required.");
        var hasProperties = schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object;
        if (hasProperties)
            foreach (var property in value.EnumerateObject())
                if (properties.TryGetProperty(property.Name, out var propertySchema))
                    ValidateValue(property.Value, propertySchema, $"{path}.{property.Name}", errors);
                else if (schema.TryGetProperty("additionalProperties", out var additional)
                    && additional.ValueKind == JsonValueKind.False)
                    errors.Add($"{path}.{property.Name} is not allowed.");
    }

    private static bool Matches(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };
}
