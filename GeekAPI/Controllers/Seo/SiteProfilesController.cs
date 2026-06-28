using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GeekAPI.Models;
using GeekAPI.Services.SiteAnalyzer2;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

/// <summary>
/// Reads Site Analyzer 2 <c>sa2.site_profiles</c> directly via <c>SITE_ANALYZER2_DATABASE_URL</c>
/// and proxies site bundles from Site Analyzer API when configured.
/// </summary>
[ApiController]
[Route("api/seo/internal/site-profiles")]
public sealed class SiteProfilesController(
    SiteAnalyzer2SiteProfileReader reader,
    SiteAnalyzer2SiteBundleClient bundleClient) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery(Name = "site_profile")] Guid site_profile,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        _ = userId;
        if (site_profile == Guid.Empty)
            return BadRequest(new { error = "site_profile query parameter is required." });

        var profile = await reader.GetByIdAsync(site_profile, ct);
        return profile is null ? NotFound() : Ok(profile);
    }

    [HttpGet("{siteProfileId:guid}/content-writer-bundle")]
    public async Task<IActionResult> GetContentWriterBundle(Guid siteProfileId, [FromQuery] Guid userId, CancellationToken ct)
    {
        _ = userId;
        if (siteProfileId == Guid.Empty)
            return BadRequest(new { error = "siteProfileId is required." });

        var bundle = await bundleClient.GetByProfileIdAsync(siteProfileId, ct);
        return bundle is null ? NotFound() : Ok(bundle);
    }
}
