using System.Security.Claims;
using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// Connecting a tenant's calendar (docs/Geek-Receptionist.md sections 6 and 7). The authorize route is called by the portal with the
/// owner's token: the tenant comes from the token's tenant_id claim, and a tenant in the URL that differs from it is refused (403), so
/// a URL is never believed on its own. The callback is Google's redirect in the owner's browser, so it carries no token and no API key
/// (ApiKeyMiddleware lets this one path through): the sealed state is what identifies the tenant. The callback never answers a browser
/// with JSON. It sends the owner back to the portal's dashboard with mcp=connected, or mcp=failed and the failure code, and every
/// failure is logged first.
/// </summary>
[ApiController]
public sealed class McpOAuthController(McpConnectionService connections, GoogleCalendarOptions options, ILogger<McpOAuthController> logger) : ControllerBase
{
    internal const string TenantClaim = "tenant_id";

    [HttpGet("api/v1/tenants/{tenantId:guid}/oauth/mcp/{provider}/authorize")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Authorize(Guid tenantId, string provider, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(TenantClaim), out var claimed) || claimed == Guid.Empty)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "The sign-in carries no tenant.", code = McpFailureCodes.TenantUnverified });
        if (claimed != tenantId)
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "That tenant is not yours.", code = McpFailureCodes.TenantForbidden });

        var result = await connections.BeginAsync(tenantId, provider, ct);
        if (result.IsSuccess) return Ok(new { authorizationUrl = result.Value });

        var error = string.IsNullOrWhiteSpace(result.Error) ? "The connection could not be started." : result.Error;
        return StatusCode(StatusFor(result.ErrorCode), new { error, code = result.ErrorCode });
    }

    [HttpGet("api/v1/oauth/mcp/{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(string provider, [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        // Checked before Google's one-time code is spent: with nowhere to send the owner back, the code would be used and the outcome lost.
        if (string.IsNullOrWhiteSpace(options.PortalBaseUrl))
        {
            logger.LogError("The calendar callback cannot answer: PORTAL_BASE_URL is not set.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "The portal address is not configured.", code = McpFailureCodes.PortalNotConfigured });
        }

        var result = await connections.CompleteAsync(provider, code, state, error, ct);
        if (result.IsSuccess)
            return Redirect($"{options.PortalBaseUrl}/dashboard?mcp=connected&provider={Uri.EscapeDataString(provider)}");

        var failure = result.ErrorCode ?? McpFailureCodes.OAuthUnavailable;
        logger.LogWarning("The {Provider} calendar connection failed: {Code}.", provider, failure);
        return Redirect($"{options.PortalBaseUrl}/dashboard?mcp=failed&provider={Uri.EscapeDataString(provider)}&code={Uri.EscapeDataString(failure)}");
    }

    /// <summary>The HTTP status a failure code is answered with on the authorize route. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        McpFailureCodes.ProviderNotSupported
            or McpFailureCodes.StateInvalid
            or McpFailureCodes.StateExpired
            or McpFailureCodes.AuthorizationDenied
            or McpFailureCodes.CodeExchangeFailed
            or McpFailureCodes.ScopeNotGranted
            or McpFailureCodes.RefreshTokenMissing
            or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        McpFailureCodes.TenantUnverified or McpFailureCodes.TenantForbidden => StatusCodes.Status403Forbidden,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        VoiceFailureCodes.Conflict or McpFailureCodes.TenantNotActive => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
