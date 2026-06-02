using GeekSeo.Application.Interfaces;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/rank-tracking")]
public sealed class RankTrackingController(
    IRankTrackingRepository rankTracking,
    IProjectRepository projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> ListKeywords(
        [FromQuery] Guid projectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await rankTracking.GetKeywordsAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost]
    public async Task<IActionResult> AddKeyword(
        [FromQuery] Guid userId,
        [FromBody] SeoTrackedKeyword body,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(body.ProjectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await rankTracking.AddKeywordAsync(body, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);

        return Ok(result.Value);
    }

    [HttpDelete("{keywordId:guid}")]
    public async Task<IActionResult> DeleteKeyword(
        Guid keywordId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        _ = userId;
        var result = await rankTracking.DeleteKeywordAsync(keywordId, ct);
        if (!result.IsSuccess)
        {
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound()
                : BadRequest(result.Error);
        }

        return NoContent();
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] Guid projectId,
        [FromQuery] string keyword,
        [FromQuery] int days = 30,
        [FromQuery] Guid userId = default,
        CancellationToken ct = default)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await rankTracking.GetHistoryAsync(
            projectId,
            keyword,
            Math.Clamp(days, 1, 366),
            ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> UpsertSnapshot([FromBody] SeoRankTracking body, CancellationToken ct)
    {
        var result = await rankTracking.UpsertSnapshotAsync(body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("maintenance/projects")]
    public async Task<IActionResult> ListProjectsWithKeywords(
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await rankTracking.ListProjectsWithKeywordsAsync(Math.Clamp(limit, 1, 500), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    private async Task<IActionResult?> EnsureProjectAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(projectId, userId, ct);
        if (!project.IsSuccess)
        {
            return project.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound()
                : BadRequest(project.Error);
        }

        return null;
    }
}
