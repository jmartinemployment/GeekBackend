using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Persistence.Entities;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/serp-deep-cache")]
public sealed class SerpDeepCacheController(ISerpDeepCacheRepository cache) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string keyword,
        [FromQuery] string location,
        [FromQuery] int resultCount = 50,
        CancellationToken ct = default)
    {
        var result = await cache.GetAsync(keyword, location, resultCount, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] SeoSerpDeepCache body, CancellationToken ct)
    {
        var result = await cache.UpsertAsync(body, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
