using System.Net;
using System.Text;
using System.Text.Json;

namespace GeekAPI.HttpClients;

/// <summary>
/// GeekAPI's client to GeekRepository's <c>repo/content-creator-v2/*</c> routes.
/// Mirrors <see cref="HttpGccRepository"/>'s style; entirely separate from v1.
/// </summary>
public class HttpGccV2Repository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ILogger<HttpGccV2Repository> _logger;

    public HttpGccV2Repository(HttpClient http, ILogger<HttpGccV2Repository> logger)
    {
        _http = http;
        _logger = logger;
    }

    // Creates

    public Task<GccV2CreateDto?> GetCreateAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2CreateDto>($"repo/content-creator-v2/creates/{id}", ct);

    public Task<IReadOnlyList<GccV2CreateDto>> ListCreatesAsync(string? ownerUserId, CancellationToken ct = default)
    {
        var path = "repo/content-creator-v2/creates" +
            (string.IsNullOrWhiteSpace(ownerUserId) ? "" : $"?ownerUserId={Uri.EscapeDataString(ownerUserId)}");
        return GetListAsync<GccV2CreateDto>(path, ct);
    }

    public Task<GccV2CreateDto> CreateCreateAsync(CreateGccV2CreateCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2CreateDto>("repo/content-creator-v2/creates", command, ct);

    // Briefs

    public Task<GccV2BriefDto?> GetBriefAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2BriefDto>($"repo/content-creator-v2/briefs/{id}", ct);

    public Task<GccV2BriefDto> CreateBriefAsync(CreateGccV2BriefCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2BriefDto>("repo/content-creator-v2/briefs", command, ct);

    public Task<IReadOnlyList<GccV2BriefDto>> ListBriefsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2BriefDto>($"repo/content-creator-v2/briefs?createId={createId}", ct);

    public Task<GccV2BriefDto> PatchBriefAsync(Guid briefId, PatchGccV2BriefCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2BriefDto>($"repo/content-creator-v2/briefs/{briefId}", command, ct);

    // Jobs

    public Task<GccV2JobDto?> GetJobAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/{id}", ct);

    /// <summary>Latest job for a create — lets Canvas routes (keyed by create id) resolve "the job"
    /// without the caller already knowing its id.</summary>
    public Task<GccV2JobDto?> GetLatestJobByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/by-create/{createId}", ct);

    public Task<IReadOnlyList<GccV2JobDto>> ListJobsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/list-by-create/{createId}", ct);

    public Task<IReadOnlyList<GccV2JobDto>> GetJobsByStatusAsync(
        string status,
        DateTimeOffset? leaseBefore = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        var q = new List<string> { $"limit={limit}" };
        if (leaseBefore is not null) q.Add($"leaseBefore={Uri.EscapeDataString(leaseBefore.Value.ToString("O"))}");
        return GetListAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/by-status/{status}?{string.Join("&", q)}", ct);
    }

    public Task<GccV2JobDto> CreateJobAsync(CreateGccV2JobCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2JobDto>("repo/content-creator-v2/jobs", command, ct);

    public Task<GccV2JobDto> PatchJobAsync(Guid id, PatchGccV2JobCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2JobDto>($"repo/content-creator-v2/jobs/{id}", command, ct);

    /// <summary>Atomically patch the job and/or append one event in a single DB transaction.</summary>
    public Task<GccV2JobTransitionResultDto> ApplyJobTransitionAsync(
        Guid id,
        ApplyGccV2JobTransitionCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2JobTransitionResultDto>($"repo/content-creator-v2/jobs/{id}/transition", command, ct);

    /// <summary>Claims the job if pending or its lease expired. Null when not claimable (409).</summary>
    public async Task<GccV2JobDto?> ClaimJobAsync(Guid id, string instanceId, int leaseSeconds = 120, CancellationToken ct = default)
    {
        var path = $"repo/content-creator-v2/jobs/{id}/claim?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}";
        var res = await _http.PostAsync(path, content: null, ct);
        if (res.StatusCode == HttpStatusCode.Conflict) return null;
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<GccV2JobDto>(json, JsonOpts);
    }

    public Task<IReadOnlyList<GccV2JobEventDto>> GetJobEventsAsync(Guid id, int afterSeq = 0, CancellationToken ct = default) =>
        GetListAsync<GccV2JobEventDto>($"repo/content-creator-v2/jobs/{id}/events?afterSeq={afterSeq}", ct);

    public Task<GccV2JobEventDto> AppendJobEventAsync(Guid id, AppendGccV2JobEventCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2JobEventDto>($"repo/content-creator-v2/jobs/{id}/events", command, ct);

    public Task<IReadOnlyList<GccV2StageResultDto>> GetStageResultsAsync(Guid id, CancellationToken ct = default) =>
        GetListAsync<GccV2StageResultDto>($"repo/content-creator-v2/jobs/{id}/stage-results", ct);

    public Task<GccV2StageResultDto> AddStageResultAsync(Guid id, CreateGccV2StageResultCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2StageResultDto>($"repo/content-creator-v2/jobs/{id}/stage-results", command, ct);

    // Brand kits

    public Task<GccV2BrandKitDto?> GetBrandKitAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits/{id}", ct);

    /// <summary>Latest-first; callers typically want <c>.FirstOrDefault()</c> for "the current kit".</summary>
    public Task<IReadOnlyList<GccV2BrandKitDto>> ListBrandKitsByProfileAsync(Guid derivedFromProfileId, CancellationToken ct = default) =>
        GetListAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits?derivedFromProfileId={derivedFromProfileId}", ct);

    public Task<IReadOnlyList<GccV2BrandKitDto>> ListBrandKitsByOwnerAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2BrandKitDto>(
            $"repo/content-creator-v2/brand-kits?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2BrandKitDto> CreateBrandKitAsync(CreateGccV2BrandKitCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2BrandKitDto>("repo/content-creator-v2/brand-kits", command, ct);

    public Task<GccV2BrandKitDto> PatchBrandKitAsync(Guid id, PatchGccV2BrandKitCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2BrandKitDto>($"repo/content-creator-v2/brand-kits/{id}", command, ct);

    // Outlines

    public Task<GccV2OutlineDto?> GetOutlineAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines/{id}", ct);

    public Task<IReadOnlyList<GccV2OutlineDto>> ListOutlinesByBriefAsync(Guid briefId, CancellationToken ct = default) =>
        GetListAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines?briefId={briefId}", ct);

    public Task<GccV2OutlineDto> CreateOutlineAsync(CreateGccV2OutlineCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2OutlineDto>("repo/content-creator-v2/outlines", command, ct);

    public Task<GccV2OutlineDto> PatchOutlineAsync(Guid id, PatchGccV2OutlineCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2OutlineDto>($"repo/content-creator-v2/outlines/{id}", command, ct);

    // Guardrail rules

    public Task<IReadOnlyList<GccV2GuardrailRuleDto>> ListGuardrailRulesAsync(bool? enabled = true, CancellationToken ct = default)
    {
        var path = "repo/content-creator-v2/guardrail-rules" + (enabled is null ? "" : $"?enabled={enabled.Value.ToString().ToLowerInvariant()}");
        return GetListAsync<GccV2GuardrailRuleDto>(path, ct);
    }

    public Task<int> SeedDefaultGuardrailRulesAsync(CancellationToken ct = default)
    {
        return PostSeedCountAsync("repo/content-creator-v2/guardrail-rules/seed-defaults", ct);
    }

    // Publish records

    public Task<GccV2PublishRecordDto?> GetPublishRecordAsync(Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records/{id}", ct);

    /// <summary>Latest-first; the frontend renders these as a publish history for the create.</summary>
    public Task<IReadOnlyList<GccV2PublishRecordDto>> ListPublishRecordsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records?createId={createId}", ct);

    public Task<GccV2PublishRecordDto> CreatePublishRecordAsync(CreateGccV2PublishRecordCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PublishRecordDto>("repo/content-creator-v2/publish-records", command, ct);

    public Task<GccV2PublishRecordDto> PatchPublishRecordAsync(Guid id, PatchGccV2PublishRecordCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2PublishRecordDto>($"repo/content-creator-v2/publish-records/{id}", command, ct);

    // AI-visibility snapshots

    public Task<GccV2AiVisibilitySnapshotDto?> GetLatestAiVisibilitySnapshotAsync(Guid createId, CancellationToken ct = default) =>
        GetAsync<GccV2AiVisibilitySnapshotDto>($"repo/content-creator-v2/ai-visibility-snapshots/latest?createId={createId}", ct);

    /// <summary>Latest-first; the frontend renders these as an AI-visibility history for the create.</summary>
    public Task<IReadOnlyList<GccV2AiVisibilitySnapshotDto>> ListAiVisibilitySnapshotsByCreateAsync(Guid createId, CancellationToken ct = default) =>
        GetListAsync<GccV2AiVisibilitySnapshotDto>($"repo/content-creator-v2/ai-visibility-snapshots?createId={createId}", ct);

    public Task<GccV2AiVisibilitySnapshotDto> CreateAiVisibilitySnapshotAsync(CreateGccV2AiVisibilitySnapshotCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AiVisibilitySnapshotDto>("repo/content-creator-v2/ai-visibility-snapshots", command, ct);

    // Project-site crawl

    public Task<GccV2ProjectSiteCrawlRunDto?> GetProjectSiteCrawlRunAsync(Guid runId, CancellationToken ct = default) =>
        GetAsync<GccV2ProjectSiteCrawlRunDto>($"repo/content-creator-v2/project-site/runs/{runId}", ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlRunDto>> ListProjectSiteCrawlRunsByOwnerAsync(
        string ownerUserId, int limit = 50, CancellationToken ct = default) =>
        GetListAsync<GccV2ProjectSiteCrawlRunDto>(
            $"repo/content-creator-v2/project-site/runs?ownerUserId={Uri.EscapeDataString(ownerUserId)}&limit={limit}",
            ct);

    public Task<GccV2ProjectSiteCrawlRunDto?> GetLatestProjectSiteCrawlRunAsync(
        string ownerUserId,
        string siteUrl,
        CancellationToken ct = default) =>
        GetAsync<GccV2ProjectSiteCrawlRunDto>(
            $"repo/content-creator-v2/project-site/runs/latest?ownerUserId={Uri.EscapeDataString(ownerUserId)}" +
            $"&siteUrl={Uri.EscapeDataString(siteUrl)}",
            ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlRunDto>> GetProjectSiteCrawlRunsByStatusAsync(
        string status,
        int limit = 200,
        CancellationToken ct = default) =>
        GetListAsync<GccV2ProjectSiteCrawlRunDto>(
            $"repo/content-creator-v2/project-site/runs/by-status/{Uri.EscapeDataString(status)}?limit={limit}",
            ct);

    public Task<GccV2ProjectSiteCrawlRunDto> CreateProjectSiteCrawlRunAsync(
        CreateGccV2ProjectSiteCrawlRunCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2ProjectSiteCrawlRunDto>("repo/content-creator-v2/project-site/runs", command, ct);

    public Task<GccV2ProjectSiteCrawlRunDto> PatchProjectSiteCrawlRunAsync(
        Guid runId,
        PatchGccV2ProjectSiteCrawlRunCommand command,
        CancellationToken ct = default) =>
        PatchAsync<GccV2ProjectSiteCrawlRunDto>($"repo/content-creator-v2/project-site/runs/{runId}", command, ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesAsync(
        Guid runId,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default) =>
        GetListAsync<GccV2ProjectSiteCrawlPageDto>(
            $"repo/content-creator-v2/project-site/pages?runId={runId}&limit={limit}&offset={offset}",
            ct);

    public Task<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>> ListProjectSiteCrawlPagesBySeedsAsync(
        Guid runId,
        IReadOnlyList<string> seedUrls,
        CancellationToken ct = default)
    {
        if (seedUrls.Count == 0)
            return Task.FromResult<IReadOnlyList<GccV2ProjectSiteCrawlPageDto>>([]);

        var seedsParam = string.Join(",", seedUrls.Select(Uri.EscapeDataString));
        return GetListAsync<GccV2ProjectSiteCrawlPageDto>(
            $"repo/content-creator-v2/project-site/pages/by-seeds?runId={runId}&seeds={seedsParam}",
            ct);
    }

    public Task<GccV2ProjectSiteCrawlPageActivityDto?> GetProjectSiteCrawlPageActivityAsync(
        Guid runId,
        CancellationToken ct = default) =>
        GetAsync<GccV2ProjectSiteCrawlPageActivityDto>(
            $"repo/content-creator-v2/project-site/pages/activity?runId={runId}",
            ct);

    public Task<GccV2ProjectSiteCrawlPageBatchResult> CreateProjectSiteCrawlPagesBatchAsync(
        CreateGccV2ProjectSiteCrawlPageBatchCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2ProjectSiteCrawlPageBatchResult>("repo/content-creator-v2/project-site/pages/batch", command, ct);

    public Task CreateProjectSiteCrawlLinksBatchAsync(
        CreateGccV2ProjectSiteCrawlLinkBatchCommand command,
        CancellationToken ct = default) =>
        PostAsync<object>("repo/content-creator-v2/project-site/links/batch", command, ct);

    // Governed skills registry. All persistence remains behind GeekRepository.

    public Task<IReadOnlyList<GccV2SkillPackageDto>> ListSkillsAsync(
        string? state = null, string? contentType = null, string? stage = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(state)) query.Add($"state={Uri.EscapeDataString(state)}");
        if (!string.IsNullOrWhiteSpace(contentType)) query.Add($"contentType={Uri.EscapeDataString(contentType)}");
        if (!string.IsNullOrWhiteSpace(stage)) query.Add($"stage={Uri.EscapeDataString(stage)}");
        return GetListAsync<GccV2SkillPackageDto>(
            "repo/content-creator-v2/skills" + (query.Count == 0 ? "" : $"?{string.Join("&", query)}"), ct);
    }

    public Task<GccV2SkillPackageDto?> GetSkillAsync(Guid packageId, CancellationToken ct = default) =>
        GetAsync<GccV2SkillPackageDto>($"repo/content-creator-v2/skills/{packageId}", ct);

    public Task<IReadOnlyList<GccV2SkillAuditEventDto>> GetSkillAuditAsync(Guid packageId, CancellationToken ct = default) =>
        GetListAsync<GccV2SkillAuditEventDto>($"repo/content-creator-v2/skills/{packageId}/audit", ct);

    public Task<GccV2SkillPackageDto> ImportSkillAsync(ImportGccV2SkillCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2SkillPackageDto>("repo/content-creator-v2/skills/imports", command, ct);

    public Task<GccV2SkillVersionDto> ReviewSkillAsync(Guid versionId, ReviewGccV2SkillCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2SkillVersionDto>($"repo/content-creator-v2/skills/versions/{versionId}/review", command, ct);

    public Task<GccV2SkillReviewFindingDto> PatchSkillFindingAsync(
        Guid versionId, Guid findingId, PatchGccV2SkillFindingCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2SkillReviewFindingDto>(
            $"repo/content-creator-v2/skills/versions/{versionId}/findings/{findingId}", command, ct);

    public Task<GccV2SkillVersionDto> PublishSkillAsync(Guid versionId, TransitionGccV2SkillCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2SkillVersionDto>($"repo/content-creator-v2/skills/versions/{versionId}/publish", command, ct);

    public Task<GccV2SkillVersionDto> DeprecateSkillAsync(Guid versionId, TransitionGccV2SkillCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2SkillVersionDto>($"repo/content-creator-v2/skills/versions/{versionId}/deprecate", command, ct);

    public Task<GccV2SkillFileDto?> GetSkillFileAsync(Guid versionId, string path, CancellationToken ct = default) =>
        GetAsync<GccV2SkillFileDto>(
            $"repo/content-creator-v2/skills/versions/{versionId}/files/{Uri.EscapeDataString(path)}", ct);

    // Governed specialist agents.

    public Task<IReadOnlyList<GccV2AgentDto>> ListAgentsAsync(
        string? state = null, string? contentType = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(state)) query.Add($"state={Uri.EscapeDataString(state)}");
        if (!string.IsNullOrWhiteSpace(contentType)) query.Add($"contentType={Uri.EscapeDataString(contentType)}");
        return GetListAsync<GccV2AgentDto>(
            "repo/content-creator-v2/agents" + (query.Count == 0 ? "" : $"?{string.Join("&", query)}"), ct);
    }

    public Task<GccV2AgentDto?> GetAgentAsync(Guid agentId, CancellationToken ct = default) =>
        GetAsync<GccV2AgentDto>($"repo/content-creator-v2/agents/{agentId}", ct);
    public Task<IReadOnlyList<GccV2AgentAuditEventDto>> GetAgentAuditAsync(Guid agentId, CancellationToken ct = default) =>
        GetListAsync<GccV2AgentAuditEventDto>($"repo/content-creator-v2/agents/{agentId}/audit", ct);
    public Task<IReadOnlyList<GccV2AgentReviewFindingDto>> GetAgentFindingsAsync(
        Guid versionId, CancellationToken ct = default) =>
        GetListAsync<GccV2AgentReviewFindingDto>(
            $"repo/content-creator-v2/agents/versions/{versionId}/findings", ct);
    public Task<GccV2AgentReviewFindingDto> PatchAgentFindingAsync(
        Guid versionId, Guid findingId, PatchGccV2AgentFindingCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2AgentReviewFindingDto>(
            $"repo/content-creator-v2/agents/versions/{versionId}/findings/{findingId}", command, ct);
    public Task<GccV2AgentDto> CreateAgentAsync(CreateGccV2AgentCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentDto>("repo/content-creator-v2/agents", command, ct);
    public Task<GccV2AgentDto> PatchAgentAsync(Guid agentId, PatchGccV2AgentCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2AgentDto>($"repo/content-creator-v2/agents/{agentId}", command, ct);
    public Task<GccV2AgentVersionDto> CreateAgentVersionAsync(
        Guid agentId, CreateGccV2AgentVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentVersionDto>($"repo/content-creator-v2/agents/{agentId}/versions", command, ct);
    public Task<GccV2AgentVersionDto> CreateAgentSuccessorAsync(
        Guid versionId, CreateGccV2AgentSuccessorCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentVersionDto>(
            $"repo/content-creator-v2/agents/versions/{versionId}/successor", command, ct);
    public Task<GccV2AgentVersionDto> ReviewAgentVersionAsync(
        Guid versionId, ReviewGccV2AgentVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentVersionDto>($"repo/content-creator-v2/agents/versions/{versionId}/review", command, ct);
    public Task<GccV2AgentTestRunDto> QueueAgentTestRunAsync(
        Guid versionId, QueueGccV2AgentTestRunCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentTestRunDto>(
            $"repo/content-creator-v2/agents/versions/{versionId}/test-runs", command, ct);
    public Task<IReadOnlyList<GccV2AgentTestRunDto>> GetAgentTestHistoryAsync(
        Guid versionId, CancellationToken ct = default) =>
        GetListAsync<GccV2AgentTestRunDto>(
            $"repo/content-creator-v2/agents/versions/{versionId}/test-runs", ct);
    public Task<GccV2AgentTestRunDto?> GetAgentTestRunAsync(Guid runId, CancellationToken ct = default) =>
        GetAsync<GccV2AgentTestRunDto>($"repo/content-creator-v2/agents/test-runs/{runId}", ct);
    public Task<IReadOnlyList<GccV2AgentTestRunDto>> GetAgentTestRunsByStatusAsync(
        string status, DateTimeOffset? leaseBefore = null, int limit = 200, CancellationToken ct = default)
    {
        var path = $"repo/content-creator-v2/agents/test-runs/by-status/{Uri.EscapeDataString(status)}?limit={limit}";
        if (leaseBefore is not null)
            path += $"&leaseBefore={Uri.EscapeDataString(leaseBefore.Value.ToString("O"))}";
        return GetListAsync<GccV2AgentTestRunDto>(path, ct);
    }
    public Task<GccV2AgentTestRunDto> ClaimAgentTestRunAsync(
        Guid runId, string instanceId, int leaseSeconds = 120, CancellationToken ct = default) =>
        PostAsync<GccV2AgentTestRunDto>(
            $"repo/content-creator-v2/agents/test-runs/{runId}/claim?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}",
            new { }, ct);
    public Task<GccV2AgentTestRunDto> PatchAgentTestRunAsync(
        Guid runId, PatchGccV2AgentTestRunCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2AgentTestRunDto>(
            $"repo/content-creator-v2/agents/test-runs/{runId}", command, ct);
    public Task<GccV2AgentVersionDto> TransitionAgentVersionAsync(
        Guid versionId, string action, TransitionGccV2AgentVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2AgentVersionDto>($"repo/content-creator-v2/agents/versions/{versionId}/{action}", command, ct);

    // User-invokable task-agent application kernel. Separate from specialist agents above.

    public Task<IReadOnlyList<GccV2TaskAgentDefinitionDto>> ListTaskAgentsAsync(
        string? state = null, CancellationToken ct = default) =>
        GetListAsync<GccV2TaskAgentDefinitionDto>(
            "repo/content-creator-v2/task-agents"
            + (string.IsNullOrWhiteSpace(state) ? "" : $"?state={Uri.EscapeDataString(state)}"), ct);
    public Task<GccV2TaskAgentDefinitionDto?> GetTaskAgentAsync(
        string idOrCapability, CancellationToken ct = default) =>
        GetAsync<GccV2TaskAgentDefinitionDto>(
            $"repo/content-creator-v2/task-agents/{Uri.EscapeDataString(idOrCapability)}", ct);
    public Task<GccV2TaskAgentDefinitionDto> CreateTaskAgentAsync(
        CreateGccV2TaskAgentDefinitionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskAgentDefinitionDto>("repo/content-creator-v2/task-agents", command, ct);
    public Task<GccV2TaskAgentDefinitionDto> PatchTaskAgentAsync(
        Guid id, PatchGccV2TaskAgentDefinitionCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2TaskAgentDefinitionDto>($"repo/content-creator-v2/task-agents/{id}", command, ct);
    public Task<GccV2TaskAgentVersionDto> CreateTaskAgentVersionAsync(
        Guid id, CreateGccV2TaskAgentVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskAgentVersionDto>($"repo/content-creator-v2/task-agents/{id}/versions", command, ct);
    public Task<GccV2TaskAgentVersionDto> TransitionTaskAgentVersionAsync(
        Guid versionId, string transition, TransitionGccV2TaskAgentVersionCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2TaskAgentVersionDto>(
            $"repo/content-creator-v2/task-agents/versions/{versionId}/{transition}", command, ct);

    public Task<GccV2TaskAgentLibraryPreferenceDto> GetTaskAgentLibraryPreferencesAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2TaskAgentLibraryPreferenceDto>(
            $"repo/content-creator-v2/task-agents/library-preferences?ownerUserId={Uri.EscapeDataString(ownerUserId)}",
            ct)!;
    public Task<GccV2TaskAgentLibraryPreferenceDto> PutTaskAgentLibraryPreferencesAsync(
        PutGccV2TaskAgentLibraryPreferencesCommand command, CancellationToken ct = default) =>
        PutAsync<GccV2TaskAgentLibraryPreferenceDto>(
            "repo/content-creator-v2/task-agents/library-preferences", command, ct);

    public Task<IReadOnlyList<GccV2GscConnectionDto>> ListGscConnectionsAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2GscConnectionDto>(
            $"repo/content-creator-v2/gsc/connections?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2GscConnectionDto?> GetGscConnectionAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2GscConnectionDto>(
            $"repo/content-creator-v2/gsc/connections/{id:D}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2GscConnectionDto> UpsertGscConnectionAsync(
        UpsertGccV2GscConnectionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2GscConnectionDto>("repo/content-creator-v2/gsc/connections", command, ct);
    public Task DeleteGscConnectionAsync(Guid id, string ownerUserId, CancellationToken ct = default) =>
        DeleteAsync(
            $"repo/content-creator-v2/gsc/connections/{id:D}?ownerUserId={Uri.EscapeDataString(ownerUserId)}",
            ct);

    public Task<IReadOnlyList<GccV2CustomerOutcomeDto>> ListCustomerOutcomesAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2CustomerOutcomeDto>(
            $"repo/content-creator-v2/roi/outcomes?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2CustomerOutcomeDto?> GetCustomerOutcomeAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2CustomerOutcomeDto>(
            $"repo/content-creator-v2/roi/outcomes/{id:D}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2CustomerOutcomeDto> CreateCustomerOutcomeAsync(
        CreateGccV2CustomerOutcomeCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2CustomerOutcomeDto>("repo/content-creator-v2/roi/outcomes", command, ct);
    public Task DeleteCustomerOutcomeAsync(Guid id, string ownerUserId, CancellationToken ct = default) =>
        DeleteAsync(
            $"repo/content-creator-v2/roi/outcomes/{id:D}?ownerUserId={Uri.EscapeDataString(ownerUserId)}",
            ct);

    public Task<IReadOnlyList<GccV2TaskRunDto>> ListTaskRunsAsync(
        string ownerUserId, string? status = null, CancellationToken ct = default) =>
        GetListAsync<GccV2TaskRunDto>(
            $"repo/content-creator-v2/task-runs?ownerUserId={Uri.EscapeDataString(ownerUserId)}"
            + (string.IsNullOrWhiteSpace(status) ? "" : $"&status={Uri.EscapeDataString(status)}"), ct);
    public Task<GccV2TaskRunDto?> GetTaskRunAsync(
        Guid runId, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2TaskRunDto>(
            $"repo/content-creator-v2/task-runs/{runId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2TaskRunDto> CreateTaskRunAsync(
        CreateGccV2TaskRunCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskRunDto>("repo/content-creator-v2/task-runs", command, ct);
    public async Task<GccV2TaskRunDto?> ClaimTaskRunAsync(
        Guid runId, string instanceId, int leaseSeconds = 120, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"repo/content-creator-v2/task-runs/{runId}/claim"
            + $"?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}", null, ct);
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<GccV2TaskRunDto>(
            await response.Content.ReadAsStringAsync(ct), JsonOpts);
    }
    public async Task<GccV2TaskRunDto?> ClaimNextTaskRunAsync(
        string instanceId, int leaseSeconds = 120, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            "repo/content-creator-v2/task-runs/claim-next"
            + $"?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}", null, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<GccV2TaskRunDto>(
            await response.Content.ReadAsStringAsync(ct), JsonOpts);
    }
    public Task<GccV2TaskRunDto> TransitionTaskRunAsync(
        Guid runId, TransitionGccV2TaskRunCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskRunDto>($"repo/content-creator-v2/task-runs/{runId}/transition", command, ct);
    public Task<IReadOnlyList<GccV2TaskRunEventDto>> GetTaskRunEventsAsync(
        Guid runId, int afterSeq = 0, CancellationToken ct = default) =>
        GetListAsync<GccV2TaskRunEventDto>(
            $"repo/content-creator-v2/task-runs/{runId}/events?afterSeq={afterSeq}", ct);
    public Task<GccV2TaskRunEventDto> AddTaskRunEventAsync(
        Guid runId, AddGccV2TaskRunEventCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskRunEventDto>($"repo/content-creator-v2/task-runs/{runId}/events", command, ct);
    public Task<GccV2TaskArtifactDto> CreateTaskArtifactAsync(
        Guid runId, CreateGccV2TaskArtifactCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskArtifactDto>($"repo/content-creator-v2/task-runs/{runId}/artifacts", command, ct);
    public Task<GccV2TaskArtifactVersionDto> CreateTaskArtifactVersionAsync(
        Guid artifactId, CreateGccV2TaskArtifactVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2TaskArtifactVersionDto>(
            $"repo/content-creator-v2/task-runs/artifacts/{artifactId}/versions", command, ct);

    // Durable canvas projects.

    public Task<IReadOnlyList<GccV2CanvasProjectListItemDto>> ListCanvasProjectsAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2CanvasProjectListItemDto>(
            $"repo/content-creator-v2/canvas-projects?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2CanvasProjectDto?> GetCanvasProjectAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2CanvasProjectDto>(
            $"repo/content-creator-v2/canvas-projects/{id}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2CanvasProjectDto> CreateCanvasProjectAsync(
        CreateGccV2CanvasProjectCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2CanvasProjectDto>("repo/content-creator-v2/canvas-projects", command, ct);

    public Task<GccV2CanvasProjectDto> PatchCanvasProjectAsync(
        Guid id, PatchGccV2CanvasProjectCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2CanvasProjectDto>($"repo/content-creator-v2/canvas-projects/{id}", command, ct);

    public Task<GccV2CanvasAssetDto> CreateCanvasAssetAsync(
        Guid projectId, CreateGccV2CanvasAssetCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2CanvasAssetDto>(
            $"repo/content-creator-v2/canvas-projects/{projectId}/assets", command, ct);

    public Task<GccV2CanvasAssetVersionDto> AppendCanvasAssetVersionAsync(
        Guid projectId, Guid assetId, AppendGccV2CanvasAssetVersionCommand command,
        CancellationToken ct = default) =>
        PostAsync<GccV2CanvasAssetVersionDto>(
            $"repo/content-creator-v2/canvas-projects/{projectId}/assets/{assetId}/versions", command, ct);

    // Durable batch grids.

    public Task<IReadOnlyList<GccV2GridListItemDto>> ListGridsAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2GridListItemDto>(
            $"repo/content-creator-v2/grids?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2GridDto?> GetGridAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2GridDto>(
            $"repo/content-creator-v2/grids/{id}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2GridDto> CreateGridAsync(
        CreateGccV2GridCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2GridDto>("repo/content-creator-v2/grids", command, ct);

    public Task<GccV2GridDto> PatchGridAsync(
        Guid id, PatchGccV2GridCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2GridDto>($"repo/content-creator-v2/grids/{id}", command, ct);

    public Task<GccV2GridRowDto> CreateGridRowAsync(
        Guid gridId, CreateGccV2GridRowCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2GridRowDto>($"repo/content-creator-v2/grids/{gridId}/rows", command, ct);

    public Task<IReadOnlyList<GccV2GridRowDto>> CreateGridRowsBulkAsync(
        Guid gridId, CreateGccV2GridRowsBulkCommand command, CancellationToken ct = default) =>
        PostListAsync<GccV2GridRowDto>($"repo/content-creator-v2/grids/{gridId}/rows/bulk", command, ct);

    public Task<GccV2GridDto> CreateGridRunAsync(
        Guid gridId, CreateGccV2GridRunCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2GridDto>($"repo/content-creator-v2/grids/{gridId}/runs", command, ct);

    // Geek Content Pipelines.

    public Task<IReadOnlyList<GccV2PipelineListItemDto>> ListPipelinesAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2PipelineListItemDto>(
            $"repo/content-creator-v2/pipelines?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2PipelineDto?> GetPipelineAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2PipelineDto>(
            $"repo/content-creator-v2/pipelines/{id}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2PipelineDto> CreatePipelineAsync(
        CreateGccV2PipelineCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PipelineDto>("repo/content-creator-v2/pipelines", command, ct);

    public Task<GccV2PipelineDto> PublishPipelineAsync(
        Guid id, GccV2PipelineActorCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PipelineDto>($"repo/content-creator-v2/pipelines/{id}/publish", command, ct);

    public Task<GccV2PipelineDto> StartPipelineRunAsync(
        Guid id, StartGccV2PipelineRunCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PipelineDto>($"repo/content-creator-v2/pipelines/{id}/runs", command, ct);

    public Task<GccV2PipelineDto> TransitionPipelineRunAsync(
        Guid runId, string action, GccV2PipelineActorCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2PipelineDto>(
            $"repo/content-creator-v2/pipelines/runs/{runId}/{Uri.EscapeDataString(action)}", command, ct);

    // Governed context.

    public Task<IReadOnlyList<GccV2KnowledgeAssetDto>> ListKnowledgeAsync(
        string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<GccV2KnowledgeAssetDto>(
            $"repo/content-creator-v2/context/knowledge?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2ContextQuotaUsageDto?> GetContextQuotaUsageAsync(
        string ownerUserId, Guid? createId = null, CancellationToken ct = default) =>
        GetAsync<GccV2ContextQuotaUsageDto>(
            $"repo/content-creator-v2/context/quotas?ownerUserId={Uri.EscapeDataString(ownerUserId)}"
            + (createId is null ? "" : $"&createId={createId:D}"), ct);
    public Task<GccV2KnowledgeAssetDto?> GetKnowledgeAsync(
        Guid assetId, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2KnowledgeAssetDto>(
            $"repo/content-creator-v2/context/knowledge/{assetId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2KnowledgeAssetDto> CreateKnowledgeAsync(
        CreateGccV2KnowledgeCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2KnowledgeAssetDto>("repo/content-creator-v2/context/knowledge", command, ct);
    public Task<GccV2KnowledgeAssetDto> PatchKnowledgeAsync(
        Guid assetId, PatchGccV2ContextCatalogCommand command, CancellationToken ct = default) =>
        PatchAsync<GccV2KnowledgeAssetDto>($"repo/content-creator-v2/context/knowledge/{assetId}", command, ct);
    public Task<GccV2KnowledgeAssetVersionDto> CreateKnowledgeVersionAsync(
        Guid assetId, CreateGccV2KnowledgeVersionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2KnowledgeAssetVersionDto>(
            $"repo/content-creator-v2/context/knowledge/{assetId}/versions", command, ct);
    public Task<GccV2KnowledgeResourceDto> AddKnowledgeResourceAsync(
        Guid versionId, AddGccV2KnowledgeResourceCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2KnowledgeResourceDto>(
            $"repo/content-creator-v2/context/knowledge/versions/{versionId}/resources", command, ct);
    public Task<GccV2KnowledgeAssetVersionDto> TransitionKnowledgeAsync(
        Guid versionId, string transition, TransitionGccV2ContextCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2KnowledgeAssetVersionDto>(
            $"repo/content-creator-v2/context/knowledge/versions/{versionId}/{transition}", command, ct);

    public Task<JsonElement> CreateContextCatalogAsync(
        CreateGccV2ContextCatalogCommand command, CancellationToken ct = default) =>
        PostAsync<JsonElement>("repo/content-creator-v2/context/catalogs", command, ct);
    public Task<IReadOnlyList<JsonElement>> ListContextCatalogsAsync(
        string kind, string ownerUserId, CancellationToken ct = default) =>
        GetListAsync<JsonElement>(
            $"repo/content-creator-v2/context/catalogs/{Uri.EscapeDataString(kind)}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<JsonElement?> GetContextCatalogAsync(
        string kind, Guid catalogId, string ownerUserId, CancellationToken ct = default) =>
        GetJsonElementAsync(
            $"repo/content-creator-v2/context/catalogs/{Uri.EscapeDataString(kind)}/{catalogId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<JsonElement> PatchContextCatalogAsync(
        string kind, Guid catalogId, PatchGccV2ContextCatalogCommand command, CancellationToken ct = default) =>
        PatchAsync<JsonElement>(
            $"repo/content-creator-v2/context/catalogs/{Uri.EscapeDataString(kind)}/{catalogId}", command, ct);
    public Task<JsonElement> CreateContextCatalogVersionAsync(
        string kind, Guid catalogId, CreateGccV2ContextCatalogVersionCommand command, CancellationToken ct = default) =>
        PostAsync<JsonElement>(
            $"repo/content-creator-v2/context/catalogs/{Uri.EscapeDataString(kind)}/{catalogId}/versions", command, ct);
    public Task<JsonElement> TransitionContextCatalogVersionAsync(
        string kind, Guid versionId, string transition, TransitionGccV2ContextCommand command,
        CancellationToken ct = default) =>
        PostAsync<JsonElement>(
            $"repo/content-creator-v2/context/catalogs/{Uri.EscapeDataString(kind)}/versions/{versionId}/{Uri.EscapeDataString(transition)}",
            command, ct);
    public Task<GccV2GovernedVersionLookupDto?> GetContextVersionAsync(
        string kind, Guid versionId, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2GovernedVersionLookupDto>(
            $"repo/content-creator-v2/context/versions/{Uri.EscapeDataString(kind)}/{versionId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2GovernedVersionLookupDto?> GetCurrentContextVersionAsync(
        string kind, Guid stableId, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2GovernedVersionLookupDto>(
            $"repo/content-creator-v2/context/current-versions/{Uri.EscapeDataString(kind)}/{stableId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);

    public Task<GccV2RunAttachmentDto> CreateRunAttachmentAsync(
        CreateGccV2RunAttachmentCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2RunAttachmentDto>("repo/content-creator-v2/context/attachments", command, ct);
    public Task<GccV2RunAttachmentDto?> GetRunAttachmentAsync(
        Guid id, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2RunAttachmentDto>(
            $"repo/content-creator-v2/context/attachments/{id}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<IReadOnlyList<GccV2RunAttachmentDto>> ListRunAttachmentsDueForRetentionAsync(
        int limit = 200, CancellationToken ct = default) =>
        GetListAsync<GccV2RunAttachmentDto>(
            $"repo/content-creator-v2/context/attachments/retention-due?limit={limit}", ct);
    public Task<GccV2RunAttachmentDto> FinalizeRunAttachmentAsync(
        Guid id, FinalizeGccV2RunAttachmentCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2RunAttachmentDto>(
            $"repo/content-creator-v2/context/attachments/{id}/finalize", command, ct);
    public Task DeleteRunAttachmentAsync(Guid id, string ownerUserId, CancellationToken ct = default) =>
        DeleteAsync($"repo/content-creator-v2/context/attachments/{id}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2ContextSelectionDto> CreateContextSelectionAsync(
        CreateGccV2ContextSelectionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2ContextSelectionDto>("repo/content-creator-v2/context/selections", command, ct);
    public Task<GccV2RunContextManifestDto> CreateContextManifestAsync(
        CreateGccV2RunContextManifestCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2RunContextManifestDto>("repo/content-creator-v2/context/manifests", command, ct);
    public Task<GccV2RunContextManifestDto?> GetContextManifestByJobAsync(
        Guid jobId, string ownerUserId, CancellationToken ct = default) =>
        GetAsync<GccV2RunContextManifestDto>(
            $"repo/content-creator-v2/context/manifests/by-job/{jobId}?ownerUserId={Uri.EscapeDataString(ownerUserId)}", ct);
    public Task<GccV2ContextIngestionJobDto> QueueKnowledgeIngestionAsync(
        Guid versionId, QueueGccV2KnowledgeIngestionCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2ContextIngestionJobDto>(
            $"repo/content-creator-v2/context/knowledge/versions/{versionId}/queue-ingestion", command, ct);
    public Task<GccV2ContextIngestionJobDto?> GetContextIngestionJobAsync(
        Guid id, CancellationToken ct = default) =>
        GetAsync<GccV2ContextIngestionJobDto>($"repo/content-creator-v2/context/ingestion-jobs/{id}", ct);
    public Task<IReadOnlyList<GccV2ContextIngestionJobDto>> ListContextIngestionJobsAsync(
        string status, DateTimeOffset? leaseBefore = null, int limit = 200, CancellationToken ct = default)
    {
        var path = $"repo/content-creator-v2/context/ingestion-jobs/by-status/{Uri.EscapeDataString(status)}?limit={limit}";
        if (leaseBefore is not null) path += $"&leaseBefore={Uri.EscapeDataString(leaseBefore.Value.ToString("O"))}";
        return GetListAsync<GccV2ContextIngestionJobDto>(path, ct);
    }
    public async Task<GccV2ContextIngestionJobDto?> ClaimContextIngestionJobAsync(
        Guid id, string instanceId, int leaseSeconds = 120, CancellationToken ct = default)
    {
        var path = $"repo/content-creator-v2/context/ingestion-jobs/{id}/claim?instanceId={Uri.EscapeDataString(instanceId)}&leaseSeconds={leaseSeconds}";
        var res = await _http.PostAsync(path, null, ct);
        if (res.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<GccV2ContextIngestionJobDto>(
            await res.Content.ReadAsStringAsync(ct), JsonOpts);
    }
    public Task<GccV2ContextIngestionJobDto> TransitionContextIngestionJobAsync(
        Guid id, TransitionGccV2ContextIngestionJobCommand command, CancellationToken ct = default) =>
        PostAsync<GccV2ContextIngestionJobDto>(
            $"repo/content-creator-v2/context/ingestion-jobs/{id}/transition", command, ct);

    private async Task<int> PostSeedCountAsync(string path, CancellationToken ct)
    {
        var res = await _http.PostAsync(path, content: null, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("seeded", out var seeded) ? seeded.GetInt32() : 0;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            var res = await _http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode) return null;
            var json = await res.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET {Path} failed", path);
            throw;
        }
    }

    private async Task<JsonElement?> GetJsonElementAsync(string path, CancellationToken ct)
    {
        var response = await _http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) return null;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            var res = await _http.GetAsync(path, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"GET {path} failed with {(int)res.StatusCode}: {TruncateBody(body)}");
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET list {Path} failed", path);
            throw;
        }
    }

    private async Task<IReadOnlyList<T>> PostListAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var res = await _http.PostAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
    }

    private static string TruncateBody(string body) =>
        string.IsNullOrWhiteSpace(body) ? "(empty)" : (body.Length <= 240 ? body : body[..240]);

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var res = await _http.PostAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }

    private async Task<T> PutAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var res = await _http.PutAsync(path, content, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }

    private async Task<T> PatchAsync<T>(string path, object body, CancellationToken ct)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, JsonOpts), Encoding.UTF8, "application/json");
        var req = new HttpRequestMessage(HttpMethod.Patch, path) { Content = content };
        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<T>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Empty response from {path}");
    }

    private async Task DeleteAsync(string path, CancellationToken ct)
    {
        var res = await _http.DeleteAsync(path, ct);
        res.EnsureSuccessStatusCode();
    }
}
