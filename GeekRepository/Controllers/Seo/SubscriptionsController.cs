using GeekSeo.Application.Interfaces.Seo;
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
}
