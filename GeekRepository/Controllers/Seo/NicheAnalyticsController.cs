using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/niche-analytics")]
public sealed class NicheAnalyticsController(
    INicheAnalyticsDapperRepository analytics,
    INicheProfileRepository profiles,
    IProjectRepository projects) : ControllerBase
{
    [HttpGet("{profileId:guid}/summary")]
    public async Task<IActionResult> GetSummary(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await analytics.GetProfileSummaryAsync(profileId, ct);
        return result.IsSuccess
            ? (result.Value is null ? NotFound() : Ok(result.Value))
            : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/coverage-matrix")]
    public async Task<IActionResult> GetCoverageMatrix(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await analytics.GetCoverageMatrixAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/gaps")]
    public async Task<IActionResult> GetGaps(
        Guid profileId,
        [FromQuery] Guid userId,
        [FromQuery] bool quickWinsOnly = false,
        CancellationToken ct = default)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await analytics.GetTopicalGapsAsync(profileId, quickWinsOnly, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
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
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/competitors")]
    public async Task<IActionResult> GetCompetitors(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await analytics.GetCompetitorOverlapAsync(profileId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{profileId:guid}/entities")]
    public async Task<IActionResult> GetEntities(
        Guid profileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var denied = await EnsureProfileOwnedAsync(profileId, userId, ct);
        if (denied is not null) return denied;

        var result = await analytics.GetEntityCoverageAsync(profileId, ct);
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
