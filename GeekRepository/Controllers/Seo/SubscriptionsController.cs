using GeekSeo.Application.Interfaces.Seo;
using GeekSeo.Application.Models.Seo;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Seo;

[ApiController]
[Route("repo/seo/subscriptions")]
public sealed class SubscriptionsController(ISubscriptionRepository subscriptions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid userId, CancellationToken ct)
    {
        var result = await subscriptions.GetByUserIdAsync(userId, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut]
    public async Task<IActionResult> Upsert(
        [FromQuery] Guid userId,
        [FromBody] UpsertSubscriptionRequest request,
        CancellationToken ct)
    {
        var result = await subscriptions.UpsertAsync(userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
