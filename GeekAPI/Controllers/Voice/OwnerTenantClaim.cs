using System.Security.Claims;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The one check every owner route of Geek-Receptionist makes before it believes a tenant in a URL: the tenant comes from the signed-in
/// owner's tenant_id claim, in a token Geek-Receptionist signed itself (<see cref="ReceptionistAuth"/>), and a URL naming any other
/// tenant is refused. A token with no claim is refused too, so a login that was
/// never given a tenant can touch no tenant's data.
/// </summary>
internal static class OwnerTenantClaim
{
    internal const string TenantClaim = ReceptionistAuth.TenantClaim;

    /// <summary>Null when the token's tenant is the tenant in the URL; otherwise the 403 to send.</summary>
    internal static ObjectResult? Refuse(ClaimsPrincipal user, Guid tenantId)
    {
        if (!Guid.TryParse(user.FindFirstValue(TenantClaim), out var claimed) || claimed == Guid.Empty)
            return Forbidden("The sign-in carries no tenant.", McpFailureCodes.TenantUnverified);
        if (claimed != tenantId)
            return Forbidden("That tenant is not yours.", McpFailureCodes.TenantForbidden);
        return null;
    }

    private static ObjectResult Forbidden(string error, string code) =>
        new(new { error, code }) { StatusCode = StatusCodes.Status403Forbidden };
}
