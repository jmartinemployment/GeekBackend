using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Owner-scoped Custom Agent Studio — durable drafts over task-agent versions.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/studio")]
public sealed class GccV2StudioController(
    ICurrentUserContext user,
    GccV2SkillAdminPolicy admin,
    HttpGccV2Repository repo) : ControllerBase
{
    private const string ArtifactType = GccV2StudioTemplateRenderer.ArtifactType;
    private string Owner => user.UserId.ToString("D");

    [HttpGet("agents")]
    public async Task<ActionResult<object>> ListAgents(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definitions = await repo.ListTaskAgentsAsync(ct: ct);
        var agents = definitions
            .Select(definition =>
            {
                if (!TrySelectStudioView(definition, Owner, out var version, out var facets))
                    return null;
                return StudioSummary(definition, version, facets);
            })
            .Where(x => x is not null)
            .ToList();
        return Ok(new
        {
            contractVersion = "gcc-studio-catalog.v1",
            agents,
        });
    }

    [HttpPost("agents")]
    public async Task<ActionResult<object>> CreateAgent(StudioCreateBody request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Outcome))
            return BadRequest(new { error = "name and outcome are required." });
        if (!IsValidVisibility(request.Visibility, out var visibility))
            return BadRequest(new { error = "visibility must be private or admin_shared." });

        var capabilityId = "studio-" + Guid.NewGuid().ToString("N")[..12];
        var draft = new StudioDraftBody(
            request.Name.Trim(),
            request.Outcome.Trim(),
            visibility,
            [],
            "You are a brand-safe marketing assistant.\nOutcome: {{outcome}}\nUse only attached approved context.",
            "{\n  \"summary\": \"…\",\n  \"sections\": []\n}",
            "gpt-5.4",
            0.2,
            EvaluationPrompt: "Output must be valid JSON matching the example shape.",
            ContextKnowledgeIds: []);

        var definition = await repo.CreateTaskAgentAsync(new(
            capabilityId, draft.Name!, draft.Outcome!, Owner), ct);
        var version = await CreateDraftVersionAsync(definition, draft, ct);
        definition = await repo.GetTaskAgentAsync(definition.Id.ToString("D"), ct) ?? definition;
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(version.FacetsJson)!;
        return CreatedAtAction(nameof(GetAgent), new { idOrCapability = definition.CapabilityId },
            StudioDetail(definition, version, facets));
    }

    [HttpGet("agents/{idOrCapability}")]
    public async Task<ActionResult<object>> GetAgent(string idOrCapability, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        if (!TrySelectStudioView(definition, Owner, out var version, out var facets))
            return NotFound();
        return Ok(StudioDetail(definition, version, facets));
    }

    [HttpPut("agents/{idOrCapability}/draft")]
    public async Task<ActionResult<object>> UpsertDraft(
        string idOrCapability, StudioDraftBody request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (!IsValidVisibility(request.Visibility, out var visibility))
            return BadRequest(new { error = "visibility must be private or admin_shared." });
        if (string.IsNullOrWhiteSpace(request.InstructionsTemplate))
            return BadRequest(new { error = "instructionsTemplate is required." });
        if (string.IsNullOrWhiteSpace(request.AllowedModel))
            return BadRequest(new { error = "allowedModel is required." });
        if (request.Fields is null)
            return BadRequest(new { error = "fields is required." });
        foreach (var field in request.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Id) || string.IsNullOrWhiteSpace(field.Label)
                || string.IsNullOrWhiteSpace(field.Type))
                return BadRequest(new { error = "Each field requires id, label, and type." });
            if (!IsValidFieldId(field.Id))
                return BadRequest(new { error = $"Invalid field id '{field.Id}'." });
        }

        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        if (!IsStudioOwned(definition, Owner))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only the Studio owner can edit drafts." });

        var normalized = request with { Visibility = visibility };
        if ((!string.IsNullOrWhiteSpace(normalized.Name) || !string.IsNullOrWhiteSpace(normalized.Outcome))
            && definition.LifecycleState is not ("published" or "deprecated" or "revoked"))
        {
            definition = await repo.PatchTaskAgentAsync(definition.Id, new(
                string.IsNullOrWhiteSpace(normalized.Name) ? null : normalized.Name.Trim(),
                string.IsNullOrWhiteSpace(normalized.Outcome) ? null : normalized.Outcome.Trim(),
                Owner), ct);
        }

        var version = await CreateDraftVersionAsync(definition, normalized, ct);
        definition = await repo.GetTaskAgentAsync(definition.Id.ToString("D"), ct) ?? definition;
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(version.FacetsJson)!;
        return Ok(StudioDetail(definition, version, facets));
    }

    [HttpPost("agents/{idOrCapability}/dry-run")]
    public async Task<ActionResult<object>> DryRun(
        string idOrCapability, StudioDryRunBody request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        if (!TrySelectStudioView(definition, Owner, out var version, out _))
            return NotFound();
        if (request.Input.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "input must be a JSON object." });

        var validationErrors = GccV2TaskInputSchemaValidator.Validate(
            request.Input, version.InputSchemaJson);
        string? exampleOutput = null;
        string instructionsTemplate = "";
        string evaluationPrompt = "";
        var knowledgeCount = 0;
        using (var workflow = JsonDocument.Parse(version.WorkflowJson))
        {
            if (workflow.RootElement.TryGetProperty("instructionsTemplate", out var template)
                && template.ValueKind == JsonValueKind.String)
                instructionsTemplate = template.GetString() ?? "";
            if (workflow.RootElement.TryGetProperty("exampleOutput", out var example)
                && example.ValueKind == JsonValueKind.String)
                exampleOutput = example.GetString();
            if (workflow.RootElement.TryGetProperty("evaluationPrompt", out var evaluation)
                && evaluation.ValueKind == JsonValueKind.String)
                evaluationPrompt = evaluation.GetString() ?? "";
            if (workflow.RootElement.TryGetProperty("contextKnowledgeIds", out var knowledge)
                && knowledge.ValueKind == JsonValueKind.Array)
                knowledgeCount = knowledge.GetArrayLength();
        }

        var render = GccV2StudioTemplateRenderer.Render(
            instructionsTemplate, definition.DisplayName, definition.Description, request.Input);
        var missingExample = string.IsNullOrWhiteSpace(exampleOutput);
        var missingEvaluation = string.IsNullOrWhiteSpace(evaluationPrompt);
        var valid = validationErrors.Count == 0
            && render.MissingTokens.Count == 0
            && !missingExample
            && !missingEvaluation;
        var message = valid
            ? knowledgeCount > 0
                ? $"Dry-run passed. Evaluation criteria on file. {knowledgeCount} knowledge attachment(s)."
                : "Dry-run passed. Evaluation criteria on file."
            : validationErrors.Count > 0
                ? $"Input schema validation failed: {string.Join(" ", validationErrors)}"
                : render.MissingTokens.Count > 0
                    ? $"Unresolved template tokens: {string.Join(", ", render.MissingTokens)}."
                    : missingExample
                        ? "Example output is required before the dry-run can pass."
                        : "Evaluation prompt is required before the dry-run can pass.";
        return Ok(new
        {
            valid,
            renderedInstructions = render.RenderedInstructions,
            missingTokens = render.MissingTokens,
            validationErrors,
            evaluationPrompt,
            knowledgeAttachmentCount = knowledgeCount,
            message,
        });
    }

    [HttpPost("agents/{idOrCapability}/publish")]
    public async Task<ActionResult<object>> Publish(string idOrCapability, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        if (!IsStudioOwned(definition, Owner))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Only the Studio owner can publish." });

        var draft = LatestOwnedDraft(definition, Owner);
        if (draft is null)
            return Conflict(new { error = "An owned draft version is required to publish." });
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(draft.FacetsJson);
        if (facets?.Visibility == "admin_shared" && !admin.IsAuthorized(user))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Administrator authorization is required to publish admin_shared Studio agents.",
            });

        var published = await repo.TransitionTaskAgentVersionAsync(
            draft.Id, "publish", new(Owner, "Published from Custom Agent Studio."), ct);
        definition = await repo.GetTaskAgentAsync(definition.Id.ToString("D"), ct) ?? definition;

        var successorBody = DraftBodyFromPublished(definition, published);
        var successor = await CreateDraftVersionAsync(definition, successorBody, ct);
        definition = await repo.GetTaskAgentAsync(definition.Id.ToString("D"), ct) ?? definition;
        var successorFacets = GccV2StudioTemplateRenderer.TryParseFacets(successor.FacetsJson)!;
        return Ok(new
        {
            contractVersion = "gcc-studio-agent.v1",
            agent = StudioSummary(definition, successor, successorFacets),
            inputSchema = JsonSerializer.Deserialize<JsonElement>(successor.InputSchemaJson),
            outputSchema = JsonSerializer.Deserialize<JsonElement>(successor.OutputSchemaJson),
            workflow = JsonSerializer.Deserialize<JsonElement>(successor.WorkflowJson),
            contextPolicy = JsonSerializer.Deserialize<JsonElement>(successor.ContextPolicyJson),
            resultRenderer = JsonSerializer.Deserialize<JsonElement>(successor.ResultRendererJson),
            compatibleArtifactTypes = JsonSerializer.Deserialize<JsonElement>(successor.CompatibleArtifactTypesJson),
            allowedModels = JsonSerializer.Deserialize<JsonElement>(successor.AllowedModelsJson),
            evaluationThresholds = JsonSerializer.Deserialize<JsonElement>(successor.EvaluationThresholdsJson),
            digests = new
            {
                successor.InputSchemaDigest,
                successor.OutputSchemaDigest,
                successor.WorkflowDigest,
                successor.VersionDigest,
            },
            publishedVersion = new
            {
                versionId = published.Id.ToString("D"),
                version = published.SemanticVersion,
                digest = published.VersionDigest,
            },
            message = $"Published {published.SemanticVersion}. Successor draft {successor.SemanticVersion} is ready.",
        });
    }

    [HttpPost("agents/{idOrCapability}/deprecate")]
    public async Task<ActionResult<object>> Deprecate(string idOrCapability, CancellationToken ct) =>
        await TransitionPublishedAsync(idOrCapability, "deprecate", ct);

    [HttpPost("agents/{idOrCapability}/revoke")]
    public async Task<ActionResult<object>> Revoke(string idOrCapability, CancellationToken ct) =>
        await TransitionPublishedAsync(idOrCapability, "revoke", ct);

    private async Task<ActionResult<object>> TransitionPublishedAsync(
        string idOrCapability, string transition, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var definition = await repo.GetTaskAgentAsync(idOrCapability, ct);
        if (definition is null) return NotFound();
        var isOwner = IsStudioOwned(definition, Owner);
        if (!isOwner && !admin.IsAuthorized(user))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Owner or administrator authorization is required.",
            });

        var target = transition switch
        {
            "deprecate" => LatestPublishedStudio(definition),
            "revoke" => definition.Versions
                .Where(GccV2StudioTemplateRenderer.IsStudioVersion)
                .Where(x => x.State is "published" or "deprecated")
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault(),
            _ => null,
        };
        if (target is null)
            return Conflict(new { error = $"No Studio version is eligible for {transition}." });

        var updated = await repo.TransitionTaskAgentVersionAsync(
            target.Id, transition, new(Owner, $"{transition} from Custom Agent Studio."), ct);
        definition = await repo.GetTaskAgentAsync(definition.Id.ToString("D"), ct) ?? definition;
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(updated.FacetsJson)!;
        return Ok(StudioDetail(definition, updated, facets));
    }

    private async Task<GccV2TaskAgentVersionDto> CreateDraftVersionAsync(
        GccV2TaskAgentDefinitionDto definition, StudioDraftBody draft, CancellationToken ct)
    {
        var semanticVersion = $"0.{definition.Versions.Count + 1}.0";
        var fields = draft.Fields ?? [];
        var inputSchema = BuildInputSchema(fields);
        var outputSchema = new
        {
            type = "object",
            required = new[] { "artifactType" },
            properties = new
            {
                artifactType = new Dictionary<string, string> { ["const"] = ArtifactType },
            },
        };
        var uiSchema = draft.UiSchema is { ValueKind: JsonValueKind.Object } ui
            ? (object)ui
            : new { fields };
        var knowledgeIds = (draft.ContextKnowledgeIds ?? [])
            .Select(id => (id ?? "").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
        var workflow = new Dictionary<string, object?>
        {
            ["engine"] = GccV2StudioTemplateRenderer.Engine,
            ["mode"] = GccV2StudioTemplateRenderer.Mode,
            ["artifactType"] = ArtifactType,
            ["instructionsTemplate"] = draft.InstructionsTemplate,
            ["exampleOutput"] = draft.ExampleOutput ?? "",
            ["temperature"] = draft.Temperature,
            ["uiSchema"] = uiSchema,
            ["evaluationPrompt"] = draft.EvaluationPrompt ?? "",
            ["contextKnowledgeIds"] = knowledgeIds,
        };
        var facets = new
        {
            category = GccV2StudioTemplateRenderer.Category,
            ownerUserId = Owner,
            visibility = draft.Visibility,
            methodology = GccV2StudioTemplateRenderer.Methodology,
        };

        return await repo.CreateTaskAgentVersionAsync(definition.Id, new(
            semanticVersion,
            "studio",
            GccV2CanonicalJson.Serialize(facets),
            GccV2CanonicalJson.Serialize(inputSchema),
            GccV2CanonicalJson.Serialize(outputSchema),
            GccV2CanonicalJson.Serialize(workflow),
            """{"requiresApprovedContext":false,"acceptsDirectDocument":true}""",
            GccV2CanonicalJson.Serialize(new { kind = "custom-agent", artifactType = ArtifactType }),
            GccV2CanonicalJson.Serialize(new { accepts = Array.Empty<string>(), produces = new[] { ArtifactType } }),
            "[]",
            GccV2CanonicalJson.Serialize(new[] { draft.AllowedModel }),
            "[]",
            """{"requiresExampleOutput":true,"dryRunRequired":true}""",
            Owner), ct);
    }

    private static StudioDraftBody DraftBodyFromPublished(
        GccV2TaskAgentDefinitionDto definition,
        GccV2TaskAgentVersionDto published)
    {
        var facets = GccV2StudioTemplateRenderer.TryParseFacets(published.FacetsJson);
        var visibility = facets?.Visibility is "admin_shared" ? "admin_shared" : "private";
        var fields = new List<StudioFieldBody>();
        var instructionsTemplate = "";
        var exampleOutput = "";
        var evaluationPrompt = "";
        double temperature = 0.2;
        var knowledgeIds = new List<string>();
        JsonElement? uiSchema = null;

        try
        {
            using var workflow = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(published.WorkflowJson) ? "{}" : published.WorkflowJson);
            var root = workflow.RootElement;
            if (root.TryGetProperty("instructionsTemplate", out var template)
                && template.ValueKind == JsonValueKind.String)
                instructionsTemplate = template.GetString() ?? "";
            if (root.TryGetProperty("exampleOutput", out var example)
                && example.ValueKind == JsonValueKind.String)
                exampleOutput = example.GetString() ?? "";
            if (root.TryGetProperty("evaluationPrompt", out var evaluation)
                && evaluation.ValueKind == JsonValueKind.String)
                evaluationPrompt = evaluation.GetString() ?? "";
            if (root.TryGetProperty("temperature", out var temp)
                && temp.ValueKind == JsonValueKind.Number)
                temperature = temp.GetDouble();
            if (root.TryGetProperty("contextKnowledgeIds", out var knowledge)
                && knowledge.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in knowledge.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(entry.GetString()))
                        knowledgeIds.Add(entry.GetString()!);
                }
            }
            if (root.TryGetProperty("uiSchema", out var schema)
                && schema.ValueKind == JsonValueKind.Object)
            {
                uiSchema = schema.Clone();
                if (schema.TryGetProperty("fields", out var fieldArray)
                    && fieldArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldArray.EnumerateArray())
                    {
                        if (field.ValueKind != JsonValueKind.Object) continue;
                        var id = field.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                            ? idEl.GetString() ?? ""
                            : "";
                        var label = field.TryGetProperty("label", out var labelEl) && labelEl.ValueKind == JsonValueKind.String
                            ? labelEl.GetString() ?? ""
                            : "";
                        var type = field.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                            ? typeEl.GetString() ?? "shortText"
                            : "shortText";
                        var required = field.TryGetProperty("required", out var reqEl)
                            && reqEl.ValueKind == JsonValueKind.True;
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) continue;
                        fields.Add(new StudioFieldBody(id, label, type, required));
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Keep defaults when workflow JSON is malformed.
        }

        var allowedModel = "gpt-5.4";
        try
        {
            using var models = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(published.AllowedModelsJson) ? "[]" : published.AllowedModelsJson);
            if (models.RootElement.ValueKind == JsonValueKind.Array
                && models.RootElement.GetArrayLength() > 0
                && models.RootElement[0].ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(models.RootElement[0].GetString()))
            {
                allowedModel = models.RootElement[0].GetString()!;
            }
        }
        catch (JsonException)
        {
            // Keep default model.
        }

        return new StudioDraftBody(
            definition.DisplayName,
            definition.Description,
            visibility,
            fields,
            instructionsTemplate,
            exampleOutput,
            allowedModel,
            temperature,
            uiSchema,
            evaluationPrompt,
            knowledgeIds);
    }

    private static object BuildInputSchema(IReadOnlyList<StudioFieldBody> fields)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();
        foreach (var field in fields)
        {
            properties[field.Id] = string.Equals(field.Type, "tags", StringComparison.OrdinalIgnoreCase)
                ? new { type = "array", items = new { type = "string" } }
                : new { type = "string" };
            if (field.Required) required.Add(field.Id);
        }
        return new
        {
            type = "object",
            additionalProperties = false,
            required,
            properties,
        };
    }

    private static bool TrySelectStudioView(
        GccV2TaskAgentDefinitionDto definition,
        string ownerUserId,
        out GccV2TaskAgentVersionDto version,
        out GccV2StudioTemplateRenderer.StudioFacets facets)
    {
        version = null!;
        facets = null!;
        var studioVersions = definition.Versions
            .Where(GccV2StudioTemplateRenderer.IsStudioVersion)
            .ToList();
        if (studioVersions.Count == 0) return false;

        var ownedDraft = studioVersions
            .Where(x => x.State == "draft" && GccV2StudioTemplateRenderer.IsOwnedBy(x, ownerUserId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (ownedDraft is not null)
        {
            version = ownedDraft;
            facets = GccV2StudioTemplateRenderer.TryParseFacets(ownedDraft.FacetsJson)!;
            return true;
        }

        var ownedAny = studioVersions
            .Where(x => GccV2StudioTemplateRenderer.IsOwnedBy(x, ownerUserId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (ownedAny is not null)
        {
            version = ownedAny;
            facets = GccV2StudioTemplateRenderer.TryParseFacets(ownedAny.FacetsJson)!;
            return true;
        }

        var shared = studioVersions
            .Where(GccV2StudioTemplateRenderer.IsAdminSharedPublished)
            .OrderByDescending(x => Version.TryParse(x.SemanticVersion, out var parsed) ? parsed : new Version())
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (shared is null) return false;
        version = shared;
        facets = GccV2StudioTemplateRenderer.TryParseFacets(shared.FacetsJson)!;
        return true;
    }

    private static bool IsStudioOwned(GccV2TaskAgentDefinitionDto definition, string ownerUserId) =>
        definition.Versions.Any(x => GccV2StudioTemplateRenderer.IsOwnedBy(x, ownerUserId));

    private static GccV2TaskAgentVersionDto? LatestOwnedDraft(
        GccV2TaskAgentDefinitionDto definition, string ownerUserId) =>
        definition.Versions
            .Where(x => x.State == "draft" && GccV2StudioTemplateRenderer.IsOwnedBy(x, ownerUserId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

    private static GccV2TaskAgentVersionDto? LatestPublishedStudio(
        GccV2TaskAgentDefinitionDto definition) =>
        definition.Versions
            .Where(x => x.State == "published" && GccV2StudioTemplateRenderer.IsStudioVersion(x))
            .OrderByDescending(x => Version.TryParse(x.SemanticVersion, out var parsed) ? parsed : new Version())
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

    private static object StudioSummary(
        GccV2TaskAgentDefinitionDto definition,
        GccV2TaskAgentVersionDto version,
        GccV2StudioTemplateRenderer.StudioFacets facets) => new
    {
        id = definition.CapabilityId,
        definitionId = definition.Id,
        definition.DisplayName,
        definition.Description,
        versionId = version.Id,
        version = version.SemanticVersion,
        version.State,
        digest = version.VersionDigest,
        visibility = facets.Visibility,
        ownerUserId = facets.OwnerUserId,
        facets = JsonSerializer.Deserialize<JsonElement>(version.FacetsJson),
    };

    private static object StudioDetail(
        GccV2TaskAgentDefinitionDto definition,
        GccV2TaskAgentVersionDto version,
        GccV2StudioTemplateRenderer.StudioFacets facets)
    {
        using var workflowDoc = JsonDocument.Parse(version.WorkflowJson);
        var workflow = workflowDoc.RootElement.Clone();
        return new
        {
            contractVersion = "gcc-studio-agent.v1",
            agent = StudioSummary(definition, version, facets),
            inputSchema = JsonSerializer.Deserialize<JsonElement>(version.InputSchemaJson),
            outputSchema = JsonSerializer.Deserialize<JsonElement>(version.OutputSchemaJson),
            workflow,
            contextPolicy = JsonSerializer.Deserialize<JsonElement>(version.ContextPolicyJson),
            resultRenderer = JsonSerializer.Deserialize<JsonElement>(version.ResultRendererJson),
            compatibleArtifactTypes = JsonSerializer.Deserialize<JsonElement>(version.CompatibleArtifactTypesJson),
            allowedModels = JsonSerializer.Deserialize<JsonElement>(version.AllowedModelsJson),
            evaluationThresholds = JsonSerializer.Deserialize<JsonElement>(version.EvaluationThresholdsJson),
            digests = new
            {
                version.InputSchemaDigest,
                version.OutputSchemaDigest,
                version.WorkflowDigest,
                version.VersionDigest,
            },
        };
    }

    private static bool IsValidVisibility(string? value, out string visibility)
    {
        visibility = (value ?? "").Trim().ToLowerInvariant();
        return visibility is "private" or "admin_shared";
    }

    private static bool IsValidFieldId(string value) =>
        value.Length is > 0 and <= 128
        && value.All(x => char.IsAsciiLetterOrDigit(x) || x == '-')
        && char.IsAsciiLetterOrDigit(value[0])
        && char.IsAsciiLetterOrDigit(value[^1]);

    public sealed record StudioCreateBody(string Name, string Outcome, string Visibility);
    public sealed record StudioFieldBody(
        string Id, string Label, string Type, bool Required,
        string? Placeholder = null, IReadOnlyList<string>? Options = null);
    public sealed record StudioDraftBody(
        string? Name, string? Outcome, string Visibility,
        IReadOnlyList<StudioFieldBody> Fields,
        string InstructionsTemplate, string ExampleOutput,
        string AllowedModel, double Temperature,
        JsonElement? UiSchema = null, string? EvaluationPrompt = null,
        IReadOnlyList<string>? ContextKnowledgeIds = null);
    public sealed record StudioDryRunBody(JsonElement Input);
}
