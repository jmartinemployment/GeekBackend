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
    DateTimeOffset NextAnalysisDue);

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
            body.NextAnalysisDue), ct);
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

    private async Task<IActionResult?> EnsureProfileOwnedAsync(Guid profileId, Guid userId, CancellationToken ct)
    {
        var profileResult = await profiles.GetByIdAsync(profileId, ct);
        if (!profileResult.IsSuccess || profileResult.Value is null)
            return profileResult.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound() : BadRequest(profileResult.Error);
        return await EnsureProjectAsync(profileResult.Value.ProjectId, userId, ct);
    }
}
