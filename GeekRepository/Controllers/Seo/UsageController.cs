using GeekSeo.Application.Interfaces.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/usage")]
public sealed class UsageController(IUsageMeteringRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCount(
        [FromQuery] Guid userId,
        [FromQuery] string feature,
        [FromQuery] DateOnly periodStart,
        CancellationToken ct)
    {
        var result = await repository.GetCountAsync(userId, periodStart, feature, ct);
        return result.IsSuccess ? Ok(new { count = result.Value }) : BadRequest(result.Error);
    }

    [HttpPost("increment")]
    public async Task<IActionResult> Increment(
        [FromQuery] Guid userId,
        [FromQuery] string feature,
        [FromQuery] DateOnly periodStart,
        [FromQuery] int amount = 1,
        CancellationToken ct = default)
    {
        var result = await repository.IncrementAsync(userId, periodStart, feature, amount, ct);
        return result.IsSuccess ? Ok(new { count = result.Value }) : BadRequest(result.Error);
    }
}
