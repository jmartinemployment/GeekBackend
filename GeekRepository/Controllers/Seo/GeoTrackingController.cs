using System.Text.Json;
using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/geo")]
public sealed class GeoTrackingController(IGeoTrackingRepository geo, IProjectRepository projects) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [HttpGet("queries")]
    public async Task<IActionResult> ListQueries([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await geo.ListByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("queries")]
    public async Task<IActionResult> CreateQuery(
        [FromQuery] Guid userId,
        [FromBody] CreateGeoTrackingQueryRequest request,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(request.ProjectId, userId, ct);
        if (owned is not null)
            return owned;

        var entity = new SeoGeoTrackingQuery
        {
            ProjectId = request.ProjectId,
            QueryText = request.QueryText.Trim(),
            PlatformsJson = JsonSerializer.Serialize(request.Platforms, JsonOptions),
            Enabled = true,
        };

        var result = await geo.CreateAsync(entity, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpDelete("queries/{queryId:guid}")]
    public async Task<IActionResult> DeleteQuery(Guid queryId, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        var result = await geo.DeleteAsync(queryId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return NoContent();
    }

    [HttpGet("queries/{queryId:guid}")]
    public async Task<IActionResult> GetQuery(Guid queryId, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        var result = await geo.GetQueryAsync(queryId, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet("queries/{queryId:guid}/snapshots")]
    public async Task<IActionResult> ListSnapshots(
        Guid queryId,
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        var result = await geo.ListSnapshotsAsync(queryId, Math.Clamp(days, 1, 90), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("snapshots")]
    public async Task<IActionResult> AddSnapshot([FromBody] SeoGeoMentionSnapshot body, CancellationToken ct)
    {
        var result = await geo.AddSnapshotAsync(body, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpGet("maintenance/enabled-queries")]
    public async Task<IActionResult> ListEnabled([FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await geo.ListEnabledQueriesAsync(Math.Clamp(limit, 1, 500), ct);
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
