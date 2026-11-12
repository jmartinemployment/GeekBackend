using System.Security.Cryptography;
using System.Text;
using GeekApplication.Interfaces.Crm;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeekAPI.Controllers.Platform;

/// <summary>
/// Which tenant may use which multi-tenant Geek product. Behind the usual X-API-Key and a second key,
/// <c>X-Platform-Admin-Key</c>, compared with GEEK_PLATFORM_ADMIN_KEY. With the variable unset the routes answer 503
/// (fail closed, never open); a missing or wrong header is 401. GeekRepository's database refuses the table to anything else.
/// </summary>
[ApiController]
[Route("api/platform")]
[PlatformAdminKey]
public sealed class CrmPlatformController(ICrmPlatformStore platform) : ControllerBase
{
    [HttpGet("subscriptions")]
    public async Task<IActionResult> ListSubscriptions([FromQuery] Guid? subscriberTenantId, CancellationToken ct) =>
        ToAction(await platform.ListSubscriptionsAsync(subscriberTenantId, ct));

    /// <summary>productCode must be an active geek_saas product of Geek @ Your Spot; holdingId optionally links its own holding.</summary>
    [HttpPost("subscriptions")]
    public async Task<IActionResult> CreateSubscription([FromBody] CreateSubscriptionRequest? body, CancellationToken ct) =>
        body is null || body.SubscriberTenantId is null || body.StartOn is null
            ? BadRequest("A subscription needs subscriberTenantId, productCode, status and startOn.")
            : ToAction(await platform.CreateSubscriptionAsync(new CreateTenantSubscriptionCommand(
                body.SubscriberTenantId.Value, body.ProductCode ?? string.Empty, body.Plan, body.Status ?? string.Empty, body.StartOn.Value, body.EndOn, body.HoldingId), ct));

    [HttpPut("subscriptions/{subscriptionId:guid}")]
    public async Task<IActionResult> UpdateSubscription(Guid subscriptionId, [FromBody] UpdateSubscriptionRequest? body, CancellationToken ct) =>
        body is null
            ? BadRequest("A subscription is required.")
            : ToAction(await platform.UpdateSubscriptionAsync(subscriptionId, new UpdateTenantSubscriptionCommand(body.Plan, body.Status ?? string.Empty, body.EndOn), ct));

    // ----- Tenant membership (Phase 4): who may sign in to a tenant's CRM, and as what -----

    [HttpGet("tenant-users")]
    public async Task<IActionResult> ListTenantUsers([FromQuery] Guid? tenantId, CancellationToken ct) =>
        ToAction(await platform.ListTenantUsersAsync(tenantId, ct));

    /// <summary>userId is the GeekOAuth user's id (the JWT <c>sub</c>); role is owner or member. A user belongs to one tenant.</summary>
    [HttpPost("tenant-users")]
    public async Task<IActionResult> AddTenantUser([FromBody] AddTenantUserRequest? body, CancellationToken ct) =>
        body is null || body.TenantId is null || body.UserId is null
            ? BadRequest("A membership needs tenantId, userId and role.")
            : ToAction(await platform.AddTenantUserAsync(new AddTenantUserCommand(body.TenantId.Value, body.UserId.Value, body.Role ?? string.Empty), ct));

    [HttpPut("tenant-users/{id:guid}")]
    public async Task<IActionResult> SetTenantUserRole(Guid id, [FromBody] SetTenantUserRoleRequest? body, CancellationToken ct) =>
        body is null ? BadRequest("A role is required.") : ToAction(await platform.SetTenantUserRoleAsync(id, new SetTenantUserRoleCommand(body.Role ?? string.Empty), ct));

    [HttpDelete("tenant-users/{id:guid}")]
    public async Task<IActionResult> RemoveTenantUser(Guid id, CancellationToken ct) =>
        ToAction(await platform.RemoveTenantUserAsync(id, ct));

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

public sealed record CreateSubscriptionRequest(Guid? SubscriberTenantId, string? ProductCode, string? Plan, string? Status, DateOnly? StartOn, DateOnly? EndOn, Guid? HoldingId);

public sealed record AddTenantUserRequest(Guid? TenantId, Guid? UserId, string? Role);

public sealed record SetTenantUserRoleRequest(string? Role);

public sealed record UpdateSubscriptionRequest(string? Plan, string? Status, DateOnly? EndOn);

/// <summary>The platform-admin gate: the key is read from GEEK_PLATFORM_ADMIN_KEY on every call and compared in constant time.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlatformAdminKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string Header = "X-Platform-Admin-Key";
    public const string Variable = "GEEK_PLATFORM_ADMIN_KEY";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()[Variable];
        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Result = new ObjectResult(new { success = false, error = $"{Variable} is not set, so platform routes are closed." }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return Task.CompletedTask;
        }

        var given = context.HttpContext.Request.Headers[Header].ToString();
        var same = given.Length > 0 && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        if (!same) context.Result = new UnauthorizedObjectResult(new { success = false, error = "A platform-admin key is required." });
        return Task.CompletedTask;
    }
}
