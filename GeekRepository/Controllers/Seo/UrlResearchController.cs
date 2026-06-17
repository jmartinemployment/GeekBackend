using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/url-research")]
public sealed class UrlResearchController(IUrlResearchService research) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid userId, [FromQuery] Guid projectId, CancellationToken ct)
    {
        var result = await research.ListSummaryByProjectAsync(userId, projectId, ct);
        return result.IsSuccess ? Ok(result.Value) : ForbiddenOrBadRequest(result.Error);
    }

    [HttpGet("{id:guid}/full")]
    public async Task<IActionResult> GetFull(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await research.GetFullAsync(userId, id, ct);
        if (!result.IsSuccess)
            return NotFoundOrForbidden(result);
        return Ok(result.Value);
    }

    [HttpPost("queued")]
    public async Task<IActionResult> CreateQueued(
        [FromQuery] Guid userId, [FromBody] CreateUrlResearchQueuedRequest request, CancellationToken ct)
    {
        var result = await research.CreateQueuedAsync(userId, request, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetFull), new { id = result.Value!.Id, userId }, result.Value)
            : ForbiddenOrBadRequest(result.Error);
    }

    [HttpPut("{id:guid}/full")]
    public async Task<IActionResult> PersistFull(
        Guid id, [FromQuery] Guid userId, [FromBody] UrlResearchFullWrite body, CancellationToken ct)
    {
        var result = await research.PersistFullAsync(userId, id, body, ct);
        if (!result.IsSuccess)
            return NotFoundOrForbidden(result);
        return Ok(result.Value);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromQuery] Guid userId, [FromBody] UrlResearchStatusPatch body, CancellationToken ct)
    {
        var result = await research.UpdateStatusAsync(userId, id, body, ct);
        if (!result.IsSuccess)
            return NotFoundOrForbidden(result);
        return Ok(result.Value);
    }

    [HttpGet("maintenance/queued")]
    public async Task<IActionResult> ListQueued(
        [FromQuery] int limit = 3,
        CancellationToken ct = default)
    {
        var repo = HttpContext.RequestServices.GetRequiredService<IUrlResearchRepository>();
        var result = await repo.ListQueuedAsync(Math.Clamp(limit, 1, 20), ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("maintenance/fail-stale-running")]
    public async Task<IActionResult> FailStaleRunning(
        [FromQuery] int maxAgeMinutes = 15,
        CancellationToken ct = default)
    {
        var repo = HttpContext.RequestServices.GetRequiredService<IUrlResearchRepository>();
        var result = await repo.FailStaleRunningAsync(
            TimeSpan.FromMinutes(Math.Clamp(maxAgeMinutes, 1, 60)), ct);
        return result.IsSuccess ? Ok(new { failedCount = result.Value }) : BadRequest(result.Error);
    }

    [HttpPatch("maintenance/{id:guid}/claim-running")]
    public async Task<IActionResult> ClaimRunning(Guid id, CancellationToken ct)
    {
        var repo = HttpContext.RequestServices.GetRequiredService<IUrlResearchRepository>();
        var result = await repo.TryClaimRunningAsync(id, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return result.Value ? Ok(new { claimed = true }) : Conflict(new { claimed = false });
    }

    private static IActionResult ForbiddenOrBadRequest(string? error) =>
        error?.Contains("Access denied", StringComparison.OrdinalIgnoreCase) == true
            ? new ObjectResult(error) { StatusCode = StatusCodes.Status403Forbidden }
            : new BadRequestObjectResult(error);

    private IActionResult NotFoundOrForbidden<T>(GeekSeo.Application.Results.Result<T> result)
    {
        if (result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
            return NotFound();
        if (result.Error?.Contains("Access denied", StringComparison.OrdinalIgnoreCase) == true)
            return StatusCode(StatusCodes.Status403Forbidden, result.Error);
        return BadRequest(result.Error);
    }
}
