using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Auth;
using GeekAPI.HttpClients;
using GeekAPI.Services.ContentCreatorV2.Generation;
using GeekAPI.Services.ContentCreatorV2.AgentTests;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.ContentCreatorV2;

[ApiController]
[Route("api/geek-content-creator-v2/agents")]
public sealed class GccV2AgentsController(
    ICurrentUserContext user,
    GccV2SkillAdminPolicy admin,
    HttpGccV2Repository repo,
    GccV2AgentTeamResolver teams,
    GccV2AgentTestWake testWake) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Catalog([FromQuery] string? contentType, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var agents = await repo.ListAgentsAsync("published", contentType, ct);
        return Ok(new
        {
            catalogVersion = GccV2AgentTeamResolver.CatalogVersion,
            selection = new { minimum = 1, exactlyOneProducer = true },
            agents = agents.Select(agent => (Agent: agent, Version: Latest(agent, "published")))
                .Where(x => x.Version is not null).Select(x => Summary(x.Agent, x.Version!)),
        });
    }

    [HttpGet("{agentId}")]
    public async Task<ActionResult<object>> Detail(string agentId, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        var agent = Guid.TryParse(agentId, out var id)
            ? await repo.GetAgentAsync(id, ct)
            : (await repo.ListAgentsAsync(ct: ct))
                .SingleOrDefault(x => string.Equals(x.Slug, agentId, StringComparison.OrdinalIgnoreCase));
        if (agent is null) return NotFound();
        var visible = agent.Versions.Where(x => x.State is "published" or "deprecated").ToList();
        var version = Latest(agent, "published") ?? visible.OrderByDescending(x => Semver(x.SemanticVersion)).FirstOrDefault();
        return version is null ? NotFound() : Ok(DetailVersion(agent, version));
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<object>> Resolve([FromBody] ResolveRequest request, CancellationToken ct)
    {
        if (!user.IsAuthenticated) return Unauthorized();
        try
        {
            var contentType = request.ContentTypes.FirstOrDefault() ?? "blog";
            var resolved = await teams.ResolveStableAsync(request.SelectedAgentIds, contentType, ct);
            foreach (var additional in request.ContentTypes.Skip(1).Distinct(StringComparer.OrdinalIgnoreCase))
                await teams.ResolveStableAsync(request.SelectedAgentIds, additional, ct);
            return Ok(new
            {
                snapshotVersion = resolved.Snapshot.SnapshotVersion,
                catalogVersion = resolved.Snapshot.CatalogVersion,
                snapshotDigest = resolved.Digest,
                resolvedAtUtc = resolved.Snapshot.ResolvedAtUtc,
                selectedAgentIds = resolved.Snapshot.Agents.Select(x => x.Slug),
                agents = resolved.Snapshot.Agents.Select(member => new
                {
                    id = member.Slug,
                    versionId = member.AgentVersionId,
                    name = member.Name,
                    version = member.Version,
                    digest = member.Digest,
                    description = "",
                    role = member.Participation.Select(x => x.Role).FirstOrDefault() ?? "contributor",
                    specialty = member.Slug,
                    status = "published",
                    supportedContentTypes = member.ContentTypes,
                    supportedStages = member.Participation.Select(x => x.Stage).Distinct(),
                    skillIds = member.Skills.Select(x => x.Slug),
                    pinnedSkills = member.Skills.Select(x => new
                    {
                        id = x.Slug, versionId = x.SkillVersionId, version = x.Version, digest = x.Digest,
                    }),
                }),
                skills = resolved.Snapshot.Agents.SelectMany(x => x.Skills)
                    .DistinctBy(x => x.SkillVersionId).Select(x => new
                    {
                        id = x.Slug, versionId = x.SkillVersionId, version = x.Version, digest = x.Digest,
                    }),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("admin")]
    public async Task<ActionResult<object>> Admin(CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var agents = await repo.ListAgentsAsync(ct: ct);
        var rows = new List<object>();
        foreach (var agent in agents)
        {
            var audit = await repo.GetAgentAuditAsync(agent.Id, ct);
            foreach (var version in agent.Versions)
                rows.Add(AdminDetail(agent, version, audit,
                    await repo.GetAgentTestHistoryAsync(version.Id, ct)));
        }
        return Ok(new { authorized = true, agents = rows });
    }

    [HttpPost("admin")]
    public async Task<ActionResult<object>> Create(
        [FromBody] CreateRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var displayName = request.DisplayName ?? request.Name;
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(request.Description)
            || string.IsNullOrWhiteSpace(request.Objective)
            || string.IsNullOrWhiteSpace(request.Instructions))
            return BadRequest(new { error = "name/displayName, description, objective, and instructions are required." });
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? Slugify(request.Specialty ?? displayName)
            : request.Slug;
        var skillVersionIds = request.SkillVersionIds?.Distinct().ToList() ?? [];
        if (skillVersionIds.Count == 0 && request.SkillIds is { Count: > 0 })
        {
            var skills = await repo.ListSkillsAsync("published", ct: ct);
            foreach (var skillId in request.SkillIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var package = skills.SingleOrDefault(x =>
                    string.Equals(x.Slug, skillId, StringComparison.OrdinalIgnoreCase));
                var pinnedVersion = package?.Versions.Where(x => x.State == "published")
                    .OrderByDescending(x => Semver(x.SemanticVersion)).FirstOrDefault();
                if (pinnedVersion is null)
                    return BadRequest(new { error = $"Published skill '{skillId}' was not found." });
                skillVersionIds.Add(pinnedVersion.Id);
            }
        }
        if (skillVersionIds.Count == 0)
            return BadRequest(new { error = "Exact skillVersionIds (or resolvable published skillIds) are required." });
        var role = string.IsNullOrWhiteSpace(request.Role) ? "contributor" : request.Role.ToLowerInvariant();
        var participation = request.StageParticipation?.ToList()
            ?? (request.Stages ?? DefaultStages(role))
                .Select((stage, order) => new CreateGccV2AgentStageParticipation(stage, role, order)).ToList();
        var contentTypes = (request.ContentTypes ?? request.SupportedContentTypes)?.ToList()
            ?? DefaultContentTypes();
        var allowedTools = (request.AllowedTools ?? request.Tools)?.ToList() ?? DefaultTools();
        if (request.ModelPolicy is not null
            && !string.Equals(request.ModelPolicy.Version, ContentModelPolicy.CurrentVersion, StringComparison.Ordinal))
            return BadRequest(new { error = $"modelPolicy.version must be {ContentModelPolicy.CurrentVersion}." });
        var allowedModels = (request.AllowedModels ?? request.ModelPolicy?.AllowedModels)?.ToList()
            ?? [ContentModelPolicy.O3, ContentModelPolicy.O1Pro];
        if (participation.Count == 0 || contentTypes.Count == 0 || allowedTools.Count == 0 || allowedModels.Count == 0)
            return BadRequest(new { error = "stages, contentTypes, allowedTools, and allowedModels cannot be empty." });
        var agent = await repo.CreateAgentAsync(new(
            slug, displayName, request.Description, request.IsFirstParty,
            Actor(), SourceIp(), HttpContext.TraceIdentifier), ct);
        var version = await repo.CreateAgentVersionAsync(agent.Id, new(
            request.SemanticVersion ?? "1.0.0", request.Instructions, contentTypes,
            allowedTools, allowedModels, skillVersionIds, participation,
            Actor(), SourceIp(), HttpContext.TraceIdentifier, request.Objective,
            request.ModelPolicy?.Version ?? ContentModelPolicy.CurrentVersion,
            request.ModelPolicy?.Profile ?? "default"), ct);
        return Ok(Summary(agent, version));
    }

    [HttpPatch("admin/{agentId:guid}")]
    public async Task<ActionResult<GccV2AgentDto>> Patch(
        Guid agentId, [FromBody] PatchRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.PatchAgentAsync(agentId, new(
            request.DisplayName, request.Description, Actor(), SourceIp(), HttpContext.TraceIdentifier), ct));
    }

    [HttpPost("admin/{agentId:guid}/versions")]
    public async Task<ActionResult<GccV2AgentVersionDto>> CreateVersion(
        Guid agentId, [FromBody] VersionRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.CreateAgentVersionAsync(agentId, new(
            request.SemanticVersion, request.Instructions, request.ContentTypes,
            request.AllowedTools, request.AllowedModels, request.SkillVersionIds,
            request.StageParticipation, Actor(), SourceIp(), HttpContext.TraceIdentifier,
            request.Objective, request.ModelPolicyVersion, request.ModelPolicyProfile), ct));
    }

    [HttpPatch("admin/versions/{versionId:guid}")]
    public async Task<ActionResult<GccV2AgentVersionDto>> UpdateDraft(
        Guid versionId, [FromBody] SuccessorRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.CreateAgentSuccessorAsync(versionId, new(
            request.SemanticVersion, request.Objective, request.Instructions,
            request.ContentTypes, request.AllowedTools, request.AllowedModels,
            request.SkillVersionIds, request.StageParticipation, request.ModelPolicyVersion,
            Actor(), SourceIp(), HttpContext.TraceIdentifier, request.ModelPolicyProfile), ct));
    }

    [HttpPost("admin/versions/{versionId:guid}/review")]
    [HttpPost("admin/{versionId:guid}/review")]
    public async Task<ActionResult<GccV2AgentVersionDto>> Review(
        Guid versionId, [FromBody] ReviewRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.ReviewAgentVersionAsync(versionId, new(
            request.Approve || string.Equals(request.Decision, "approve", StringComparison.OrdinalIgnoreCase),
            request.Notes, Actor(), SourceIp(), HttpContext.TraceIdentifier), ct));
    }

    [HttpPost("admin/versions/{versionId:guid}/test")]
    [HttpPost("admin/{versionId:guid}/test")]
    public async Task<ActionResult<GccV2AgentTestRunDto>> Test(
        Guid versionId, [FromBody] TestRequest? request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var run = await repo.QueueAgentTestRunAsync(versionId, new(
            request?.Scenario ?? "contract",
            request?.Input is { } input ? input.GetRawText() : "{}",
            Actor(), SourceIp(), HttpContext.TraceIdentifier), ct);
        testWake.Wake(run.Id);
        return Accepted(new { runId = run.Id, status = run.Status, testRun = run });
    }

    [HttpGet("admin/versions/{versionId:guid}/tests")]
    [HttpGet("admin/{versionId:guid}/tests")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentTestRunDto>>> TestHistory(
        Guid versionId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.GetAgentTestHistoryAsync(versionId, ct));
    }

    [HttpGet("admin/test-runs/{runId:guid}")]
    public async Task<ActionResult<GccV2AgentTestRunDto>> TestRun(Guid runId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var run = await repo.GetAgentTestRunAsync(runId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpPost("admin/test-runs/{runId:guid}/cancel")]
    public async Task<ActionResult<GccV2AgentTestRunDto>> CancelTestRun(Guid runId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        var run = await repo.PatchAgentTestRunAsync(runId, new(
            null, null, "cancellation-requested", null, null, null, true,
            Actor(), SourceIp(), HttpContext.TraceIdentifier), ct);
        testWake.Wake(run.Id);
        return Accepted(run);
    }

    [HttpPost("admin/versions/{versionId:guid}/{transition:regex(^publish|deprecate|revoke$)}")]
    [HttpPost("admin/{versionId:guid}/{transition:regex(^publish|deprecate|revoke$)}")]
    public async Task<ActionResult<GccV2AgentVersionDto>> Transition(
        Guid versionId, string transition, [FromBody] TransitionRequest? request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.TransitionAgentVersionAsync(versionId, transition, new(
            Actor(), request?.Reason, SourceIp(), HttpContext.TraceIdentifier), ct));
    }

    [HttpGet("admin/{agentId:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentAuditEventDto>>> Audit(
        Guid agentId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.GetAgentAuditAsync(agentId, ct));
    }

    [HttpGet("admin/versions/{versionId:guid}/findings")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentReviewFindingDto>>> Findings(
        Guid versionId, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.GetAgentFindingsAsync(versionId, ct));
    }

    [HttpPatch("admin/versions/{versionId:guid}/findings/{findingId:guid}")]
    public async Task<ActionResult<GccV2AgentReviewFindingDto>> PatchFinding(
        Guid versionId, Guid findingId, FindingDispositionRequest request, CancellationToken ct)
    {
        if (!IsAdmin(out var denied)) return denied;
        return Ok(await repo.PatchAgentFindingAsync(versionId, findingId, new(
            request.Disposition, request.ReviewerRationale, Actor(), SourceIp(),
            HttpContext.TraceIdentifier), ct));
    }

    private static object Summary(GccV2AgentDto agent, GccV2AgentVersionDto version) => new
    {
        id = agent.Slug, agentId = agent.Id, versionId = version.Id, name = agent.DisplayName,
        agent.Description, version = version.SemanticVersion, digest = version.VersionDigest,
        version.Objective,
        role = PrimaryRole(version), specialty = agent.Slug, status = version.State,
        origin = agent.IsFirstParty ? "first-party" : "admin",
        supportedContentTypes = Strings(version.ContentTypesJson),
        supportedStages = version.StageParticipation.Select(x => x.Stage).Distinct(),
        skillIds = version.Skills.Select(x => x.SkillVersion.Package.Slug),
        publishedAtUtc = version.PublishedAtUtc,
        deprecatedAtUtc = version.DeprecatedAtUtc,
        revokedAtUtc = version.RevokedAtUtc,
        skills = version.Skills.Select(x => new
        {
            id = x.SkillVersion.Package.Slug, version = x.SkillVersion.SemanticVersion,
            versionId = x.SkillVersionId, digest = x.SkillVersion.PackageSha256,
        }),
    };

    private static object DetailVersion(GccV2AgentDto agent, GccV2AgentVersionDto version) => new
    {
        id = agent.Slug, agentId = agent.Id, versionId = version.Id, name = agent.DisplayName,
        displayName = agent.DisplayName,
        agent.Description, version = version.SemanticVersion, digest = version.VersionDigest,
        version.Objective,
        role = PrimaryRole(version), specialty = agent.Slug, status = version.State,
        supportedContentTypes = Strings(version.ContentTypesJson),
        supportedStages = version.StageParticipation.Select(x => x.Stage).Distinct(),
        skillIds = version.Skills.Select(x => x.SkillVersion.Package.Slug),
        publishedAtUtc = version.PublishedAtUtc, deprecatedAtUtc = version.DeprecatedAtUtc,
        revokedAtUtc = version.RevokedAtUtc, version.Instructions,
        tools = Strings(version.AllowedToolsJson),
        models = Strings(version.AllowedModelsJson),
        participation = version.StageParticipation,
        version.Reviewer, version.ReviewNotes, version.TestResultJson,
        versions = agent.Versions.Where(x => x.State is "published" or "deprecated")
            .Select(x => new { participation = x.StageParticipation }),
    };

    private static IReadOnlyList<string> Strings(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];
    private static object AdminDetail(
        GccV2AgentDto agent, GccV2AgentVersionDto version,
        IReadOnlyList<GccV2AgentAuditEventDto> audit,
        IReadOnlyList<GccV2AgentTestRunDto> testRuns) => new
    {
        id = agent.Slug, agentId = agent.Id, versionId = version.Id, name = agent.DisplayName,
        agent.Description, version = version.SemanticVersion, digest = version.VersionDigest,
        version.Objective,
        role = PrimaryRole(version), specialty = agent.Slug, status = version.State,
        supportedContentTypes = Strings(version.ContentTypesJson),
        supportedStages = version.StageParticipation.Select(x => x.Stage).Distinct(),
        skillIds = version.Skills.Select(x => x.SkillVersion.Package.Slug),
        version.Instructions, tools = Strings(version.AllowedToolsJson),
        models = Strings(version.AllowedModelsJson),
        modelPolicy = new { version = version.ModelPolicyVersion, profile = version.ModelPolicyProfile,
            allowedModels = Strings(version.AllowedModelsJson) },
        skills = version.Skills.Select(x => new
        {
            id = x.SkillVersion.Package.Slug, versionId = x.SkillVersionId,
            version = x.SkillVersion.SemanticVersion, digest = x.SkillVersion.PackageSha256,
        }),
        participation = version.StageParticipation,
        findings = version.Findings ?? [],
        tests = testRuns.Select(run => new
        {
            id = run.Id, run.AgentVersionId, run.VersionDigest, run.Scenario, run.InputJson,
            run.Status, run.ProgressPercent, run.Phase, run.ResultJson, run.Error,
            run.RequestedBy, run.QueuedAtUtc, run.StartedAtUtc, run.CompletedAtUtc,
            run.UpdatedAtUtc, run.ClaimedByInstanceId, run.ClaimedAtUtc, run.LeaseUntilUtc,
            run.AttemptCount, run.RecoveryCount, run.CancellationRequestedAtUtc, run.CancelledAtUtc,
        }),
        audit = audit.Where(x => x.AgentVersionId is null || x.AgentVersionId == version.Id).Select(x => new
        {
            id = x.Id, action = x.Action, actor = x.Actor, atUtc = x.CreatedAtUtc,
            detail = x.DetailJson,
        }),
    };
    private static GccV2AgentVersionDto? Latest(GccV2AgentDto agent, string state) =>
        agent.Versions.Where(x => x.State == state)
            .OrderByDescending(x => Semver(x.SemanticVersion)).FirstOrDefault();
    private static string PrimaryRole(GccV2AgentVersionDto version) =>
        version.StageParticipation.Any(x => x.Role == "producer") ? "producer"
        : version.StageParticipation.Any(x => x.Role == "contributor") ? "contributor" : "reviewer";
    private static Version Semver(string value) =>
        Version.TryParse(value, out var parsed) ? parsed : new Version(0, 0, 0);
    private static string Slugify(string value) =>
        Regex.Replace(
            new string(value.Trim().ToLowerInvariant()
                .Select(x => char.IsLetterOrDigit(x) ? x : '-').ToArray()),
            "-+", "-").Trim('-');
    private static List<string> DefaultContentTypes() =>
        ["blog", "email", "linkedin-post", "linkedin-document", "social"];
    private static IReadOnlyList<string> DefaultStages(string role) => role switch
    {
        "producer" => ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"],
        "reviewer" => ["validation"],
        _ => ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis"],
    };
    private static List<string> DefaultTools() =>
        ["search_corpus", "load_evidence_page", "get_brief_context", "get_outline_context",
            "get_completed_section_summaries", "get_specialist_artifacts", "activate_skill",
            "read_skill_resource", "submit_contribution", "submit_review", "submit_research_plan",
            "submit_outline", "submit_section", "submit_repair", "submit_validation",
            "submit_final_synthesis"];
    private bool IsAdmin(out ActionResult denied)
    {
        denied = user.IsAuthenticated
            ? StatusCode(StatusCodes.Status403Forbidden, new { error = "Administrator authorization is required." })
            : Unauthorized();
        return admin.IsAuthorized(user);
    }
    private string Actor() => user.UserId.ToString("D");
    private string? SourceIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    public sealed record ResolveRequest(
        IReadOnlyList<string> SelectedAgentIds, IReadOnlyList<string> ContentTypes);
    public sealed record CreateRequest(
        string? Slug, string? DisplayName, string? Name, string Description,
        string Objective, string Instructions, string? Role = null, string? Specialty = null,
        string? SemanticVersion = null, IReadOnlyList<string>? SkillIds = null,
        IReadOnlyList<Guid>? SkillVersionIds = null, IReadOnlyList<string>? Stages = null,
        IReadOnlyList<string>? ContentTypes = null, IReadOnlyList<string>? SupportedContentTypes = null,
        IReadOnlyList<string>? AllowedTools = null, IReadOnlyList<string>? Tools = null,
        IReadOnlyList<string>? AllowedModels = null,
        AgentModelPolicyRequest? ModelPolicy = null,
        IReadOnlyList<CreateGccV2AgentStageParticipation>? StageParticipation = null,
        bool IsFirstParty = false);
    public sealed record AgentModelPolicyRequest(
        string Version, IReadOnlyList<string> AllowedModels, string Profile = "default");
    public sealed record PatchRequest(string? DisplayName, string? Description);
    public sealed record VersionRequest(
        string SemanticVersion, string Objective, string Instructions, IReadOnlyList<string> ContentTypes,
        IReadOnlyList<string> AllowedTools, IReadOnlyList<string> AllowedModels,
        IReadOnlyList<Guid> SkillVersionIds,
        IReadOnlyList<CreateGccV2AgentStageParticipation> StageParticipation,
        string ModelPolicyVersion = "content-model-policy.v1", string ModelPolicyProfile = "default");
    public sealed record ReviewRequest(bool Approve = false, string? Decision = null, string? Notes = null);
    public sealed record TestRequest(string? Scenario = null, JsonElement? Input = null);
    public sealed record TransitionRequest(string? Reason);
    public sealed record FindingDispositionRequest(string Disposition, string ReviewerRationale);
    public sealed record SuccessorRequest(
        string SemanticVersion, string? Objective = null, string? Instructions = null,
        IReadOnlyList<string>? ContentTypes = null, IReadOnlyList<string>? AllowedTools = null,
        IReadOnlyList<string>? AllowedModels = null, IReadOnlyList<Guid>? SkillVersionIds = null,
        IReadOnlyList<CreateGccV2AgentStageParticipation>? StageParticipation = null,
        string? ModelPolicyVersion = null, string? ModelPolicyProfile = null);
}
