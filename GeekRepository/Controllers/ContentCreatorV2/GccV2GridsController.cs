using System.Diagnostics;
using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

/// <summary>Internal persistence API for owner-scoped durable batch grids.</summary>
[ApiController]
[Route("repo/content-creator-v2/grids")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2GridsController(ContentCreatorV2DbContext db) : ControllerBase
{
    private const int MaxRunsInGraph = 20;
    private const string DefaultExecutionNote =
        "Sample/full runs create one durable TaskRun per selected row; GeekAPI supplies RAG content artifacts when available, otherwise in-process deterministic payloads.";

    private static readonly HashSet<string> GridStatuses =
        ["draft", "ready", "running", "complete"];
    private static readonly HashSet<string> RunModes =
        ["sample", "full"];
    private static readonly HashSet<string> DemoCapabilities =
        ["faq-generator", "pillar-outline"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] FaqDemoTopics =
    [
        "What is Evidence Engine?",
        "How does RAG grounding work?",
        "Who owns brand voice approvals?",
        "How do credit budgets apply to batch runs?",
        "Can agents cite source library assets?",
        "What happens on a failed row?",
        "How do sample runs differ from full runs?",
        "Where do FAQ outputs publish?",
        "How is owner isolation enforced?",
        "What is a TaskRun fan-out?",
        "Can grids reuse canvas assets?",
        "How do I preview estimated credits?",
    ];

    private static readonly string[] PillarDemoTopics =
    [
        "Evidence Engine",
        "RAG grounding",
        "Brand voice approvals",
        "Credit budgets for batch runs",
        "Source library citations",
        "Failed row recovery",
        "Sample versus full runs",
        "FAQ output publishing",
        "Owner isolation",
        "TaskRun fan-out",
        "Canvas asset reuse",
        "Estimated credit previews",
    ];

    private static string BuildDefaultConfigJson(string capability)
    {
        var (label, capabilityId) = capability switch
        {
            "pillar-outline" => ("Pillar Article Outline", "pillar-outline"),
            _ => ("FAQ Generator", "faq-generator"),
        };
        return JsonSerializer.Serialize(new
        {
            columns = new object[]
            {
                new { key = "topic", kind = "input", label = "Topic" },
                new { key = "agent", kind = "agent", label, capability = capabilityId },
                new { key = "output", kind = "output", label = "Result" },
            },
            creditsPerRow = 1,
            executionNote = DefaultExecutionNote,
        }, JsonOpts);
    }

    private static string NormalizeDemoCapability(string? capability)
    {
        var value = string.IsNullOrWhiteSpace(capability) ? "faq-generator" : capability.Trim();
        return DemoCapabilities.Contains(value) ? value : "";
    }
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GridListItem>>> List(
        [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");

        var grids = await db.GccV2Grids.AsNoTracking()
            .Where(g => g.OwnerUserId == ownerUserId)
            .OrderByDescending(g => g.UpdatedAtUtc)
            .Select(g => new
            {
                g.Id,
                g.OwnerUserId,
                g.Name,
                g.Description,
                g.Status,
                g.CreatedAtUtc,
                g.UpdatedAtUtc,
                g.ConfigJson,
                RowCount = g.Rows.Count,
                LastRunStatus = g.Runs
                    .OrderByDescending(r => r.StartedAtUtc)
                    .Select(r => r.Status)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(grids.Select(g => new GridListItem(
            g.Id, g.OwnerUserId, g.Name, g.Description, g.Status,
            g.CreatedAtUtc, g.UpdatedAtUtc, g.ConfigJson, g.RowCount, g.LastRunStatus)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GccV2Grid>> Get(
        Guid id, [FromQuery] string ownerUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            return BadRequest("ownerUserId is required.");

        var grid = await LoadGraphAsync(id, ownerUserId, tracking: false, ct);
        return grid is null ? NotFound() : Ok(grid);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Grid>> Create(
        CreateGridCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId) || string.IsNullOrWhiteSpace(command.Name))
            return BadRequest("ownerUserId and name are required.");
        var status = string.IsNullOrWhiteSpace(command.Status) ? "draft" : command.Status.Trim();
        if (!GridStatuses.Contains(status))
            return BadRequest($"status must be one of: {string.Join(", ", GridStatuses)}.");

        var capability = NormalizeDemoCapability(command.Capability);
        if (capability.Length == 0)
            return BadRequest($"capability must be one of: {string.Join(", ", DemoCapabilities)}.");

        var now = DateTimeOffset.UtcNow;
        var seedDemo = command.SeedDemo == true;
        var grid = new GccV2Grid
        {
            OwnerUserId = command.OwnerUserId.Trim(),
            Name = command.Name.Trim(),
            Description = (command.Description ?? string.Empty).Trim(),
            Status = seedDemo ? "ready" : status,
            ConfigJson = string.IsNullOrWhiteSpace(command.ConfigJson)
                ? BuildDefaultConfigJson(capability)
                : command.ConfigJson,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        if (seedDemo)
        {
            if (string.IsNullOrWhiteSpace(command.Description))
            {
                grid.Description = capability == "pillar-outline"
                    ? "Twelve pillar topics ready for a sample stub run."
                    : "Twelve FAQ topics ready for a sample stub run.";
            }

            var topics = capability == "pillar-outline" ? PillarDemoTopics : FaqDemoTopics;
            for (var i = 0; i < topics.Length; i++)
            {
                grid.Rows.Add(new GccV2GridRow
                {
                    GridId = grid.Id,
                    RowIndex = i,
                    InputJson = JsonSerializer.Serialize(new { topic = topics[i] }, JsonOpts),
                    Status = "pending",
                    UpdatedAtUtc = now,
                });
            }
        }

        db.Add(grid);
        await db.SaveChangesAsync(ct);
        var loaded = await LoadGraphAsync(grid.Id, grid.OwnerUserId, tracking: false, ct)
            ?? throw new InvalidOperationException("Created grid could not be reloaded.");
        return CreatedAtAction(nameof(Get), new { id = grid.Id, ownerUserId = grid.OwnerUserId }, loaded);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<GccV2Grid>> Patch(
        Guid id, PatchGridCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");

        var grid = await db.GccV2Grids
            .SingleOrDefaultAsync(g => g.Id == id && g.OwnerUserId == command.OwnerUserId, ct);
        if (grid is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(command.Name))
            grid.Name = command.Name.Trim();
        if (command.Description is not null)
            grid.Description = command.Description.Trim();
        if (!string.IsNullOrWhiteSpace(command.Status))
        {
            var status = command.Status.Trim();
            if (!GridStatuses.Contains(status))
                return BadRequest($"status must be one of: {string.Join(", ", GridStatuses)}.");
            grid.Status = status;
        }
        if (command.ConfigJson is not null)
            grid.ConfigJson = command.ConfigJson;
        grid.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var loaded = await LoadGraphAsync(grid.Id, grid.OwnerUserId, tracking: false, ct)
            ?? throw new InvalidOperationException("Patched grid could not be reloaded.");
        return Ok(loaded);
    }

    [HttpPost("{id:guid}/rows")]
    public async Task<ActionResult<GccV2GridRow>> CreateRow(
        Guid id, CreateGridRowCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");

        var grid = await db.GccV2Grids
            .Include(g => g.Rows)
            .SingleOrDefaultAsync(g => g.Id == id && g.OwnerUserId == command.OwnerUserId, ct);
        if (grid is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var nextIndex = grid.Rows.Count == 0 ? 0 : grid.Rows.Max(r => r.RowIndex) + 1;
        var row = new GccV2GridRow
        {
            GridId = grid.Id,
            RowIndex = nextIndex,
            InputJson = string.IsNullOrWhiteSpace(command.InputJson) ? "{}" : command.InputJson,
            Status = "pending",
            UpdatedAtUtc = now,
        };
        if (grid.Status is "draft" or "complete")
            grid.Status = "ready";
        grid.UpdatedAtUtc = now;
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = grid.Id, ownerUserId = grid.OwnerUserId }, row);
    }

    [HttpPost("{id:guid}/rows/bulk")]
    public async Task<ActionResult<IReadOnlyList<GccV2GridRow>>> CreateRowsBulk(
        Guid id, CreateGridRowsBulkCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");
        if (command.InputJsons is null || command.InputJsons.Count == 0)
            return BadRequest("inputJsons must contain at least one row.");
        if (command.InputJsons.Count > 100)
            return BadRequest("inputJsons is limited to 100 rows per request.");

        var grid = await db.GccV2Grids
            .Include(g => g.Rows)
            .SingleOrDefaultAsync(g => g.Id == id && g.OwnerUserId == command.OwnerUserId, ct);
        if (grid is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var nextIndex = grid.Rows.Count == 0 ? 0 : grid.Rows.Max(r => r.RowIndex) + 1;
        var created = new List<GccV2GridRow>(command.InputJsons.Count);
        foreach (var inputJson in command.InputJsons)
        {
            var row = new GccV2GridRow
            {
                GridId = grid.Id,
                RowIndex = nextIndex++,
                InputJson = string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson,
                Status = "pending",
                UpdatedAtUtc = now,
            };
            created.Add(row);
            db.Add(row);
        }

        if (grid.Status is "draft" or "complete")
            grid.Status = "ready";
        grid.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Ok(created);
    }

    [HttpPost("{id:guid}/runs")]
    public async Task<ActionResult<GccV2Grid>> CreateRun(
        Guid id, CreateGridRunCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.OwnerUserId))
            return BadRequest("ownerUserId is required.");

        var mode = string.IsNullOrWhiteSpace(command.Mode) ? "sample" : command.Mode.Trim();
        if (!RunModes.Contains(mode))
            return BadRequest($"mode must be one of: {string.Join(", ", RunModes)}.");

        var sampleSize = command.SampleSize is > 0 ? command.SampleSize.Value : 10;

        var grid = await db.GccV2Grids
            .Include(g => g.Rows)
            .Include(g => g.Runs)
            .SingleOrDefaultAsync(g => g.Id == id && g.OwnerUserId == command.OwnerUserId, ct);
        if (grid is null) return NotFound();

        var started = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        var actor = string.IsNullOrWhiteSpace(command.ActorUserId)
            ? command.OwnerUserId.Trim()
            : command.ActorUserId.Trim();

        var run = new GccV2GridRun
        {
            GridId = grid.Id,
            Mode = mode,
            SampleSize = mode == "sample" ? sampleSize : null,
            Status = "running",
            ActorUserId = actor,
            StartedAtUtc = started,
            BudgetPreviewJson = "{}",
            HistoryJson = "[]",
            OutputCount = 0,
        };
        grid.Status = "running";
        grid.UpdatedAtUtc = started;
        db.Add(run);

        var orderedRows = grid.Rows.OrderBy(r => r.RowIndex).ToList();
        var selected = mode == "full"
            ? orderedRows
            : orderedRows.Take(sampleSize).ToList();

        var creditsPerRow = ReadCreditsPerRow(grid.ConfigJson);
        var capability = ReadAgentCapability(grid.ConfigJson);
        var note = ReadExecutionNote(grid.ConfigJson);
        var agent = await ResolvePublishedAgentAsync(capability, ct);
        var runsController = new GccV2TaskRunsController(db);
        var succeededCount = 0;

        foreach (var row in selected)
        {
            row.Status = "running";
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
            row.Error = null;

            if (agent is null)
            {
                row.Status = "failed";
                row.Error = $"No published task agent for capability '{capability}'.";
                row.OutputJson = JsonSerializer.Serialize(new
                {
                    result = (string?)null,
                    mode = "unavailable",
                    capability,
                }, JsonOpts);
                row.UpdatedAtUtc = DateTimeOffset.UtcNow;
                continue;
            }

            try
            {
                string? artifactOverride = null;
                if (command.RowArtifactJsonByRowId is not null
                    && command.RowArtifactJsonByRowId.TryGetValue(row.Id.ToString("D"), out var supplied)
                    && !string.IsNullOrWhiteSpace(supplied))
                    artifactOverride = supplied;

                var executed = await CompleteTaskRunForRowAsync(
                    runsController, agent, grid.OwnerUserId, row, actor, run.Id, artifactOverride, ct);
                row.OutputJson = JsonSerializer.Serialize(new
                {
                    result = executed.Preview,
                    mode = "task-run",
                    capability = agent.CapabilityId,
                    endpoint = agent.Endpoint,
                    taskRunId = executed.TaskRunId.ToString("D"),
                    artifactType = agent.ArtifactType,
                    artifactSource = executed.ArtifactSource,
                    artifact = executed.Payload,
                }, JsonOpts);
                row.Status = "succeeded";
                succeededCount++;
            }
            catch (Exception ex)
            {
                row.Status = "failed";
                row.Error = ex.Message;
                row.OutputJson = JsonSerializer.Serialize(new
                {
                    result = (string?)null,
                    mode = "failed",
                    capability = agent.CapabilityId,
                    error = ex.Message,
                }, JsonOpts);
            }

            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        sw.Stop();
        var completed = DateTimeOffset.UtcNow;
        var estimatedCredits = creditsPerRow * selected.Count;
        var budget = new
        {
            creditsPerRow,
            rowCount = selected.Count,
            estimatedCredits,
            note,
            succeededCount,
            capability = agent?.CapabilityId ?? capability,
            execution = agent is null ? "unavailable" : "task-run",
        };
        run.BudgetPreviewJson = JsonSerializer.Serialize(budget, JsonOpts);
        run.OutputCount = succeededCount;
        run.Status = selected.Count > 0 && succeededCount == 0 ? "failed" : "succeeded";
        run.CompletedAtUtc = completed;

        var historyEntry = new
        {
            actor,
            mode,
            status = run.Status,
            startedAt = started.ToString("O"),
            durationMs = sw.ElapsedMilliseconds,
            outputCount = succeededCount,
            estimatedCredits,
            capability = agent?.CapabilityId ?? capability,
        };
        run.HistoryJson = JsonSerializer.Serialize(new[] { historyEntry }, JsonOpts);

        var allSucceeded = grid.Rows.Count > 0 && grid.Rows.All(r => r.Status == "succeeded");
        grid.Status = allSucceeded ? "complete" : "ready";
        grid.UpdatedAtUtc = completed;

        await db.SaveChangesAsync(ct);

        var loaded = await LoadGraphAsync(grid.Id, grid.OwnerUserId, tracking: false, ct)
            ?? throw new InvalidOperationException("Grid could not be reloaded after run.");
        return Ok(loaded);
    }

    private sealed record ResolvedTaskAgent(
        Guid DefinitionId, Guid VersionId, string VersionDigest,
        string CapabilityId, string Endpoint, string ArtifactType);

    private sealed record CompletedRowTaskRun(
        Guid TaskRunId, JsonElement Payload, string Preview, string ArtifactSource);

    private async Task<ResolvedTaskAgent?> ResolvePublishedAgentAsync(
        string capability, CancellationToken ct)
    {
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

    private static async Task<CompletedRowTaskRun> CompleteTaskRunForRowAsync(
        GccV2TaskRunsController runs,
        ResolvedTaskAgent agent,
        string ownerUserId,
        GccV2GridRow row,
        string actor,
        Guid gridRunId,
        string? artifactPayloadOverride,
        CancellationToken ct)
    {
        var inputCanonical = GccV2TaskAgentsController.Canonical(
            string.IsNullOrWhiteSpace(row.InputJson) ? "{}" : row.InputJson, JsonValueKind.Object);
        var emptyCanonical = GccV2TaskAgentsController.Canonical("{}", JsonValueKind.Object);
        var emptyDigest = GccV2TaskAgentsController.Hash(emptyCanonical);
        var inputDigest = GccV2TaskAgentsController.Hash(inputCanonical);
        var instanceId = $"grid-run:{gridRunId:N}";

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

        var topic = ReadTopicOrFirstInput(row.InputJson);
        var artifactSource = "deterministic";
        string payloadJson;
        if (!string.IsNullOrWhiteSpace(artifactPayloadOverride))
        {
            payloadJson = artifactPayloadOverride;
            artifactSource = "rag";
        }
        else
        {
            payloadJson = BuildDeterministicArtifactPayload(agent.ArtifactType, topic);
        }

        var payloadCanonical = GccV2TaskAgentsController.Canonical(payloadJson, JsonValueKind.Object);
        using var payloadDoc = JsonDocument.Parse(payloadCanonical);

        Unwrap<GccV2TaskArtifactVersion>(
            (await runs.AddArtifactVersion(artifact.Id, new GccV2TaskRunsController.CreateArtifactVersionCommand(
                ownerUserId, payloadCanonical, "[]", "[]", "valid", "{}", [],
                actor, ExpectedClaimedBy: instanceId), ct)).Result,
            "write task artifact version");

        Unwrap<GccV2TaskRun>(
            (await runs.Transition(created.Id, new GccV2TaskRunsController.TransitionRunCommand(
                "succeeded", "complete", 100, "succeeded",
                JsonSerializer.Serialize(new { source = "grid", gridRunId, artifactSource }),
                actor, instanceId, null, null), ct)).Result,
            "succeed task run");

        return new CompletedRowTaskRun(
            created.Id,
            payloadDoc.RootElement.Clone(),
            BuildPreview(agent.ArtifactType, topic, payloadDoc.RootElement),
            artifactSource);
    }

    private static string BuildDeterministicArtifactPayload(string artifactType, string topic)
    {
        if (string.Equals(artifactType, "faqSet.v1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                artifactType = "faqSet.v1",
                methodology = "grid-sync.v1",
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

        if (string.Equals(artifactType, "pillarOutline.v1", StringComparison.OrdinalIgnoreCase))
        {
            return JsonSerializer.Serialize(new
            {
                artifactType = "pillarOutline.v1",
                methodology = "grid-sync.v1",
                topic,
                sections = new[]
                {
                    new
                    {
                        sectionId = "sec-1",
                        heading = $"What is {topic}?",
                        objective = $"Define {topic} in answer-first language.",
                        answerFirstPrompt = $"{topic} is introduced with a direct answer and supporting evidence.",
                        relatedQueries = Array.Empty<string>(),
                        evidenceIds = Array.Empty<string>(),
                    },
                },
                supportingContentPlan = new[]
                {
                    new
                    {
                        contentType = "faq",
                        title = $"{topic} FAQ",
                        rationale = "Capture definitional queries as reusable FAQ pairs.",
                        origin = "generatedHypothesis",
                    },
                },
                warnings = new[]
                {
                    "Supporting content plans are labeled generatedHypothesis.",
                },
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            artifactType,
            methodology = "grid-sync.v1",
            summary = $"Deterministic grid output for “{topic}”.",
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

        if (string.Equals(artifactType, "pillarOutline.v1", StringComparison.OrdinalIgnoreCase))
        {
            if (payload.TryGetProperty("topic", out var pillarTopic)
                && pillarTopic.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(pillarTopic.GetString()))
                return $"Pillar outline for: {pillarTopic.GetString()}";
            return $"Pillar outline for: {topic}";
        }

        return string.Equals(artifactType, "faqSet.v1", StringComparison.OrdinalIgnoreCase)
            ? $"FAQ draft for: {topic}"
            : $"TaskRun output for: {topic}";
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

    private async Task<GccV2Grid?> LoadGraphAsync(
        Guid id, string ownerUserId, bool tracking, CancellationToken ct)
    {
        IQueryable<GccV2Grid> query = tracking ? db.GccV2Grids : db.GccV2Grids.AsNoTracking();
        var grid = await query
            .Include(g => g.Rows)
            .Include(g => g.Runs)
            .SingleOrDefaultAsync(g => g.Id == id && g.OwnerUserId == ownerUserId, ct);
        if (grid is null) return null;

        grid.Rows = grid.Rows.OrderBy(r => r.RowIndex).ToList();
        grid.Runs = grid.Runs
            .OrderByDescending(r => r.StartedAtUtc)
            .Take(MaxRunsInGraph)
            .ToList();
        return grid;
    }

    private static int ReadCreditsPerRow(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (doc.RootElement.TryGetProperty("creditsPerRow", out var el) && el.TryGetInt32(out var n))
                return Math.Max(0, n);
        }
        catch (JsonException)
        {
            // fall through
        }
        return 1;
    }

    private static string ReadAgentCapability(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (doc.RootElement.TryGetProperty("columns", out var cols)
                && cols.ValueKind == JsonValueKind.Array)
            {
                foreach (var col in cols.EnumerateArray())
                {
                    if (col.TryGetProperty("kind", out var kind)
                        && kind.GetString() == "agent"
                        && col.TryGetProperty("capability", out var cap))
                    {
                        var value = cap.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return "faq-generator";
    }

    private static string ReadExecutionNote(string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
            if (doc.RootElement.TryGetProperty("executionNote", out var el))
            {
                var note = el.GetString();
                if (!string.IsNullOrWhiteSpace(note))
                    return note;
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return DefaultExecutionNote;
    }

    private static string ReadTopicOrFirstInput(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("topic", out var topic)
                    && topic.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(topic.GetString()))
                    return topic.GetString()!;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                        return prop.Value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return "(empty)";
    }

    public sealed record GridListItem(
        Guid Id, string OwnerUserId, string Name, string Description, string Status,
        DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string ConfigJson,
        int RowCount, string? LastRunStatus);

    public sealed record CreateGridCommand(
        string OwnerUserId, string Name, string? Description = null, string? Status = null,
        string? ConfigJson = null, bool? SeedDemo = null, string? Capability = null);

    public sealed record PatchGridCommand(
        string OwnerUserId, string? Name = null, string? Description = null, string? Status = null,
        string? ConfigJson = null);

    public sealed record CreateGridRowCommand(
        string OwnerUserId, string? InputJson = null);

    public sealed record CreateGridRowsBulkCommand(
        string OwnerUserId, IReadOnlyList<string> InputJsons);

    public sealed record CreateGridRunCommand(
        string OwnerUserId, string? Mode = null, int? SampleSize = null, string? ActorUserId = null,
        IReadOnlyDictionary<string, string>? RowArtifactJsonByRowId = null);
}
