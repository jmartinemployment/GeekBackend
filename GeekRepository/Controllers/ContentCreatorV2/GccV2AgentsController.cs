using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeekRepository.Auth;
using GeekRepository.Data;
using GeekRepository.Data.Entities.ContentCreatorV2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GeekRepository.Controllers.ContentCreatorV2;

[ApiController]
[Route("repo/content-creator-v2/agents")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class GccV2AgentsController(ContentCreatorV2DbContext db) : ControllerBase
{
    private static readonly HashSet<string> Roles = ["contributor", "producer", "reviewer"];
    private static readonly HashSet<string> RequiredProducerStages =
        ["researchPlanning", "outline", "section", "repair", "validation", "finalSynthesis", "complete"];

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GccV2Agent>>> List(
        [FromQuery] string? state, [FromQuery] string? contentType, CancellationToken ct)
    {
        var query = db.GccV2Agents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.LifecycleState == state);
        var agents = await IncludeGraph(query).OrderBy(x => x.Slug).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(contentType))
            agents = agents.Where(x => x.Versions.Any(v => v.State == "published"
                && (JsonSerializer.Deserialize<List<string>>(v.ContentTypesJson) ?? [])
                    .Contains(contentType, StringComparer.Ordinal))).ToList();
        return Ok(agents);
    }

    [HttpGet("{agentId:guid}")]
    public async Task<ActionResult<GccV2Agent>> Get(Guid agentId, CancellationToken ct)
    {
        var agent = await IncludeGraph(db.GccV2Agents.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == agentId, ct);
        return agent is null ? NotFound() : Ok(agent);
    }

    [HttpGet("{agentId:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentAuditEvent>>> Audit(Guid agentId, CancellationToken ct) =>
        Ok(await db.GccV2AgentAuditEvents.AsNoTracking().Where(x => x.AgentId == agentId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(ct));

    [HttpGet("versions/{versionId:guid}/findings")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentReviewFinding>>> Findings(
        Guid versionId, CancellationToken ct) =>
        Ok(await db.GccV2AgentReviewFindings.AsNoTracking()
            .Where(x => x.AgentVersionId == versionId).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct));

    [HttpPatch("versions/{versionId:guid}/findings/{findingId:guid}")]
    public async Task<ActionResult<GccV2AgentReviewFinding>> PatchFinding(
        Guid versionId, Guid findingId, PatchFindingCommand command, CancellationToken ct)
    {
        if (command.Disposition is not ("accepted" or "resolved" or "false_positive"))
            return BadRequest("Invalid finding disposition.");
        if (string.IsNullOrWhiteSpace(command.ReviewerRationale))
            return BadRequest("reviewerRationale is required.");
        var finding = await db.GccV2AgentReviewFindings.SingleOrDefaultAsync(
            x => x.Id == findingId && x.AgentVersionId == versionId, ct);
        if (finding is null) return NotFound();
        finding.Disposition = command.Disposition;
        finding.ReviewerRationale = command.ReviewerRationale.Trim();
        finding.DisposedAtUtc = DateTimeOffset.UtcNow;
        var version = await db.GccV2AgentVersions.SingleAsync(x => x.Id == versionId, ct);
        db.GccV2AgentAuditEvents.Add(Event(version.AgentId, versionId, command.Actor,
            "finding-disposition", version.State, version.State,
            JsonSerializer.Serialize(new { finding.Id, finding.Rule, finding.Disposition }),
            command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(finding);
    }

    [HttpPost]
    public async Task<ActionResult<GccV2Agent>> Create(CreateAgentCommand command, CancellationToken ct)
    {
        var slug = command.Slug.Trim().ToLowerInvariant();
        if (!ValidSlug(slug) || string.IsNullOrWhiteSpace(command.DisplayName)
            || string.IsNullOrWhiteSpace(command.Description)) return BadRequest("Valid slug, displayName, and description are required.");
        if (await db.GccV2Agents.AnyAsync(x => x.Slug == slug, ct)) return Conflict("Agent slug already exists.");
        var agent = new GccV2Agent
        {
            Slug = slug, DisplayName = command.DisplayName.Trim(), Description = command.Description.Trim(),
            IsFirstParty = command.IsFirstParty,
        };
        db.GccV2Agents.Add(agent);
        db.GccV2AgentAuditEvents.Add(Event(agent.Id, null, command.Actor, "create", null, "draft", null,
            command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { agentId = agent.Id }, agent);
    }

    [HttpPatch("{agentId:guid}")]
    public async Task<ActionResult<GccV2Agent>> Patch(Guid agentId, PatchAgentCommand command, CancellationToken ct)
    {
        var agent = await db.GccV2Agents.SingleOrDefaultAsync(x => x.Id == agentId, ct);
        if (agent is null) return NotFound();
        if (agent.LifecycleState is "published" or "deprecated" or "revoked")
            return Conflict("Published agent metadata is immutable.");
        if (!string.IsNullOrWhiteSpace(command.DisplayName)) agent.DisplayName = command.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(command.Description)) agent.Description = command.Description.Trim();
        agent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.GccV2AgentAuditEvents.Add(Event(agent.Id, null, command.Actor, "update", agent.LifecycleState,
            agent.LifecycleState, null, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(agent);
    }

    [HttpPost("{agentId:guid}/versions")]
    public async Task<ActionResult<GccV2AgentVersion>> CreateVersion(
        Guid agentId, CreateVersionCommand command, CancellationToken ct)
    {
        var agent = await db.GccV2Agents.SingleOrDefaultAsync(x => x.Id == agentId, ct);
        if (agent is null) return NotFound();
        var hasPublishedVersion = await db.GccV2AgentVersions
            .AnyAsync(x => x.AgentId == agentId && x.State == "published", ct);
        if (!ValidSemver(command.SemanticVersion) || string.IsNullOrWhiteSpace(command.Instructions))
            return BadRequest("semanticVersion and instructions are required.");
        if (command.ModelPolicyVersion != "content-model-policy.v1")
            return BadRequest("modelPolicyVersion must be content-model-policy.v1.");
        var modelPolicyProfile = string.IsNullOrWhiteSpace(command.ModelPolicyProfile)
            ? "default" : command.ModelPolicyProfile.Trim();
        var contentTypes = Normalize(command.ContentTypes);
        var tools = Normalize(command.AllowedTools);
        var models = Normalize(command.AllowedModels);
        if (contentTypes.Count == 0 || models.Count == 0 || command.StageParticipation.Count == 0)
            return BadRequest("contentTypes, allowedModels, and stageParticipation are required.");
        if (command.StageParticipation.Any(x => !Roles.Contains(x.Role) || string.IsNullOrWhiteSpace(x.Stage)))
            return BadRequest("Stage role must be contributor, producer, or reviewer.");
        if (command.StageParticipation.GroupBy(x => new { x.Stage, x.Role }).Any(x => x.Count() > 1))
            return BadRequest("Duplicate stage/role participation is prohibited.");
        var producerStages = command.StageParticipation.Where(x => x.Role == "producer")
            .Select(x => x.Stage).ToHashSet(StringComparer.Ordinal);
        if (producerStages.Count > 0 && !producerStages.SetEquals(RequiredProducerStages))
            return BadRequest("Producer versions must participate in every durable generation stage.");
        var skillIds = command.SkillVersionIds.Distinct().ToList();
        var skills = await db.GccV2SkillVersions.Where(x => skillIds.Contains(x.Id)).ToListAsync(ct);
        if (skills.Count != skillIds.Count || skills.Any(x => x.State != "published"))
            return Conflict("Every assigned skill version must exist and be published.");
        if (skills.Select(x => x.PackageId).Distinct().Count() != skills.Count)
            return Conflict("Only one exact version of a skill may be assigned to an agent version.");
        var canonical = JsonSerializer.Serialize(new
        {
            agentId, command.SemanticVersion,
            objective = string.IsNullOrWhiteSpace(command.Objective) ? command.Instructions.Trim() : command.Objective.Trim(),
            instructions = command.Instructions.Trim(), modelPolicyVersion = command.ModelPolicyVersion,
            modelPolicyProfile,
            contentTypes, tools, models, skillVersionIds = skillIds.Order(),
            stages = command.StageParticipation.OrderBy(x => x.Order).ThenBy(x => x.Stage).ThenBy(x => x.Role),
        });
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var version = new GccV2AgentVersion
        {
            AgentId = agentId, SemanticVersion = command.SemanticVersion,
            Objective = string.IsNullOrWhiteSpace(command.Objective) ? command.Instructions.Trim() : command.Objective.Trim(),
            ModelPolicyVersion = command.ModelPolicyVersion, ModelPolicyProfile = modelPolicyProfile,
            Instructions = command.Instructions.Trim(), ContentTypesJson = JsonSerializer.Serialize(contentTypes),
            AllowedToolsJson = JsonSerializer.Serialize(tools), AllowedModelsJson = JsonSerializer.Serialize(models),
            VersionDigest = digest,
            Skills = skillIds.Select((id, index) => new GccV2AgentVersionSkillVersion
                { SkillVersionId = id, Order = index }).ToList(),
            StageParticipation = command.StageParticipation.Select(x => new GccV2AgentStageParticipation
                { Stage = x.Stage.Trim(), Role = x.Role, Order = x.Order }).ToList(),
            Findings =
            [
                new GccV2AgentReviewFinding
                {
                    Severity = "info", Rule = "governance-contract",
                    Message = "Objective, instructions, exact skills, tools, stages, and model policy were digest-pinned.",
                    Disposition = "accepted", ReviewerRationale = "Deterministic creation check.",
                    DisposedAtUtc = DateTimeOffset.UtcNow,
                },
            ],
        };
        db.GccV2AgentVersions.Add(version);
        if (!hasPublishedVersion) agent.LifecycleState = "draft";
        agent.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.GccV2AgentAuditEvents.Add(Event(agent.Id, version.Id, command.Actor, "create-version", null,
            "draft", JsonSerializer.Serialize(new { version.VersionDigest }), command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { agentId }, version);
    }

    [HttpPost("versions/{versionId:guid}/successor")]
    public async Task<ActionResult<GccV2AgentVersion>> CreateSuccessor(
        Guid versionId, CreateSuccessorCommand command, CancellationToken ct)
    {
        var source = await db.GccV2AgentVersions.AsNoTracking()
            .Include(x => x.Skills).Include(x => x.StageParticipation)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (source is null) return NotFound();
        if (source.State is not ("published" or "deprecated"))
            return Conflict("Only immutable published/deprecated versions use successor editing.");
        return await CreateVersion(source.AgentId, new(
            command.SemanticVersion,
            command.Instructions ?? source.Instructions,
            command.ContentTypes ?? JsonSerializer.Deserialize<List<string>>(source.ContentTypesJson) ?? [],
            command.AllowedTools ?? JsonSerializer.Deserialize<List<string>>(source.AllowedToolsJson) ?? [],
            command.AllowedModels ?? JsonSerializer.Deserialize<List<string>>(source.AllowedModelsJson) ?? [],
            command.SkillVersionIds ?? source.Skills.OrderBy(x => x.Order).Select(x => x.SkillVersionId).ToList(),
            command.StageParticipation ?? source.StageParticipation.OrderBy(x => x.Order)
                .Select(x => new StageParticipationCommand(x.Stage, x.Role, x.Order)).ToList(),
            command.Actor, command.SourceIp, command.RequestId,
            command.Objective ?? source.Objective,
            command.ModelPolicyVersion ?? source.ModelPolicyVersion,
            command.ModelPolicyProfile ?? source.ModelPolicyProfile), ct);
    }

    [HttpPost("versions/{versionId:guid}/review")]
    public Task<ActionResult<GccV2AgentVersion>> Review(
        Guid versionId, ReviewVersionCommand command, CancellationToken ct) =>
        ReviewCore(versionId, command, ct);

    [HttpPost("versions/{versionId:guid}/test-runs")]
    public async Task<ActionResult<GccV2AgentTestRun>> QueueTest(
        Guid versionId, QueueTestRunCommand command, CancellationToken ct)
    {
        var version = await db.GccV2AgentVersions.Include(x => x.Agent)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.State == "revoked") return Conflict("Revoked versions cannot be tested.");
        if (version.State is not ("approved" or "published" or "deprecated"))
            return Conflict("Version must be approved before testing.");
        var run = new GccV2AgentTestRun
        {
            AgentVersionId = version.Id,
            VersionDigest = version.VersionDigest,
            Scenario = string.IsNullOrWhiteSpace(command.Scenario) ? "contract" : command.Scenario.Trim(),
            InputJson = string.IsNullOrWhiteSpace(command.InputJson) ? "{}" : command.InputJson,
            RequestedBy = command.Actor,
        };
        db.GccV2AgentTestRuns.Add(run);
        db.GccV2AgentAuditEvents.Add(Event(version.AgentId, version.Id, command.Actor,
            "test-queued", version.State, version.State,
            JsonSerializer.Serialize(new { run.Id, run.Scenario, run.VersionDigest }),
            command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return AcceptedAtAction(nameof(GetTestRun), new { runId = run.Id }, run);
    }

    [HttpGet("versions/{versionId:guid}/test-runs")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentTestRun>>> TestHistory(
        Guid versionId, CancellationToken ct) =>
        Ok(await db.GccV2AgentTestRuns.AsNoTracking().Where(x => x.AgentVersionId == versionId)
            .OrderByDescending(x => x.QueuedAtUtc).ToListAsync(ct));

    [HttpGet("test-runs/{runId:guid}")]
    public async Task<ActionResult<GccV2AgentTestRun>> GetTestRun(Guid runId, CancellationToken ct)
    {
        var run = await db.GccV2AgentTestRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId, ct);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("test-runs/by-status/{status}")]
    public async Task<ActionResult<IReadOnlyList<GccV2AgentTestRun>>> TestRunsByStatus(
        string status, [FromQuery] DateTimeOffset? leaseBefore, [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var query = db.GccV2AgentTestRuns.AsNoTracking().Where(x => x.Status == status);
        if (leaseBefore is not null)
            query = query.Where(x => x.LeaseUntilUtc != null && x.LeaseUntilUtc < leaseBefore);
        return Ok(await query.OrderBy(x => x.QueuedAtUtc).Take(limit).ToListAsync(ct));
    }

    [HttpPost("test-runs/{runId:guid}/claim")]
    public async Task<ActionResult<GccV2AgentTestRun>> ClaimTestRun(
        Guid runId, [FromQuery] string instanceId, [FromQuery] int leaseSeconds = 120,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return BadRequest("instanceId is required.");
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.AddSeconds(Math.Max(10, leaseSeconds));
        if (!db.Database.IsRelational())
        {
            var candidate = await db.GccV2AgentTestRuns.SingleOrDefaultAsync(x => x.Id == runId, ct);
            if (candidate is null) return NotFound();
            if (candidate.Status != "queued"
                && !(candidate.Status == "running" && candidate.LeaseUntilUtc < now))
                return Conflict(new { claimed = false });
            if (candidate.Status == "running") candidate.RecoveryCount++;
            candidate.Status = "running";
            candidate.Phase = "starting";
            candidate.StartedAtUtc ??= now;
            candidate.UpdatedAtUtc = now;
            candidate.ClaimedByInstanceId = instanceId;
            candidate.ClaimedAtUtc = now;
            candidate.LeaseUntilUtc = leaseUntil;
            candidate.AttemptCount++;
            await db.SaveChangesAsync(ct);
            return Ok(candidate);
        }
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE content_creator_v2.gcc_v2_agent_test_runs
            SET ""Status"" = 'running',
                ""Phase"" = 'starting',
                ""StartedAtUtc"" = COALESCE(""StartedAtUtc"", {now}),
                ""UpdatedAtUtc"" = {now},
                ""ClaimedByInstanceId"" = {instanceId},
                ""ClaimedAtUtc"" = {now},
                ""LeaseUntilUtc"" = {leaseUntil},
                ""AttemptCount"" = ""AttemptCount"" + 1,
                ""RecoveryCount"" = ""RecoveryCount"" + CASE WHEN ""Status"" = 'running' THEN 1 ELSE 0 END
            WHERE ""Id"" = {runId}
              AND (
                ""Status"" = 'queued'
                OR (""Status"" = 'running' AND ""LeaseUntilUtc"" < {now})
              )", ct);
        if (rows == 0)
            return await db.GccV2AgentTestRuns.AnyAsync(x => x.Id == runId, ct)
                ? Conflict(new { claimed = false }) : NotFound();
        return Ok((await db.GccV2AgentTestRuns.AsNoTracking()
            .SingleAsync(x => x.Id == runId, ct)));
    }

    [HttpPatch("test-runs/{runId:guid}")]
    public async Task<ActionResult<GccV2AgentTestRun>> PatchTestRun(
        Guid runId, PatchTestRunCommand command, CancellationToken ct)
    {
        var run = await db.GccV2AgentTestRuns.Include(x => x.AgentVersion)
            .SingleOrDefaultAsync(x => x.Id == runId, ct);
        if (run is null) return NotFound();
        if (run.Status is "passed" or "failed" or "cancelled") return Conflict("Test run is terminal.");
        if (command.Status is not null
            && command.Status is not ("queued" or "running" or "passed" or "failed" or "cancelled"))
            return BadRequest("Invalid test run status.");
        if (command.Status is not null) run.Status = command.Status;
        if (command.ProgressPercent is not null) run.ProgressPercent = Math.Clamp(command.ProgressPercent.Value, 0, 100);
        if (command.Phase is not null) run.Phase = command.Phase;
        if (command.ResultJson is not null) run.ResultJson = command.ResultJson;
        if (command.Error is not null) run.Error = command.Error;
        if (command.LeaseUntilUtc is not null) run.LeaseUntilUtc = command.LeaseUntilUtc;
        if (command.CancellationRequested is true) run.CancellationRequestedAtUtc = DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        run.UpdatedAtUtc = now;
        if (run.Status is "passed" or "failed" or "cancelled")
        {
            run.CompletedAtUtc = now;
            run.LeaseUntilUtc = null;
            run.ClaimedByInstanceId = null;
            if (run.Status == "cancelled") run.CancelledAtUtc = now;
            run.ProgressPercent = 100;
            run.AgentVersion.TestedAtUtc = now;
            run.AgentVersion.TestResultJson = run.ResultJson;
            db.GccV2AgentAuditEvents.Add(Event(run.AgentVersion.AgentId, run.AgentVersionId,
                command.Actor, $"test-{run.Status}", run.AgentVersion.State, run.AgentVersion.State,
                JsonSerializer.Serialize(new { run.Id, run.VersionDigest, run.Error }),
                command.SourceIp, command.RequestId));
        }
        await db.SaveChangesAsync(ct);
        return Ok(run);
    }

    [HttpPost("versions/{versionId:guid}/publish")]
    public Task<ActionResult<GccV2AgentVersion>> Publish(
        Guid versionId, TransitionVersionCommand command, CancellationToken ct) =>
        Transition(versionId, "approved", "published", "publish", command, ct, requireTest: true);

    [HttpPost("versions/{versionId:guid}/deprecate")]
    public Task<ActionResult<GccV2AgentVersion>> Deprecate(
        Guid versionId, TransitionVersionCommand command, CancellationToken ct) =>
        Transition(versionId, "published", "deprecated", "deprecate", command, ct);

    [HttpPost("versions/{versionId:guid}/revoke")]
    public Task<ActionResult<GccV2AgentVersion>> Revoke(
        Guid versionId, TransitionVersionCommand command, CancellationToken ct) =>
        Transition(versionId, null, "revoked", "revoke", command, ct);

    private async Task<ActionResult<GccV2AgentVersion>> ReviewCore(
        Guid versionId, ReviewVersionCommand command, CancellationToken ct)
    {
        var version = await db.GccV2AgentVersions.Include(x => x.Agent)
            .Include(x => x.Skills).ThenInclude(x => x.SkillVersion)
            .Include(x => x.Findings)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (version.State != "draft") return Conflict("Only draft versions may be reviewed.");
        if (command.Approve && version.Skills.Any(x => x.SkillVersion.State != "published"))
            return Conflict("Assigned skills must still be published at review time.");
        if (command.Approve && version.Findings.Any(x =>
                x.Severity is "high" or "critical" && x.Disposition == "unreviewed"))
            return Conflict("Every blocking governance finding requires disposition before approval.");
        var before = version.State;
        version.State = command.Approve ? "approved" : "draft";
        if (!await db.GccV2AgentVersions.AnyAsync(
                x => x.AgentId == version.AgentId && x.Id != version.Id && x.State == "published", ct))
            version.Agent.LifecycleState = version.State;
        version.ReviewedAtUtc = DateTimeOffset.UtcNow;
        version.Reviewer = command.Actor;
        version.ReviewNotes = command.Notes;
        db.GccV2AgentAuditEvents.Add(Event(version.AgentId, version.Id, command.Actor,
            command.Approve ? "approve" : "request-changes", before, version.State,
            null, command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    private async Task<ActionResult<GccV2AgentVersion>> Transition(
        Guid versionId, string? required, string next, string action,
        TransitionVersionCommand command, CancellationToken ct, bool requireTest = false)
    {
        var version = await db.GccV2AgentVersions.Include(x => x.Agent)
            .Include(x => x.Skills).ThenInclude(x => x.SkillVersion)
            .Include(x => x.Findings)
            .SingleOrDefaultAsync(x => x.Id == versionId, ct);
        if (version is null) return NotFound();
        if (next == "revoked")
        {
            if (version.State is not ("published" or "deprecated"))
                return Conflict("Only published or deprecated versions may be revoked.");
        }
        else if (version.State != required) return Conflict($"Version must be {required}.");
        if (requireTest)
        {
            var latest = await db.GccV2AgentTestRuns.AsNoTracking()
                .Where(x => x.AgentVersionId == version.Id)
                .OrderByDescending(x => x.QueuedAtUtc).ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
            if (latest is null || latest.Status != "passed"
                || !string.Equals(latest.VersionDigest, version.VersionDigest, StringComparison.Ordinal))
                return Conflict("The latest test run must pass for this exact immutable version digest.");
        }
        if (next == "published" && version.Skills.Any(x => x.SkillVersion.State != "published"))
            return Conflict("Assigned skill versions must be published.");
        if (next == "published" && version.Findings.Any(x =>
                x.Severity is "high" or "critical" && x.Disposition == "unreviewed"))
            return Conflict("Blocking governance findings must be disposed before publishing.");
        var before = version.State;
        version.State = next;
        version.Agent.LifecycleState = next == "published"
            || await db.GccV2AgentVersions.AnyAsync(
                x => x.AgentId == version.AgentId && x.Id != version.Id && x.State == "published", ct)
            ? "published"
            : next;
        var now = DateTimeOffset.UtcNow;
        if (next == "published") version.PublishedAtUtc = now;
        if (next == "deprecated") version.DeprecatedAtUtc = now;
        if (next == "revoked") version.RevokedAtUtc = now;
        db.GccV2AgentAuditEvents.Add(Event(version.AgentId, version.Id, command.Actor,
            action, before, next, JsonSerializer.Serialize(new { command.Reason }),
            command.SourceIp, command.RequestId));
        await db.SaveChangesAsync(ct);
        return Ok(version);
    }

    private static IQueryable<GccV2Agent> IncludeGraph(IQueryable<GccV2Agent> query) =>
        query.Include(x => x.Versions.OrderByDescending(v => v.CreatedAtUtc))
            .ThenInclude(x => x.StageParticipation)
            .Include(x => x.Versions).ThenInclude(x => x.Skills).ThenInclude(x => x.SkillVersion)
            .ThenInclude(x => x.Package)
            .Include(x => x.Versions).ThenInclude(x => x.Skills).ThenInclude(x => x.SkillVersion)
            .ThenInclude(x => x.Applicability)
            .Include(x => x.Versions).ThenInclude(x => x.Findings);

    private static GccV2AgentAuditEvent Event(
        Guid agentId, Guid? versionId, string actor, string action, string? before, string? after,
        string? detail, string? sourceIp, string? requestId) => new()
    {
        AgentId = agentId, AgentVersionId = versionId, Actor = actor, Action = action,
        BeforeState = before, AfterState = after, DetailJson = detail,
        SourceIp = sourceIp, RequestId = requestId,
    };

    private static bool ValidSlug(string value) =>
        value.Length is > 0 and <= 128 && value.All(x => char.IsAsciiLetterOrDigit(x) || x == '-')
        && !value.StartsWith('-') && !value.EndsWith('-');
    private static bool ValidSemver(string value) =>
        value.Split('.').Length == 3 && value.Split('.').All(x => int.TryParse(x, out _));
    private static List<string> Normalize(IReadOnlyList<string> values) =>
        values.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();

    public sealed record CreateAgentCommand(
        string Slug, string DisplayName, string Description, bool IsFirstParty,
        string Actor, string? SourceIp, string? RequestId);
    public sealed record PatchAgentCommand(
        string? DisplayName, string? Description, string Actor, string? SourceIp, string? RequestId);
    public sealed record CreateVersionCommand(
        string SemanticVersion, string Instructions, IReadOnlyList<string> ContentTypes,
        IReadOnlyList<string> AllowedTools, IReadOnlyList<string> AllowedModels,
        IReadOnlyList<Guid> SkillVersionIds, IReadOnlyList<StageParticipationCommand> StageParticipation,
        string Actor, string? SourceIp, string? RequestId,
        string Objective = "", string ModelPolicyVersion = "content-model-policy.v1",
        string ModelPolicyProfile = "default");
    public sealed record StageParticipationCommand(string Stage, string Role, int Order);
    public sealed record ReviewVersionCommand(
        bool Approve, string? Notes, string Actor, string? SourceIp, string? RequestId);
    public sealed record QueueTestRunCommand(
        string Scenario, string InputJson, string Actor, string? SourceIp, string? RequestId);
    public sealed record PatchTestRunCommand(
        string? Status, int? ProgressPercent, string? Phase, string? ResultJson, string? Error,
        DateTimeOffset? LeaseUntilUtc, bool? CancellationRequested,
        string Actor, string? SourceIp, string? RequestId);
    public sealed record PatchFindingCommand(
        string Disposition, string ReviewerRationale, string Actor, string? SourceIp, string? RequestId);
    public sealed record CreateSuccessorCommand(
        string SemanticVersion, string? Objective, string? Instructions,
        IReadOnlyList<string>? ContentTypes, IReadOnlyList<string>? AllowedTools,
        IReadOnlyList<string>? AllowedModels, IReadOnlyList<Guid>? SkillVersionIds,
        IReadOnlyList<StageParticipationCommand>? StageParticipation, string? ModelPolicyVersion,
        string Actor, string? SourceIp, string? RequestId, string? ModelPolicyProfile = null);
    public sealed record TransitionVersionCommand(
        string Actor, string? Reason, string? SourceIp, string? RequestId);
}
