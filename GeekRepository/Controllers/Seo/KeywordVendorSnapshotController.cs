using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/keyword-vendor-snapshots")]
public sealed class KeywordVendorSnapshotController(IKeywordVendorSnapshotRepository snapshots) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string seedKeyword,
        [FromQuery] string location,
        [FromQuery] string languageCode = "en",
        CancellationToken ct = default)
    {
        var result = await snapshots.GetAsync(seedKeyword, location, languageCode, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] SeoKeywordVendorSnapshot body, CancellationToken ct = default)
    {
        var result = await snapshots.UpsertAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
