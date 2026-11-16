using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GeekAPI.Auth;

/// <summary>
/// Refuses a request unless ApiKeyMiddleware checked its X-API-Key. The middleware lets a request through without the key when a
/// GeekOAuth token validated, and GeekOAuth is shared across Geek apps with no audience check, so a route meant for a server holding
/// the key (the Geek-Receptionist portal's onboarding and owner sign-in) says so with this, and a signed-in user of any Geek app is
/// refused there. Answered 401 with the code api_key_required.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string FailureCode = "api_key_required";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.HttpContext.Items.TryGetValue(CrmAuthConstants.VerifiedApiKeyItem, out var verified) && verified is true) return;

        context.Result = new ObjectResult(new { error = "This route needs the X-API-Key.", code = FailureCode })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };
    }
}
