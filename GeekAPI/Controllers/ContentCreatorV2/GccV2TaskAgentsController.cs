using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Context;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.Gsc;
using GeekAPI.Services.ContentCreatorV2.TaskAgents;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Owner-scoped task-agent catalog and durable run/result shell.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/task-agents")]
public sealed class GccV2TaskAgentsController(
    ICurrentUserContext user,
    GccV2SkillAdminPolicy admin,
    HttpGccV2Repository repo,
    GccV2ContextResolver contextResolver,
    GccV2GscSearchAnalyticsClient gscSearch) : ControllerBase
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

    [HttpGet("library")]
    public async Task<ActionResult<object>> Library(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var prefs = await repo.GetTaskAgentLibraryPreferencesAsync(Owner, ct);
        var runs = await repo.ListTaskRunsAsync(Owner, null, ct);
        var definitions = await repo.ListTaskAgentsAsync(null, ct);
        var capabilityByDefinition = definitions.ToDictionary(x => x.Id, x => x.CapabilityId);
        var recent = runs
            .Select(run =>
            {
                capabilityByDefinition.TryGetValue(run.TaskAgentDefinitionId, out var capabilityId);
                return new
                {
                    capabilityId,
                    lastRunAtUtc = run.UpdatedAtUtc == default ? run.CreatedAtUtc : run.UpdatedAtUtc,
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.capabilityId))
            .GroupBy(x => x.capabilityId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.lastRunAtUtc).First())
            .OrderByDescending(x => x.lastRunAtUtc)
            .Take(12)
            .Select(x => new { capabilityId = x.capabilityId, lastRunAtUtc = x.lastRunAtUtc });

        return Ok(new
        {
            contractVersion = "gcc-task-agent-library.v1",
            favorites = DeserializeStringArray(prefs.FavoritesJson),
            recent,
            savedConfigs = DeserializeSavedConfigs(prefs.SavedConfigsJson),
        });
    }

    [HttpPut("library")]
    public async Task<ActionResult<object>> PutLibrary([FromBody] PutLibraryRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var favorites = (request.Favorites ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var savedConfigs = (request.SavedConfigs ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.CapabilityId) && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new
            {
                id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("D") : x.Id.Trim(),
                capabilityId = x.CapabilityId.Trim(),
                name = x.Name.Trim(),
                values = x.Values ?? new Dictionary<string, string>(),
                updatedAtUtc = string.IsNullOrWhiteSpace(x.UpdatedAtUtc)
                    ? DateTimeOffset.UtcNow.ToString("O")
                    : x.UpdatedAtUtc,
            })
            .ToArray();

        var prefs = await repo.PutTaskAgentLibraryPreferencesAsync(
            new PutGccV2TaskAgentLibraryPreferencesCommand(
                Owner,
                JsonSerializer.Serialize(favorites),
                JsonSerializer.Serialize(savedConfigs)),
            ct);

        var runs = await repo.ListTaskRunsAsync(Owner, null, ct);
        var definitions = await repo.ListTaskAgentsAsync(null, ct);
        var capabilityByDefinition = definitions.ToDictionary(x => x.Id, x => x.CapabilityId);
        var recent = runs
            .Select(run =>
            {
                capabilityByDefinition.TryGetValue(run.TaskAgentDefinitionId, out var capabilityId);
                return new
                {
                    capabilityId,
                    lastRunAtUtc = run.UpdatedAtUtc == default ? run.CreatedAtUtc : run.UpdatedAtUtc,
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.capabilityId))
            .GroupBy(x => x.capabilityId!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.lastRunAtUtc).First())
            .OrderByDescending(x => x.lastRunAtUtc)
            .Take(12)
            .Select(x => new { capabilityId = x.capabilityId, lastRunAtUtc = x.lastRunAtUtc });

        return Ok(new
        {
            contractVersion = "gcc-task-agent-library.v1",
            favorites = DeserializeStringArray(prefs.FavoritesJson),
            recent,
            savedConfigs = DeserializeSavedConfigs(prefs.SavedConfigsJson),
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

    /// <summary>
    /// CC-owned GSC queries for Query Planner. Prefer /gsc/connections/{id}/observed-queries;
    /// this alias keeps the query-planner route stable while dropping Geek SEO project IDs.
    /// </summary>
    [HttpGet("query-planner/observed-queries")]
    public async Task<ActionResult<object>> ObservedQueries(
        [FromQuery] Guid connectionId,
        [FromQuery] DateOnly? startDate,
        [FromQuery] DateOnly? endDate,
        [FromQuery] int? rowLimit,
        CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (connectionId == Guid.Empty)
            return BadRequest(new { error = "connectionId is required (Content Creator GSC connection)." });

        var connection = await repo.GetGscConnectionAsync(connectionId, Owner, ct);
        if (connection is null) return NotFound(new { error = "GSC connection not found." });

        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var start = startDate ?? end.AddDays(-90);
        var limit = rowLimit is null or < 1 ? 200 : Math.Min(rowLimit.Value, 1000);
        var fetchedAt = DateTimeOffset.UtcNow;
        var sourceId = $"gsc:{connection.Id:D}:{start:yyyy-MM-dd}:{end:yyyy-MM-dd}";

        IReadOnlyList<string> queries;
        if (connection.Status == "stub" || connection.EncryptedRefreshToken.Length == 0)
        {
            queries = [];
        }
        else
        {
            var clientId = (Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_ID") ?? "").Trim();
            var clientSecret = (Environment.GetEnvironmentVariable("GEEK_CC_GSC_GOOGLE_CLIENT_SECRET") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new
                {
                    error = "GEEK_CC_GSC_GOOGLE_CLIENT_ID/SECRET are required for live Search Console fetch.",
                });
            }

            try
            {
                var refresh = GccV2GscCredentialProtector.Decrypt(
                    connection.EncryptedRefreshToken,
                    connection.EncryptionIv,
                    connection.EncryptionTag);
                var access = await gscSearch.ExchangeRefreshTokenAsync(refresh, clientId, clientSecret, ct);
                var rows = await gscSearch.QueryAsync(access, connection.SiteUrl, start, end, limit, ct);
                queries = rows.Select(x => x.Query).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
            }
        }

        return Ok(new
        {
            contractVersion = "gcc-query-planner-observed.v1",
            connectionId = connection.Id,
            siteUrl = connection.SiteUrl,
            startDate = start,
            endDate = end,
            fetchedAtUtc = fetchedAt,
            source = new
            {
                sourceId,
                kind = "google-search-console",
                label = connection.SiteUrl,
                siteUrl = connection.SiteUrl,
                connectionId = connection.Id,
            },
            demandDisclaimer =
                "Observed GSC queries are first-party search analytics, not traffic, volume, ranking, or demand scores for planning heuristics.",
            queries = queries.Select(query => new
            {
                query,
                origin = "observed",
                sourceId,
                observedAtUtc = fetchedAt.ToString("O"),
            }),
            queryCount = queries.Count,
        });
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

        Guid? contextManifestId = request.ContextManifestId;
        string? contextManifestDigest = request.ContextManifestDigest;
        object? contextEnvelope = null;
        if (request.ContextSelection is { } selection)
        {
            var prepared = await contextResolver.PrepareForTaskAgentAsync(Owner, selection, ct);
            if (prepared.Preview.BlockingFindings.Count > 0)
            {
                return Conflict(new
                {
                    error = "Governed context is not eligible for this task-agent run.",
                    blockingFindings = prepared.Preview.BlockingFindings,
                    warnings = prepared.Preview.Warnings,
                });
            }
            contextManifestId = prepared.ManifestId;
            contextManifestDigest = prepared.Sha256;
            contextEnvelope = new
            {
                schemaVersion = "task-agent-context-envelope.v1",
                manifestId = prepared.ManifestId,
                digest = prepared.Sha256,
                signature = prepared.Signature,
                signingKeyId = prepared.SigningKeyId,
                resolvedAtUtc = prepared.ResolvedAtUtc,
                canonicalJson = prepared.CanonicalJson,
                effectiveEntries = prepared.Preview.EffectiveEntries,
            };
        }

        var parentArtifactVersionIds = (request.ParentArtifactVersionIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        var lineageRelationship = string.IsNullOrWhiteSpace(request.LineageRelationship)
            ? (request.RetryOfRunId is null ? "derived-from" : "retry-of")
            : request.LineageRelationship.Trim();
        if (parentArtifactVersionIds.Count == 0 && request.RetryOfRunId is { } retryOf)
        {
            var prior = await repo.GetTaskRunAsync(retryOf, Owner, ct);
            if (prior is null) return Conflict(new { error = "Retry source run was not found." });
            parentArtifactVersionIds = (prior.Artifacts ?? [])
                .SelectMany(artifact => artifact.Versions)
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => version.Id)
                .Take(1)
                .ToList();
            lineageRelationship = "retry-of";
        }

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
            contextManifestId,
            contextEnvelope,
            parentArtifactVersionIds,
            lineageRelationship,
        });
        var run = await repo.CreateTaskRunAsync(new(
            Owner, definition.Id, version.Id, version.VersionDigest,
            input, GccV2CanonicalJson.Sha256(input),
            contextManifestId, contextManifestDigest,
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
            sharedContext = new { contextManifestId, contextManifestDigest },
            lineage = new { parentArtifactVersionIds, relationship = lineageRelationship },
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
        var versions = (run.Artifacts ?? []).SelectMany(x => x.Versions).ToList();
        var lineage = versions.Select(x => new
        {
            artifactVersionId = x.Id,
            versionNumber = x.VersionNumber,
            digest = x.Digest,
            parents = (x.Parents ?? []).Select(edge => new
            {
                edge.ParentArtifactVersionId,
                edge.Relationship,
                edge.CreatedAtUtc,
            }),
            children = (x.Children ?? []).Select(edge => new
            {
                edge.ChildArtifactVersionId,
                edge.Relationship,
                edge.CreatedAtUtc,
            }),
        }).ToList();

        object? changeOverTime = null;
        if (string.Equals(run.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            var priors = await repo.ListTaskRunsAsync(Owner, "succeeded", ct);
            var definitions = await repo.ListTaskAgentsAsync(ct: ct);
            var capabilityByDefinition = definitions.ToDictionary(x => x.Id, x => x.CapabilityId);
            capabilityByDefinition[definition.Id] = definition.CapabilityId;
            var change = GccV2ArtifactChangeOverTime.FromRuns(
                run,
                definition.CapabilityId,
                priors,
                id => capabilityByDefinition.TryGetValue(id, out var capability) ? capability : null);

            if (change is not null)
            {
                changeOverTime = new
                {
                    available = change.Available,
                    priorRunId = change.PriorRunId,
                    priorCompletedAtUtc = change.PriorCompletedAtUtc,
                    subjectKey = change.SubjectKey,
                    currentOverall = change.CurrentOverall,
                    priorOverall = change.PriorOverall,
                    overallDelta = change.OverallDelta,
                    dimensions = change.Dimensions.Select(d => new
                    {
                        dimension = d.Dimension,
                        current = d.Current,
                        prior = d.Prior,
                        delta = d.Delta,
                    }),
                    message = change.Message,
                };
            }
        }

        return Ok(new
        {
            contractVersion = "gcc-task-result-shell.v1",
            identity = new { definition.CapabilityId, definition.DisplayName, objective = definition.Description },
            taskInputs = JsonSerializer.Deserialize<JsonElement>(run.InputJson),
            sharedContext = new { run.ContextManifestId, run.ContextManifestDigest },
            sourceReadinessAndProvenance = JsonSerializer.Deserialize<JsonElement>(run.SourceSnapshotJson),
            progress = new { run.Status, run.Phase, run.ProgressPercent, events = run.Events ?? [] },
            renderer = JsonSerializer.Deserialize<JsonElement>(version.ResultRendererJson),
            findingsAndEvidence = versions.Select(x => new
            {
                artifactVersionId = x.Id,
                evidence = JsonSerializer.Deserialize<JsonElement>(x.EvidenceJson),
                citations = JsonSerializer.Deserialize<JsonElement>(x.CitationsJson),
                x.ValidationState,
            }),
            artifacts = run.Artifacts ?? [],
            lineage,
            changeOverTime,
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
            rerun = new
            {
                capabilityId = definition.CapabilityId,
                versionId = version.Id,
                retryOfRunId = run.Id,
                parentArtifactVersionIds = versions
                    .OrderByDescending(x => x.VersionNumber)
                    .Select(x => x.Id)
                    .Take(1)
                    .ToList(),
            },
            compatibleNextActions = JsonSerializer.Deserialize<JsonElement>(version.CompatibleArtifactTypesJson),
            nextActions = GccV2TaskAgentNextActions.FromCompatibilityJson(version.CompatibleArtifactTypesJson),
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
            visibilityScope = VisibilityScope(version),
            facets = JsonSerializer.Deserialize<JsonElement>(version.FacetsJson),
        };

    private static string VisibilityScope(GccV2TaskAgentVersionDto version)
    {
        if (!GccV2StudioTemplateRenderer.IsStudioVersion(version)) return "public";
        return GccV2StudioTemplateRenderer.IsAdminSharedPublished(version) ? "workspace" : "custom";
    }

    private static IReadOnlyList<string> DeserializeStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<object> DeserializeSavedConfigs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            var configs = new List<object>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                var id = entry.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                var capabilityId = entry.TryGetProperty("capabilityId", out var capabilityEl)
                    && capabilityEl.ValueKind == JsonValueKind.String
                    ? capabilityEl.GetString()
                    : null;
                var name = entry.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(capabilityId)
                    || string.IsNullOrWhiteSpace(name))
                    continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                if (entry.TryGetProperty("values", out var valuesEl) && valuesEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in valuesEl.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                            values[property.Name] = property.Value.GetString() ?? "";
                    }
                }
                var updatedAtUtc = entry.TryGetProperty("updatedAtUtc", out var updatedEl)
                    && updatedEl.ValueKind == JsonValueKind.String
                    ? updatedEl.GetString()
                    : DateTimeOffset.UtcNow.ToString("O");
                configs.Add(new { id, capabilityId, name, values, updatedAtUtc });
            }
            return configs;
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
        Guid? RetryOfRunId = null, GccV2ContextSelectionRequest? ContextSelection = null,
        IReadOnlyList<Guid>? ParentArtifactVersionIds = null, string? LineageRelationship = null);
    public sealed record PutLibraryRequest(
        IReadOnlyList<string>? Favorites = null,
        IReadOnlyList<LibrarySavedConfigRequest>? SavedConfigs = null);
    public sealed record LibrarySavedConfigRequest(
        string? Id,
        string CapabilityId,
        string Name,
        Dictionary<string, string>? Values,
        string? UpdatedAtUtc);
    public sealed record CreateDefinitionRequest(string CapabilityId, string DisplayName, string Description);
    public sealed record PatchDefinitionRequest(string? DisplayName, string? Description);
    public sealed record CreateVersionRequest(
        string SemanticVersion, string WorkflowGroup, JsonElement Facets, JsonElement InputSchema,
        JsonElement OutputSchema, JsonElement Workflow, JsonElement ContextPolicy,
        JsonElement ResultRenderer, JsonElement CompatibleArtifactTypes, JsonElement AllowedTools,
        JsonElement AllowedModels, JsonElement SkillVersionIds, JsonElement EvaluationThresholds);
    public sealed record TransitionRequest(string? Reason);
}

