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
}
