using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/content-guard")]
public sealed class ContentGuardController(IContentGuardRepository guard, IProjectRepository projects) : ControllerBase
{
    [HttpGet("{projectId:guid}/policy")]
    public async Task<IActionResult> GetPolicy(Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await guard.GetPolicyAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{projectId:guid}/policy")]
    public async Task<IActionResult> UpsertPolicy(
        Guid projectId,
        [FromQuery] Guid userId,
        [FromBody] UpsertContentGuardPolicyRequest request,
        CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var policy = new SeoContentGuardPolicy
        {
            ProjectId = projectId,
            UserId = userId,
            Enabled = request.Enabled,
            AutoPatch = request.AutoPatch,
        };

        var result = await guard.UpsertPolicyAsync(policy, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{projectId:guid}/runs")]
    public async Task<IActionResult> ListRuns(
        Guid projectId,
        [FromQuery] Guid userId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await guard.ListRunsAsync(projectId, Math.Clamp(limit, 1, 100), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("runs")]
    public async Task<IActionResult> CreateRun([FromBody] SeoContentGuardRun body, CancellationToken ct)
    {
        var result = await guard.CreateRunAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("runs/{runId:guid}")]
    public async Task<IActionResult> UpdateRun(Guid runId, [FromBody] SeoContentGuardRun body, CancellationToken ct)
    {
        body.Id = runId;
        var result = await guard.UpdateRunAsync(body, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpGet("runs/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runId, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        var result = await guard.GetRunAsync(runId, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpGet("maintenance/daily-scan")]
    public async Task<IActionResult> ListDailyScan([FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await guard.ListProjectsForDailyScanAsync(Math.Clamp(limit, 1, 200), ct);
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
