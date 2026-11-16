using GeekApplication.Models.Social;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekRepository.Controllers.Social;

/// <summary>
/// Result to HTTP, one way for every social controller, read off the failure's code and never off
/// its wording: a value is 200, not_found is 404, invalid_request is 400, conflict (a lost race or a
/// draft that is no longer in the state the caller read) is 409, and anything else, a store that is
/// down included, is 503 carrying the message so the caller reports it instead of treating the
/// answer as empty.
/// </summary>
internal static class SocialResults
{
    public static IActionResult ToAction<T>(ControllerBase controller, Result<T> result) =>
        result.IsSuccess ? controller.Ok(result.Value) : Failed(controller, result.ErrorCode, result.Error);

    /// <summary>The header GeekAPI sends with every tenant-scoped call: the tenant the signed-in person owns.</summary>
    public const string TenantHeader = "X-Social-Tenant-Id";

    /// <summary>
    /// Reads the tenant a call is for. A call with no tenant, or one that is not a tenant id, is refused 400 and
    /// never answered from "every tenant", so a caller that forgot its tenant fails loudly instead of leaking.
    /// </summary>
    public static bool TryTenant(ControllerBase controller, out Guid tenantId, out IActionResult refusal)
    {
        tenantId = Guid.Empty;
        refusal = null!;
        var raw = controller.Request.Headers[TenantHeader].ToString();
        if (Guid.TryParse(raw, out tenantId) && tenantId != Guid.Empty) return true;

        refusal = controller.BadRequest($"{TenantHeader} is required and must be a tenant id.");
        return false;
    }

    public static IActionResult Failed(ControllerBase controller, string? errorCode, string? error)
    {
        var message = string.IsNullOrWhiteSpace(error) ? "The social store failed." : error;
        return errorCode switch
        {
            SocialFailureCodes.NotFound => controller.NotFound(message),
            SocialFailureCodes.InvalidRequest => controller.BadRequest(message),
            SocialFailureCodes.Conflict => controller.Conflict(message),
            _ => controller.StatusCode(StatusCodes.Status503ServiceUnavailable, message),
        };
    }
}
