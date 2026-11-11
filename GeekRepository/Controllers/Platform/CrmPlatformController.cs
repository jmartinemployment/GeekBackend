using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using GeekRepository.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Platform;

/// <summary>
/// repo/platform/* — platform-scoped Geek-CRM data (tenant subscriptions) for GeekAPI only. GeekAPI reaches it
/// only behind the platform-admin key; the database also refuses it to any operation that has not set the
/// platform flag. Status codes come from the failure code, never its wording.
/// </summary>
[ApiController]
[Route("repo/platform")]
[Authorize(Policy = RepositoryAuthConstants.InternalServicePolicy)]
public sealed class CrmPlatformController(ICrmPlatformStore platform) : ControllerBase
{
    [HttpGet("subscriptions")]
    public async Task<IActionResult> ListSubscriptions([FromQuery] Guid? subscriberTenantId, CancellationToken ct) =>
        ToAction(await platform.ListSubscriptionsAsync(subscriberTenantId, ct));

    [HttpPost("subscriptions")]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateTenantSubscriptionCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A subscription is required.") : ToAction(await platform.CreateSubscriptionAsync(command, ct));

    [HttpPut("subscriptions/{subscriptionId:guid}")]
    public async Task<IActionResult> UpdateSubscription(Guid subscriptionId, [FromBody] UpdateTenantSubscriptionCommand command, CancellationToken ct) =>
        command is null ? BadRequest("A subscription is required.") : ToAction(await platform.UpdateSubscriptionAsync(subscriptionId, command, ct));

    private IActionResult ToAction<T>(Result<T> result)
    {
        if (result.IsSuccess) return Ok(result.Value);
        var message = string.IsNullOrWhiteSpace(result.Error) ? "The CRM store failed." : result.Error;
        return result.ErrorCode switch
        {
            CrmFailureCodes.NotFound => NotFound(message),
            CrmFailureCodes.InvalidRequest => BadRequest(message),
            CrmFailureCodes.Conflict => Conflict(message),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, message),
        };
    }
}
