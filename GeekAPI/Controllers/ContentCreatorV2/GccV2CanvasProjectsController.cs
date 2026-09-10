using System.Text.Json;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

/// <summary>Owner-scoped durable multi-asset canvas projects.</summary>
[ApiController]
[Route("api/geek-content-creator-v2/projects")]
public sealed class GccV2CanvasProjectsController(
    ICurrentUserContext user,
    HttpGccV2Repository repo) : ControllerBase
{
    private const string ContractVersion = "gcc-canvas-project.v1";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string Owner => user.UserId.ToString("D");

    [HttpGet]
    public async Task<ActionResult<object>> List(CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var items = await repo.ListCanvasProjectsAsync(Owner, ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            projects = items.Select(Summary),
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var project = await repo.GetCanvasProjectAsync(id, Owner, ct);
        return project is null ? NotFound() : Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(CreatePublicProjectRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "name is required." });

        if (request.SeedDemo == true)
        {
            var seeded = await SeedEvidenceEngineLaunchAsync(Owner, request, ct);
            return CreatedAtAction(nameof(Get), new { id = seeded.Id }, new
            {
                contractVersion = ContractVersion,
                project = Detail(seeded),
            });
        }

        var created = await repo.CreateCanvasProjectAsync(new(
            Owner,
            request.Name.Trim(),
            request.Description?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(request.Status) ? "planning" : request.Status.Trim()), ct);
        var project = await repo.GetCanvasProjectAsync(created.Id, Owner, ct)
            ?? throw new InvalidOperationException("Created project could not be reloaded.");
        return CreatedAtAction(nameof(Get), new { id = project.Id }, new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
        });
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<object>> Patch(Guid id, PatchPublicProjectRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        var updated = await repo.PatchCanvasProjectAsync(id, new(
            Owner, request.Name, request.Description, request.Status), ct);
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(updated),
        });
    }

    [HttpPost("{id:guid}/assets")]
    public async Task<ActionResult<object>> CreateAsset(
        Guid id, CreatePublicAssetRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title is required." });
        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        await repo.CreateCanvasAssetAsync(id, new(
            Owner, request.Title.Trim(), request.Kind, request.ParentAssetIds), ct);
        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
        });
    }

    [HttpPost("{id:guid}/assets/from-task-artifact")]
    public async Task<ActionResult<object>> AttachFromTaskArtifact(
        Guid id, AttachFromTaskArtifactRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (request.RunId == Guid.Empty || request.ArtifactVersionId == Guid.Empty)
            return BadRequest(new { error = "runId and artifactVersionId are required." });

        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();

        var run = await repo.GetTaskRunAsync(request.RunId, Owner, ct);
        if (run is null) return NotFound(new { error = "Task run not found." });
        if (!string.Equals(run.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "Only succeeded task runs can be attached to a project." });

        var artifact = (run.Artifacts ?? [])
            .FirstOrDefault(item => item.Versions.Any(version => version.Id == request.ArtifactVersionId));
        var version = artifact?.Versions.FirstOrDefault(item => item.Id == request.ArtifactVersionId);
        if (artifact is null || version is null)
            return NotFound(new { error = "Artifact version was not found on the owned task run." });

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"Task report · {artifact.ArtifactType}"
            : request.Title.Trim();
        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "report" : request.Kind.Trim();

        var asset = await repo.CreateCanvasAssetAsync(id, new(Owner, title, kind), ct);
        var provenanceJson = JsonSerializer.Serialize(new
        {
            origin = "agent",
            note = $"Attached from task run {run.Id:D}.",
            sourceRunId = run.Id.ToString("D"),
            sourceArtifactVersionId = version.Id.ToString("D"),
            artifactType = artifact.ArtifactType,
            digest = version.Digest,
        }, JsonOpts);
        var summary = BuildAttachSummary(artifact.ArtifactType, version.Digest, version.PayloadJson);

        await repo.AppendCanvasAssetVersionAsync(id, asset.Id, new(
            Owner,
            Owner,
            "draft",
            summary,
            string.IsNullOrWhiteSpace(version.EvidenceJson) ? "[]" : version.EvidenceJson,
            provenanceJson), ct);

        var activityJson = PrependActivity(
            existing.ActivityJson,
            new
            {
                id = Guid.NewGuid().ToString("D"),
                kind = "handoff",
                actor = Owner,
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                message = $"Attached {title} from task artifact {artifact.ArtifactType}",
            });
        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);

        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
            assetId = asset.Id.ToString("D"),
        });
    }

    [HttpPost("{id:guid}/assets/from-grid")]
    public async Task<ActionResult<object>> AttachFromGrid(
        Guid id, AttachFromGridRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        if (request.GridId == Guid.Empty)
            return BadRequest(new { error = "gridId is required." });

        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();

        var grid = await repo.GetGridAsync(request.GridId, Owner, ct);
        if (grid is null) return NotFound(new { error = "Grid not found." });

        var selectedIds = (request.RowIds ?? [])
            .Where(rowId => rowId != Guid.Empty)
            .ToHashSet();
        var rows = grid.Rows
            .Where(row => string.Equals(row.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            .Where(row => selectedIds.Count == 0 || selectedIds.Contains(row.Id))
            .OrderBy(row => row.RowIndex)
            .ToList();
        if (rows.Count == 0)
            return Conflict(new { error = "No succeeded grid rows were available to attach." });

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "report" : request.Kind.Trim();
        var attached = new List<object>();
        var activityJson = existing.ActivityJson;

        foreach (var row in rows)
        {
            var topic = ReadGridTopic(row.InputJson);
            var preview = ReadGridResultPreview(row.OutputJson);
            var title = string.IsNullOrWhiteSpace(request.TitlePrefix)
                ? $"{grid.Name} · {topic}"
                : $"{request.TitlePrefix.Trim()} · {topic}";

            var asset = await repo.CreateCanvasAssetAsync(id, new(Owner, title, kind), ct);
            var taskRunId = ReadGridOutputString(row.OutputJson, "taskRunId");
            var artifactType = ReadGridOutputString(row.OutputJson, "artifactType");
            var provenanceJson = JsonSerializer.Serialize(new
            {
                origin = "agent",
                note = $"Attached from grid {grid.Name} row {row.RowIndex + 1}.",
                sourceGridId = grid.Id.ToString("D"),
                sourceGridRowId = row.Id.ToString("D"),
                sourceRunId = string.IsNullOrWhiteSpace(taskRunId) ? null : taskRunId,
                artifactType = string.IsNullOrWhiteSpace(artifactType) ? null : artifactType,
            }, JsonOpts);
            var summary = string.IsNullOrWhiteSpace(preview)
                ? $"Grid output for {topic}."
                : preview;

            await repo.AppendCanvasAssetVersionAsync(id, asset.Id, new(
                Owner,
                Owner,
                "draft",
                summary,
                "[]",
                provenanceJson), ct);

            activityJson = PrependActivity(
                activityJson,
                new
                {
                    id = Guid.NewGuid().ToString("D"),
                    kind = "handoff",
                    actor = Owner,
                    occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                    message = $"Attached {title} from grid {grid.Name}",
                });
            attached.Add(new { assetId = asset.Id.ToString("D"), title, rowId = row.Id.ToString("D") });
        }

        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);
        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
            attachedCount = attached.Count,
            attached,
        });
    }

    [HttpPost("{id:guid}/assets/{assetId:guid}/versions")]
    public async Task<ActionResult<object>> AppendVersion(
        Guid id, Guid assetId, AppendPublicVersionRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        if (existing.Assets.All(a => a.Id != assetId)) return NotFound();

        var evidenceJson = request.Evidence is null
            ? "[]"
            : JsonSerializer.Serialize(request.Evidence, JsonOpts);
        var provenanceJson = request.Provenance is null
            ? """{"origin":"human","note":""}"""
            : JsonSerializer.Serialize(request.Provenance, JsonOpts);

        var asset = existing.Assets.Single(a => a.Id == assetId);
        var nextVersion = (asset.Versions.Count == 0
            ? 0
            : asset.Versions.Max(v => v.VersionNumber)) + 1;
        var actor = string.IsNullOrWhiteSpace(request.CreatedBy) ? Owner : request.CreatedBy.Trim();

        await repo.AppendCanvasAssetVersionAsync(id, assetId, new(
            Owner,
            actor,
            request.Status,
            request.Summary,
            evidenceJson,
            provenanceJson), ct);

        var activityJson = PrependActivity(
            existing.ActivityJson,
            new
            {
                id = Guid.NewGuid().ToString("D"),
                kind = "versioned",
                actor,
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                message = $"Created {asset.Title} v{nextVersion}",
            });
        var status = (request.Status ?? "").Trim().ToLowerInvariant();
        if (status is "in-review" or "approved" or "published")
        {
            var (kind, message) = status switch
            {
                "in-review" => ("review", $"Requested review for {asset.Title}"),
                "approved" => ("approval", $"Approved {asset.Title}"),
                _ => ("publish", $"Published {asset.Title}"),
            };
            activityJson = PrependActivity(
                activityJson,
                new
                {
                    id = Guid.NewGuid().ToString("D"),
                    kind,
                    actor,
                    occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                    message,
                });
        }
        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);

        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
        });
    }

    [HttpPost("{id:guid}/assets/{assetId:guid}/comments")]
    public async Task<ActionResult<object>> AddComment(
        Guid id, Guid assetId, AddPublicCommentRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var message = (request.Message ?? "").Trim();
        if (message.Length == 0)
            return BadRequest(new { error = "message is required." });
        if (message.Length > 500)
            return BadRequest(new { error = "message must be 500 characters or fewer." });

        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        var asset = existing.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null) return NotFound();

        var actor = string.IsNullOrWhiteSpace(request.CreatedBy) ? Owner : request.CreatedBy.Trim();
        var activityJson = PrependActivity(
            existing.ActivityJson,
            new
            {
                id = Guid.NewGuid().ToString("D"),
                kind = "comment",
                actor,
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                message = $"Commented on {asset.Title}: {message}",
            });
        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);

        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
        });
    }

    [HttpPost("{id:guid}/assets/{assetId:guid}/to-grid")]
    public async Task<ActionResult<object>> ConvertToGrid(
        Guid id, Guid assetId, ConvertToGridRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        var asset = existing.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null) return NotFound();

        var latest = asset.Versions
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
        if (latest is null)
            return Conflict(new { error = "Asset has no versions to convert." });

        var capability = string.IsNullOrWhiteSpace(request.Capability)
            ? DefaultBatchCapability(asset.Kind)
            : request.Capability.Trim();
        if (capability is not ("faq-generator" or "pillar-outline"))
            return BadRequest(new { error = "capability must be faq-generator or pillar-outline." });

        var gridName = $"{asset.Title} batch";
        var description =
            $"Batch converted from Canvas asset “{asset.Title}” (v{latest.VersionNumber}) in project “{existing.Name}”.";
        var grid = await repo.CreateGridAsync(new(
            Owner,
            gridName,
            description,
            Status: "ready",
            Capability: capability), ct);

        var inputJson = JsonSerializer.Serialize(new
        {
            topic = asset.Title,
            sourceSummary = latest.Summary,
            sourceProjectId = existing.Id.ToString("D"),
            sourceAssetId = asset.Id.ToString("D"),
            sourceVersionId = latest.Id.ToString("D"),
            sourceVersionNumber = latest.VersionNumber,
        }, JsonOpts);
        await repo.CreateGridRowAsync(grid.Id, new(Owner, inputJson), ct);
        grid = await repo.GetGridAsync(grid.Id, Owner, ct)
            ?? throw new InvalidOperationException("Converted grid could not be reloaded.");

        var actor = string.IsNullOrWhiteSpace(request.CreatedBy) ? Owner : request.CreatedBy.Trim();
        var activityJson = PrependActivity(
            existing.ActivityJson,
            new
            {
                id = Guid.NewGuid().ToString("D"),
                kind = "handoff",
                actor,
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                message = $"Converted {asset.Title} to batch grid {grid.Name}",
            });
        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);

        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
            gridId = grid.Id.ToString("D"),
            gridName = grid.Name,
            capability,
            rowCount = grid.Rows.Count,
        });
    }

    private static string DefaultBatchCapability(string kind) =>
        kind.Trim().ToLowerInvariant() switch
        {
            "article" or "brief" => "pillar-outline",
            _ => "faq-generator",
        };

    [HttpPost("{id:guid}/assets/{assetId:guid}/to-agent")]
    public async Task<ActionResult<object>> SendToAgent(
        Guid id, Guid assetId, SendToAgentRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var existing = await repo.GetCanvasProjectAsync(id, Owner, ct);
        if (existing is null) return NotFound();
        var asset = existing.Assets.FirstOrDefault(a => a.Id == assetId);
        if (asset is null) return NotFound();

        var latest = asset.Versions
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();
        if (latest is null)
            return Conflict(new { error = "Asset has no versions to send." });

        var capabilityId = string.IsNullOrWhiteSpace(request.CapabilityId)
            ? DefaultBatchCapability(asset.Kind)
            : request.CapabilityId.Trim();
        if (capabilityId is not ("faq-generator" or "pillar-outline" or "citable-claims"))
        {
            return BadRequest(new
            {
                error = "capabilityId must be faq-generator, pillar-outline, or citable-claims.",
            });
        }

        var agentLabel = capabilityId switch
        {
            "pillar-outline" => "Pillar Article Outline",
            "citable-claims" => "Citable Claims",
            _ => "FAQ Generator",
        };

        var actor = string.IsNullOrWhiteSpace(request.CreatedBy) ? Owner : request.CreatedBy.Trim();
        var activityJson = PrependActivity(
            existing.ActivityJson,
            new
            {
                id = Guid.NewGuid().ToString("D"),
                kind = "handoff",
                actor,
                occurredAt = DateTimeOffset.UtcNow.ToString("O"),
                message = $"Sent {asset.Title} to {agentLabel}",
            });
        await repo.PatchCanvasProjectAsync(id, new(Owner, ActivityJson: activityJson), ct);

        var query = new List<string>
        {
            $"fromCanvasProjectId={Uri.EscapeDataString(existing.Id.ToString("D"))}",
            $"fromCanvasAssetId={Uri.EscapeDataString(asset.Id.ToString("D"))}",
            $"fromCanvasVersionId={Uri.EscapeDataString(latest.Id.ToString("D"))}",
            $"topic={Uri.EscapeDataString(asset.Title)}",
        };
        if (!string.IsNullOrWhiteSpace(latest.Summary))
            query.Add($"sourceContent={Uri.EscapeDataString(latest.Summary)}");

        var project = await repo.GetCanvasProjectAsync(id, Owner, ct)
            ?? throw new InvalidOperationException("Project could not be reloaded.");
        return Ok(new
        {
            contractVersion = ContractVersion,
            project = Detail(project),
            capabilityId,
            agentLabel,
            redirectPath = $"/task-agents/{Uri.EscapeDataString(capabilityId)}?{string.Join("&", query)}",
        });
    }

    private async Task<GccV2CanvasProjectDto> SeedEvidenceEngineLaunchAsync(
        string owner, CreatePublicProjectRequest request, CancellationToken ct)
    {
        var activity = JsonSerializer.Serialize(new object[]
        {
            new { id = "activity-4", kind = "handoff", actor = "Content Producer", occurredAt = "2026-09-08T18:42:00.000Z", message = "Handed article proof points to Customer launch email" },
            new { id = "activity-3", kind = "review", actor = "Maya Chen", occurredAt = "2026-09-07T15:30:00.000Z", message = "Requested review for Reliable content operations" },
            new { id = "activity-2", kind = "handoff", actor = "Maya Chen", occurredAt = "2026-09-07T10:05:00.000Z", message = "Adapted article into Launch carousel" },
            new { id = "activity-1", kind = "created", actor = "Jeff Martin", occurredAt = "2026-09-03T14:00:00.000Z", message = "Created the project and strategy brief" },
        }, JsonOpts);

        var project = await repo.CreateCanvasProjectAsync(new(
            owner,
            string.IsNullOrWhiteSpace(request.Name) ? "Evidence Engine launch" : request.Name.Trim(),
            string.IsNullOrWhiteSpace(request.Description)
                ? "A coordinated launch package built from one approved strategy brief."
                : request.Description.Trim(),
            string.IsNullOrWhiteSpace(request.Status) ? "review" : request.Status.Trim(),
            activity), ct);

        var brief = await repo.CreateCanvasAssetAsync(project.Id, new(owner, "Launch strategy brief", "brief"), ct);
        await repo.AppendCanvasAssetVersionAsync(project.Id, brief.Id, new(
            owner, "Jeff Martin", "draft",
            "Initial positioning, audience, and launch outcomes.",
            """[{"id":"ev-interviews","label":"Customer interview synthesis","source":"Brand & Source Library"}]""",
            """{"origin":"human","note":"Authored from stakeholder workshop notes."}"""), ct);
        await repo.AppendCanvasAssetVersionAsync(project.Id, brief.Id, new(
            owner, "Maya Chen", "approved",
            "Approved positioning with proof points and channel handoffs.",
            """[{"id":"ev-interviews","label":"Customer interview synthesis","source":"Brand & Source Library"},{"id":"ev-benchmark","label":"Campaign benchmark report","source":"Research corpus"}]""",
            """{"origin":"mixed","agent":"Marketing Strategist","model":"o3","note":"Human-edited strategy suggestions."}"""), ct);

        var article = await repo.CreateCanvasAssetAsync(
            project.Id, new(owner, "Reliable content operations", "article", [brief.Id]), ct);
        await repo.AppendCanvasAssetVersionAsync(project.Id, article.Id, new(
            owner, "Content Producer", "in-review",
            "Long-form launch narrative with evidence-backed operational guidance.",
            """[{"id":"ev-benchmark","label":"Campaign benchmark report","source":"Research corpus"},{"id":"ev-docs","label":"Evidence Engine product notes","source":"Direct upload"}]""",
            """{"origin":"agent","agent":"Content Producer 3.0.0","model":"o1-pro","note":"Generated from launch brief v2; citations verified."}"""), ct);

        var social = await repo.CreateCanvasAssetAsync(
            project.Id, new(owner, "Launch carousel", "social", [article.Id]), ct);
        await repo.AppendCanvasAssetVersionAsync(project.Id, social.Id, new(
            owner, "Maya Chen", "draft",
            "Six-slide narrative adapted from the pillar article.",
            """[{"id":"ev-docs","label":"Evidence Engine product notes","source":"Direct upload"}]""",
            """{"origin":"mixed","agent":"Content Producer 3.0.0","model":"o3","note":"Adapted from article v1 and edited by Maya."}"""), ct);

        var email = await repo.CreateCanvasAssetAsync(
            project.Id, new(owner, "Customer launch email", "email", [brief.Id, article.Id]), ct);
        await repo.AppendCanvasAssetVersionAsync(project.Id, email.Id, new(
            owner, "Content Producer", "in-review",
            "Concise customer announcement with article handoff.",
            """[{"id":"ev-interviews","label":"Customer interview synthesis","source":"Brand & Source Library"}]""",
            """{"origin":"agent","agent":"Content Producer 3.0.0","model":"o3","note":"Generated from approved brief and article draft."}"""), ct);

        return await repo.GetCanvasProjectAsync(project.Id, owner, ct)
            ?? throw new InvalidOperationException("Seeded project could not be reloaded.");
    }

    private static object Summary(GccV2CanvasProjectListItemDto p) => new
    {
        id = p.Id.ToString("D"),
        name = p.Name,
        description = p.Description,
        status = p.Status,
        updatedAt = p.UpdatedAtUtc.ToString("O"),
        owner = p.OwnerUserId,
        collaborators = Array.Empty<string>(),
        persistence = "server",
        assetCount = p.AssetCount,
    };

    private static object Detail(GccV2CanvasProjectDto p) => new
    {
        id = p.Id.ToString("D"),
        name = p.Name,
        description = p.Description,
        status = p.Status,
        updatedAt = p.UpdatedAtUtc.ToString("O"),
        owner = p.OwnerUserId,
        collaborators = Array.Empty<string>(),
        persistence = "server",
        activity = ParseJson(p.ActivityJson, "[]"),
        assets = p.Assets
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new
            {
                id = a.Id.ToString("D"),
                title = a.Title,
                kind = a.Kind,
                parentAssetIds = ParseGuidArray(a.ParentAssetIdsJson)
                    .Select(g => g.ToString("D"))
                    .ToArray(),
                versions = a.Versions
                    .OrderBy(v => v.VersionNumber)
                    .Select(v => new
                    {
                        id = v.Id.ToString("D"),
                        version = v.VersionNumber,
                        createdAt = v.CreatedAtUtc.ToString("O"),
                        createdBy = v.CreatedBy,
                        status = v.Status,
                        summary = v.Summary,
                        evidence = ParseJson(v.EvidenceJson, "[]"),
                        provenance = ParseJson(v.ProvenanceJson, "{}"),
                    })
                    .ToArray(),
            })
            .ToArray(),
    };

    private static string PrependActivity(string existingJson, object entry)
    {
        var items = new List<object> { entry };
        try
        {
            using var doc = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(existingJson) ? "[]" : existingJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    items.Add(JsonSerializer.Deserialize<object>(el.GetRawText(), JsonOpts)!);
            }
        }
        catch (JsonException)
        {
            // Keep only the new entry when stored activity is malformed.
        }

        return JsonSerializer.Serialize(items, JsonOpts);
    }

    private static JsonElement ParseJson(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? fallback : json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(fallback);
            return doc.RootElement.Clone();
        }
    }

    private static string BuildAttachSummary(string artifactType, string digest, string payloadJson)
    {
        var digestHint = string.IsNullOrWhiteSpace(digest)
            ? "no digest"
            : digest.Length > 12 ? digest[..12] : digest;
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            if (document.RootElement.TryGetProperty("overallScore", out var score)
                && score.ValueKind == JsonValueKind.Number)
            {
                return $"{artifactType} attached from a task agent (score {score.GetRawText()}, {digestHint}).";
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic summary.
        }

        return $"{artifactType} attached from a task agent ({digestHint}).";
    }

    private static string ReadGridTopic(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("topic", out var topic)
                && topic.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(topic.GetString()))
            {
                return topic.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall through.
        }

        return "Untitled topic";
    }

    private static string ReadGridResultPreview(string? outputJson)
    {
        return ReadGridOutputString(outputJson, "result");
    }

    private static string ReadGridOutputString(string? outputJson, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(outputJson) ? "{}" : outputJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Fall through.
        }

        return "";
    }

    private static IReadOnlyList<Guid> ParseGuidArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public sealed record CreatePublicProjectRequest(
        string Name, string? Description = null, string? Status = null, bool? SeedDemo = null);

    public sealed record PatchPublicProjectRequest(
        string? Name = null, string? Description = null, string? Status = null);

    public sealed record CreatePublicAssetRequest(
        string Title, string? Kind = null, IReadOnlyList<Guid>? ParentAssetIds = null);

    public sealed record AttachFromTaskArtifactRequest(
        Guid RunId, Guid ArtifactVersionId, string? Title = null, string? Kind = null);

    public sealed record AttachFromGridRequest(
        Guid GridId, IReadOnlyList<Guid>? RowIds = null, string? TitlePrefix = null, string? Kind = null);

    public sealed record AppendPublicVersionRequest(
        string? CreatedBy = null, string? Status = null, string? Summary = null,
        JsonElement? Evidence = null, JsonElement? Provenance = null);

    public sealed record AddPublicCommentRequest(
        string Message, string? CreatedBy = null);

    public sealed record ConvertToGridRequest(
        string? Capability = null, string? CreatedBy = null);

    public sealed record SendToAgentRequest(
        string? CapabilityId = null, string? CreatedBy = null);
}
