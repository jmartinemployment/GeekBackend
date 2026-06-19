using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/site-analyzer/step-runs")]
public sealed class SiteAnalyzerStepRunsController(
    ISiteResearchRepository siteResearch,
    IUrlResearchRepository urlResearch) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Upsert(
        [FromQuery] Guid userId, [FromBody] SiteAnalyzerStepRunUpsert body, CancellationToken ct)
    {
        var access = await EnsureAccessAsync(userId, body, ct);
        if (access is not null)
            return access;

        var result = await siteResearch.UpsertStepRunAsync(body, ct);
        return result.IsSuccess ? Ok() : BadRequest(result.Error);
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid userId,
        [FromQuery] Guid? siteResearchId,
        [FromQuery] Guid? urlResearchId,
        CancellationToken ct)
    {
        if (siteResearchId is Guid siteId)
        {
            var access = await EnsureSiteAccessAsync(siteId, userId, ct);
            if (access is not null)
                return access;

            var siteRuns = await siteResearch.GetStepRunsForSiteAsync(siteId, ct);
            return siteRuns.IsSuccess ? Ok(siteRuns.Value) : BadRequest(siteRuns.Error);
        }

        if (urlResearchId is Guid packId)
        {
            var access = await EnsurePackAccessAsync(packId, userId, ct);
            if (access is not null)
                return access;

            var packRuns = await siteResearch.GetStepRunsForPackAsync(packId, ct);
            return packRuns.IsSuccess ? Ok(packRuns.Value) : BadRequest(packRuns.Error);
        }

        return BadRequest("siteResearchId or urlResearchId is required.");
    }

    private async Task<IActionResult?> EnsureAccessAsync(
        Guid userId, SiteAnalyzerStepRunUpsert body, CancellationToken ct)
    {
        if (body.SiteResearchId is Guid siteId)
            return await EnsureSiteAccessAsync(siteId, userId, ct);
        if (body.UrlResearchId is Guid packId)
            return await EnsurePackAccessAsync(packId, userId, ct);
        return BadRequest("SiteResearchId or UrlResearchId is required.");
    }

    private async Task<IActionResult?> EnsureSiteAccessAsync(Guid siteResearchId, Guid userId, CancellationToken ct)
    {
        var site = await siteResearch.GetWithPagesAsync(siteResearchId, ct);
        if (!site.IsSuccess || site.Value is null)
            return NotFound();
        if (site.Value.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "Access denied");
        return null;
    }

    private async Task<IActionResult?> EnsurePackAccessAsync(Guid urlResearchId, Guid userId, CancellationToken ct)
    {
        var pack = await urlResearch.GetHeadAsync(urlResearchId, ct);
        if (!pack.IsSuccess || pack.Value is null)
            return NotFound();
        if (pack.Value.UserId != userId)
            return StatusCode(StatusCodes.Status403Forbidden, "Access denied");
        return null;
    }
}
