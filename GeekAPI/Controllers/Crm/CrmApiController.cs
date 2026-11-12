using GeekAPI.Auth;
using GeekApplication.Models.Crm;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Crm;

/// <summary>What every /api/crm controller shares: who is calling, which tenant they work in, and how a store failure becomes a status.</summary>
public abstract class CrmApiController : ControllerBase
{
    protected CrmCaller Caller => HttpContext.Items[CrmAuthConstants.CallerItem] as CrmCaller
        ?? throw new InvalidOperationException("CrmAccess did not run for this request.");

    /// <summary>A server names its tenant (default Geek @ Your Spot); a signed-in user works in the tenant tenant_users gives them, whatever the address says.</summary>
    protected Guid Tenant(Guid? requested) => Caller.IsServer ? requested ?? CrmSeed.GeekAtYourSpotTenantId : Caller.TenantId!.Value;

    protected IActionResult ToAction<T>(Result<T> result)
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
