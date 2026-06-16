using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Mapping;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

public sealed record UpdateNicheStatusRequest(
    string Status,
    string? Step,
    int StepNumber,
    int TotalSteps,
    string? ErrorMessage,
    NicheAnalysisStepLogEntry? StepLogEntry = null);

public sealed record UpdateNicheScoresRequest(
    decimal AuthorityScore,
    int Covered,
    int Partial,
    int Gap);

public sealed record UpdateNicheStepStatusRequest(
    string Slug,
    string Status,
    NicheAnalysisStepLogEntry? StepLogEntry = null);

public sealed record InvalidateNicheStepsRequest(
    IReadOnlyList<string> DownstreamSlugs);

public sealed record UpdateCrawledUrlsRequest(
    string CrawledUrlsJson);

public sealed record SaveNicheAnalysisResultsRequest(
    string PrimaryNiche,
    string NicheDescription,
    string[] NicheTags,
    string AudienceType,
    string DiscoveryMethod,
    decimal AuthorityScore,
    int TotalPillarsIdentified,
    int Covered,
    int Partial,
    int Gap,
    DateTimeOffset AnalyzedAt,
    DateTimeOffset NextAnalysisDue,
    string? FusionSnapshot = null);

public sealed record SaveNicheFusionSnapshotRequest(string FusionSnapshot);
public sealed record ReplaceSchemaSignalsRequest(IReadOnlyList<NicheProfileSchemaSignalWrite> Signals);
public sealed record ReplaceDiscoveredUrlsRequest(IReadOnlyList<NicheProfileDiscoveredUrlWrite> Urls);
public sealed record ReplaceNavigationLinksRequest(IReadOnlyList<NicheProfileNavigationLinkWrite> Links);
public sealed record ReplaceHeadingsRequest(IReadOnlyList<NicheProfileHeadingWrite> Headings);
public sealed record ReplaceTopicCandidateEvidenceRequest(IReadOnlyList<NicheTopicCandidateEvidenceWrite> Evidence);
public sealed record ReplacePageContentRequest(NicheProfilePageContentWrite Content);
public sealed record ReplaceSiteStructureRequest(NicheProfileSiteStructureWrite Structure);

