using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/site-research")]
public sealed class SiteResearchController(ISiteResearchRepository siteResearch) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> GetOrCreate(
        [FromQuery] Guid userId, [FromBody] CreateSiteResearchRequest request, CancellationToken ct)
    {
        var result = await siteResearch.GetOrCreateForProjectAsync(userId, request, ct);
        if (!result.IsSuccess)
            return ForbiddenOrBadRequest(result.Error);
        return Ok(result.Value);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWithPages(Guid id, [FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await siteResearch.GetWithPagesAsync(id, ct);
        if (!result.IsSuccess)
            return NotFound();
        if (result.Value!.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "Access denied");
        return Ok(result.Value);
    }

    [HttpPut("{id:guid}/step1")]
    public async Task<IActionResult> PersistStep1(
        Guid id, [FromQuery] Guid userId, [FromBody] SiteResearchStep1Write body, CancellationToken ct)
    {
        var access = await EnsureAccessAsync(id, userId, ct);
        if (access is not null)
            return access;

        var result = await siteResearch.PersistStep1Async(id, body, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFoundOrBadRequest(result);
    }

    [HttpPut("{id:guid}/pages")]
    public async Task<IActionResult> ReplacePages(
        Guid id, [FromQuery] Guid userId, [FromBody] IReadOnlyList<SiteResearchPageWrite> pages, CancellationToken ct)
    {
        var access = await EnsureAccessAsync(id, userId, ct);
        if (access is not null)
            return access;

        var result = await siteResearch.ReplacePagesAsync(id, pages, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFoundOrBadRequest(result);
    }

    [HttpPut("{id:guid}/step4")]
    public async Task<IActionResult> PersistStep4(
        Guid id, [FromQuery] Guid userId, [FromBody] SiteResearchStep4Write body, CancellationToken ct)
    {
        var access = await EnsureAccessAsync(id, userId, ct);
        if (access is not null)
            return access;

        var result = await siteResearch.PersistStep4Async(id, body, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFoundOrBadRequest(result);
    }

    private async Task<IActionResult?> EnsureAccessAsync(Guid siteResearchId, Guid userId, CancellationToken ct)
    {
        var head = await siteResearch.GetWithPagesAsync(siteResearchId, ct);
        if (!head.IsSuccess || head.Value is null)
            return NotFound();
        if (head.Value.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "Access denied");
        return null;
    }

    private static IActionResult ForbiddenOrBadRequest(string? error) =>
        error?.Contains("Access denied", StringComparison.OrdinalIgnoreCase) == true
            ? new ObjectResult(error) { StatusCode = StatusCodes.Status403Forbidden }
            : new BadRequestObjectResult(error);

    private static IActionResult NotFoundOrBadRequest<T>(GeekSeo.Application.Results.Result<T> result) =>
        result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
            ? new NotFoundResult()
            : new BadRequestObjectResult(result.Error);
}
