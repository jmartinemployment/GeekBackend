using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/site-audits")]
public sealed class SiteAuditsController(ISiteAuditRepository audits, IProjectRepository projects) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(projectId, userId, ct);
        if (owned is not null)
            return owned;

        var result = await audits.ListByProjectAsync(projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{auditId:guid}")]
    public async Task<IActionResult> Get(Guid auditId, [FromQuery] Guid userId, CancellationToken ct)
    {
        var denied = await EnsureAuditOwnedAsync(auditId, userId, ct);
        if (denied is not null)
            return denied;

        var result = await audits.GetByIdAsync(auditId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromQuery] Guid userId, [FromBody] CreateSiteAuditRequest request, CancellationToken ct)
    {
        var owned = await EnsureProjectAsync(request.ProjectId, userId, ct);
        if (owned is not null)
            return owned;

        var audit = new SeoSiteAudit
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            Status = "running",
            PagesCrawled = 0,
            StartedAt = DateTimeOffset.UtcNow,
        };

        var result = await audits.CreateAsync(audit, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPatch("{auditId:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid auditId,
        [FromQuery] Guid userId,
        [FromBody] UpdateSiteAuditStatusRequest request,
        CancellationToken ct)
    {
        var denied = await EnsureAuditOwnedAsync(auditId, userId, ct);
        if (denied is not null)
            return denied;

        var result = await audits.UpdateStatusAsync(auditId, request, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    [HttpPost("{auditId:guid}/pages")]
    public async Task<IActionResult> AppendPages(
        Guid auditId,
        [FromQuery] Guid userId,
        [FromBody] AppendSiteAuditPagesRequest request,
        CancellationToken ct)
    {
        var denied = await EnsureAuditOwnedAsync(auditId, userId, ct);
        if (denied is not null)
            return denied;

        var result = await audits.AppendPagesAsync(auditId, request, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }

    private async Task<IActionResult?> EnsureProjectAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await projects.GetByIdAsync(projectId, userId, ct);
        if (!project.IsSuccess)
            return project.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(project.Error);
        return null;
    }

    private async Task<IActionResult?> EnsureAuditOwnedAsync(Guid auditId, Guid userId, CancellationToken ct)
    {
        var auditResult = await audits.GetByIdAsync(auditId, ct);
        if (!auditResult.IsSuccess || auditResult.Value is null)
            return auditResult.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ? NotFound() : BadRequest(auditResult.Error);

        return await EnsureProjectAsync(auditResult.Value.ProjectId, userId, ct);
    }
}