[ApiController]
[Route("repo/seo/niche-profiles")]
public sealed class NicheProfilesController(
    INicheProfileRepository profiles,
    INicheAnalyticsDapperRepository analytics,
    IProjectRepository projects) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromQuery] Guid userId,
        [FromBody] NicheProfile body,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(body.ProjectId, userId, ct);
        if (owned is not null) return owned;

        var result = await profiles.CreateAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}")]
    public async Task<IActionResult> GetById(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetByIdAsync(profileId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound() : BadRequest(result.Error);
        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet("{profileId:guid}/competitors")]
    public async Task<IActionResult> GetCompetitors(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetCompetitorsAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("project/{projectId:guid}/latest")]
    public async Task<IActionResult> GetLatest(
        Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null) return owned;

        var result = await profiles.GetLatestByProjectAsync(projectId, ct);
        return result.IsSuccess
            ? (result.Value is null ? NoContent() : Ok(result.Value))
            : BadRequest(result.Error);
    }

    [HttpGet("project/{projectId:guid}/progress")]
    public async Task<IActionResult> GetProgress(
        Guid projectId,
        [FromQuery] Guid userId,
        [FromQuery] int months = 12,
        CancellationToken ct = default)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null) return owned;

        var result = await analytics.GetAuthorityProgressAsync(projectId, Math.Clamp(months, 1, 36), ct);
        return result.IsSuccess ? Ok(result.Value) : Ok(Array.Empty<AuthorityProgressPoint>());
    }

    [HttpGet("project/{projectId:guid}/history")]
    public async Task<IActionResult> GetHistory(
        Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null) return owned;

        var result = await profiles.GetHistoryAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] UpdateNicheStatusRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateStatusAsync(
            profileId, body.Status, body.Step, body.StepNumber, body.TotalSteps, body.ErrorMessage, body.StepLogEntry, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/scores")]
    public async Task<IActionResult> UpdateScores(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] UpdateNicheScoresRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateScoresAsync(
            profileId, body.AuthorityScore, body.Covered, body.Partial, body.Gap, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/status-snapshot")]
    public async Task<IActionResult> GetStatusSnapshot(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetStatusRowAsync(profileId, ct);
        return result.IsSuccess
            ? (result.Value is null ? NotFound() : Ok(result.Value))
            : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/analysis-details-snapshot")]
    public async Task<IActionResult> GetAnalysisDetailsSnapshot(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromQuery] bool includeFusion = false,
        CancellationToken ct = default)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetAnalysisDetailsRowAsync(profileId, includeFusion, ct);
        return result.IsSuccess
            ? (result.Value is null ? NotFound() : Ok(result.Value))
            : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/step-runs")]
    public async Task<IActionResult> GetStepRuns(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetStepRunsAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/step-runs/{stepSlug}")]
    public async Task<IActionResult> UpsertStepRun(
        Guid profileId,
        string stepSlug,
        [FromQuery] Guid userId,
        [FromBody] NicheProfileStepRunUpsert body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;
        if (!string.Equals(stepSlug, body.StepSlug, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Route step slug must match request body.");

        var result = await profiles.UpsertStepRunAsync(profileId, body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/step-runs/{stepSlug}/status")]
    public async Task<IActionResult> UpdateStepRunStatus(
        Guid profileId,
        string stepSlug,
        [FromQuery] Guid userId,
        [FromBody] NicheProfileStepRunStatusPatch body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateStepRunStatusAsync(profileId, stepSlug, body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    /// <summary>Legacy route used by GeekSeoBackend HttpNicheProfileRepository.</summary>
    [HttpPatch("{profileId:guid}/step-status")]
    public async Task<IActionResult> UpdateStepStatus(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] UpdateNicheStepStatusRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateStepStatusAsync(
            profileId, body.Slug, body.Status, body.StepLogEntry, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    /// <summary>Legacy route used by GeekSeoBackend HttpNicheProfileRepository.</summary>
    [HttpGet("{profileId:guid}/step-statuses")]
    public async Task<IActionResult> GetStepStatuses(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetStepStatusesAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    /// <summary>Legacy route used by GeekSeoBackend HttpNicheProfileRepository.</summary>
    [HttpPatch("{profileId:guid}/invalidate-steps")]
    public async Task<IActionResult> InvalidateDownstreamSteps(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] InvalidateNicheStepsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.InvalidateDownstreamStepsAsync(profileId, body.DownstreamSlugs, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    /// <summary>Legacy route used by GeekSeoBackend HttpNicheProfileRepository.</summary>
    [HttpPatch("{profileId:guid}/crawled-urls")]
    public async Task<IActionResult> UpdateCrawledUrls(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] UpdateCrawledUrlsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateCrawledUrlsAsync(profileId, body.CrawledUrlsJson, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/schema-signals")]
    public async Task<IActionResult> ReplaceSchemaSignals(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceSchemaSignalsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceSchemaSignalsAsync(profileId, body.Signals, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/schema-signals")]
    public async Task<IActionResult> GetSchemaSignals(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetSchemaSignalsAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/discovered-urls")]
    public async Task<IActionResult> ReplaceDiscoveredUrls(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceDiscoveredUrlsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceDiscoveredUrlsAsync(profileId, body.Urls, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/discovered-urls")]
    public async Task<IActionResult> GetDiscoveredUrls(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetDiscoveredUrlsAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/navigation-links")]
    public async Task<IActionResult> ReplaceNavigationLinks(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceNavigationLinksRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceNavigationLinksAsync(profileId, body.Links, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/navigation-links")]
    public async Task<IActionResult> GetNavigationLinks(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetNavigationLinksAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/headings")]
    public async Task<IActionResult> ReplaceHeadings(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceHeadingsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceHeadingsAsync(profileId, body.Headings, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/headings")]
    public async Task<IActionResult> GetHeadings(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetHeadingsAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/topic-candidate-evidence")]
    public async Task<IActionResult> ReplaceTopicCandidateEvidence(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceTopicCandidateEvidenceRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceTopicCandidateEvidenceAsync(profileId, body.Evidence, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/topic-candidate-evidence")]
    public async Task<IActionResult> GetTopicCandidateEvidence(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetTopicCandidateEvidenceAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/page-content")]
    public async Task<IActionResult> ReplacePageContent(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplacePageContentRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplacePageContentAsync(profileId, body.Content, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/page-content")]
    public async Task<IActionResult> GetPageContent(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetPageContentAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{profileId:guid}/site-structure")]
    public async Task<IActionResult> ReplaceSiteStructure(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] ReplaceSiteStructureRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.ReplaceSiteStructureAsync(profileId, body.Structure, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/site-structure")]
    public async Task<IActionResult> GetSiteStructure(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetSiteStructureAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/profile-summary")]
    public async Task<IActionResult> UpdateProfileSummary(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] NicheProfileSummaryPatch body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdateProfileSummaryAsync(profileId, body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/fusion-snapshot")]
    public async Task<IActionResult> SaveFusionSnapshot(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] SaveNicheFusionSnapshotRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.SaveFusionSnapshotAsync(profileId, body.FusionSnapshot, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/phase-status")]
    public async Task<IActionResult> UpdatePhaseStatus(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] NichePhaseStatusPatch body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.UpdatePhaseStatusAsync(profileId, body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("{profileId:guid}/topic-candidates/bulk")]
    public async Task<IActionResult> BulkUpsertTopicCandidates(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] List<NicheTopicCandidateBulkUpsert> body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var key)
            ? key.ToString()
            : Guid.NewGuid().ToString("N");

        var result = await profiles.BulkUpsertTopicCandidatesAsync(
            profileId, body, idempotencyKey, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/topic-candidates")]
    public async Task<IActionResult> GetTopicCandidates(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] bool? selectedOnly = null,
        CancellationToken ct = default)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.GetTopicCandidatesAsync(profileId, page, pageSize, selectedOnly, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{profileId:guid}/analysis-results")]
    public async Task<IActionResult> SaveAnalysisResults(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromBody] SaveNicheAnalysisResultsRequest body,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await profiles.SaveAnalysisResultsAsync(profileId, new NicheAnalysisSaveRequest(
            body.PrimaryNiche,
            body.NicheDescription,
            body.NicheTags,
            body.AudienceType,
            body.DiscoveryMethod,
            body.AuthorityScore,
            body.TotalPillarsIdentified,
            body.Covered,
            body.Partial,
            body.Gap,
            body.AnalyzedAt,
            body.NextAnalysisDue,
            body.FusionSnapshot), ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("pillars")]
    public async Task<IActionResult> BulkInsertPillars(
        [FromQuery] Guid userId,
        [FromBody] List<NichePillarBulkInsert> body,
        CancellationToken ct)
    {
        var entities = body.Select(NicheBulkInsertMapper.ToEntity).ToList();
        var result = await profiles.BulkInsertPillarsAsync(entities, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("subtopics")]
    public async Task<IActionResult> BulkInsertSubtopics(
        [FromQuery] Guid userId,
        [FromBody] List<NicheSubtopicBulkInsert> body,
        CancellationToken ct)
    {
        var entities = body.Select(NicheBulkInsertMapper.ToEntity).ToList();
        var result = await profiles.BulkInsertSubtopicsAsync(entities, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("competitors")]
    public async Task<IActionResult> BulkInsertCompetitors(
        [FromQuery] Guid userId,
        [FromBody] List<NicheCompetitorBulkInsert> body,
        CancellationToken ct)
    {
        var entities = body.Select(NicheBulkInsertMapper.ToEntity).ToList();
        var result = await profiles.BulkInsertCompetitorsAsync(entities, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("entities")]
    public async Task<IActionResult> BulkInsertEntities(
        [FromQuery] Guid userId,
        [FromBody] List<NicheEntityBulkInsert> body,
        CancellationToken ct)
    {
        var entities = body.Select(NicheBulkInsertMapper.ToEntity).ToList();
        var result = await profiles.BulkInsertEntitiesAsync(entities, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("pillar-pages")]
    public async Task<IActionResult> BulkInsertPillarPages(
        [FromQuery] Guid userId,
        [FromBody] List<NichePillarPageBulkInsert> body,
        CancellationToken ct)
    {
        var entities = body.Select(NicheBulkInsertMapper.ToEntity).ToList();
        var result = await profiles.BulkInsertPillarPagesAsync(entities, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("maintenance/due")]
    public async Task<IActionResult> ListDue(
        [FromQuery] int limit = 5,
        CancellationToken ct = default)
    {
        var result = await profiles.ListDueForReanalysisAsync(Math.Clamp(limit, 1, 50), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("maintenance/fail-stale-processing")]
    public async Task<IActionResult> FailStaleProcessing(
        [FromQuery] int maxAgeMinutes = 5,
        CancellationToken ct = default)
    {
        var result = await profiles.FailStaleProcessingAsync(
            TimeSpan.FromMinutes(Math.Clamp(maxAgeMinutes, 1, 60)), ct);
        return result.IsSuccess ? Ok(new { failedCount = result.Value }) : BadRequest(result.Error);
    }

    [HttpGet("maintenance/queued")]
    public async Task<IActionResult> ListQueued(
        [FromQuery] int limit = 3,
        CancellationToken ct = default)
    {
        var result = await profiles.ListQueuedAsync(Math.Clamp(limit, 1, 20), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    private async Task<IActionResult?> EnsureProjectAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(projectId, userId, ct);
        if (!project.IsSuccess)
            return project.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound() : BadRequest(project.Error);
        return null;
    }

    [HttpGet("{profileId:guid}/project-id")]
    public async Task<IActionResult> GetProjectId(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var result = await profiles.GetProjectIdAsync(profileId, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        if (result.Value is null)
            return NotFound();
        return Ok(new { projectId = result.Value });
    }

    private async Task<IActionResult?> EnsureProfileOwnedAsync(Guid profileId, Guid userId, CancellationToken ct)
    {
        var idResult = await profiles.GetProjectIdAsync(profileId, ct);
        if (!idResult.IsSuccess)
            return BadRequest(idResult.Error);
        if (idResult.Value is null)
            return NotFound();
        return await EnsureProjectAsync(idResult.Value.Value, userId, ct);
    }
}
