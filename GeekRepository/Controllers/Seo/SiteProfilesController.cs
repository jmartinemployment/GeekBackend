using GeekRepository.Repositories.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/site-profiles")]
public sealed class SiteProfilesController(SiteAnalyzerSiteProfileRepository profiles) : ControllerBase
{
    [HttpGet("{siteProfileId:guid}/content-writer-bundle")]
    public async Task<IActionResult> GetContentWriterBundle(
        Guid siteProfileId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        _ = userId;
        if (siteProfileId == Guid.Empty)
            return BadRequest(new { error = "siteProfileId is required." });

        var result = await profiles.GetContentWriterBundleAsync(siteProfileId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound()
                : BadRequest(result.Error);
        return Ok(result.Value);
    }

    /// <summary>
    /// The by-project bundle. <c>SiteAnalyzerSiteProfileRepository</c> has always implemented this;
    /// it had no route, so the only way to reach it was GeekAPI's own controller opening an
    /// NpgsqlConnection against <c>sa2</c> directly. That controller is gone (2026-09-29, Jeff:
    /// "UNAUTHORIZED DIRECT CALLS TO SUPABASE ARE TO BE DELETED"), so the authorized path needs the
    /// endpoint the unauthorized one was standing in for.
    /// </summary>
    [HttpGet("by-project/{geekSeoProjectId:guid}/content-writer-bundle")]
    public async Task<IActionResult> GetContentWriterBundleByProject(
        Guid geekSeoProjectId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        _ = userId;
        if (geekSeoProjectId == Guid.Empty)
            return BadRequest(new { error = "geekSeoProjectId is required." });

        var result = await profiles.GetContentWriterBundleByGeekSeoProjectIdAsync(geekSeoProjectId, ct);
        if (!result.IsSuccess)
            return result.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true
                ? NotFound()
                : BadRequest(result.Error);
        return Ok(result.Value);
    }
}
