using GeekApplication.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/serp-cache")]
public sealed class SerpCacheController(ISerpCacheRepository cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string keyword,
        [FromQuery] string location,
        [FromQuery] string languageCode = "en",
        CancellationToken ct = default)
    {
        var result = await cache.GetAsync(keyword, location, languageCode, ct);
        if (!result.IsSuccess)
            return BadRequest(result.Error);
        return result.Value is null ? NotFound() : Ok(result.Value);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        [FromQuery] string keyword,
        [FromQuery] string location,
        [FromQuery] string languageCode = "en",
        CancellationToken ct = default)
    {
        var result = await cache.DeleteAsync(keyword, location, languageCode, ct);
        return result.IsSuccess ? NoContent() : BadRequest(result.Error);
    }
}
