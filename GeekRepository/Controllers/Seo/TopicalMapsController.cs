using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/topical-maps")]
public sealed class TopicalMapsController(ITopicalMapRepository maps, IProjectRepository projects) : ControllerBase
{
    [HttpGet("{projectId:guid}")]
    public async Task<IActionResult> Get(Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await maps.GetByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{projectId:guid}")]
    public async Task<IActionResult> Upsert(
        Guid projectId,
        [FromQuery] Guid userId,
        [FromBody] SeoTopicalMap body,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        body.ProjectId = projectId;
        var result = await maps.UpsertAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("maintenance/due")]
    public async Task<IActionResult> ListDue([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var result = await maps.ListDueForRefreshAsync(Math.Clamp(limit, 1, 100), ct);
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
