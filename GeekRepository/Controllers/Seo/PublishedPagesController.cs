using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/published-pages")]
public sealed class PublishedPagesController(IPublishedPageRepository pages, IProjectRepository projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await pages.ListByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{publishedPageId:guid}/sparkline")]
    public async Task<IActionResult> Sparkline(
        Guid publishedPageId,
        [FromQuery] Guid userId,
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        var pageResult = await pages.GetByIdAsync(publishedPageId, ct);
        if (!pageResult.IsSuccess || pageResult.Value is null)
            return NotFound();

        var owned = await EnsureProjectAsync(pageResult.Value.ProjectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await pages.GetSparklineAsync(publishedPageId, Math.Clamp(days, 7, 90), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("snapshots")]
    public async Task<IActionResult> UpsertSnapshot([FromBody] SeoContentPerformanceSnapshot body, CancellationToken ct)
    {
        var result = await pages.UpsertSnapshotAsync(body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("maintenance/due")]
    public async Task<IActionResult> ListDue([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await pages.ListDueForSnapshotAsync(Math.Clamp(limit, 1, 200), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    private async Task<IActionResult?> EnsureProjectAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(projectId, userId, ct);
        if (!project.IsSuccess)
            return project.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(project.Error);
        return null;
    }
}
