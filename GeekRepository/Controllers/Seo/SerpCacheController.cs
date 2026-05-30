using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
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

    [HttpPost]
    public async Task<IActionResult> Upsert(
        [FromBody] SerpCacheUpsertRequest body,
        CancellationToken ct = default)
    {
        var result = await cache.UpsertAsync(
            body.Keyword, body.Location, body.LanguageCode, body.Serp, body.Benchmarks, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
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

public sealed record SerpCacheUpsertRequest
{
    public required string Keyword { get; init; }
    public required string Location { get; init; }
    public string LanguageCode { get; init; } = "en";
    public required SerpResult Serp { get; init; }
    public required SerpBenchmarksPayload Benchmarks { get; init; }
}
