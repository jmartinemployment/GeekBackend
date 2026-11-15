using GeekAPI.Services.Voice.Mcp;
using GeekApplication.Models.Voice;
using GeekAPI.Services.Voice.OwnerAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>The query Google sent the portal: the one-time code and the sealed state, or the error when the person declined.</summary>
public sealed record CompleteMcpConnectionRequest(string? Code, string? State, string? Error);

/// <summary>
/// Connecting a tenant's calendar (docs/Geek-Receptionist.md sections 6 and 7). Both routes are called by the portal with the owner's
/// token: Google sends the owner's browser to the portal, and the portal, which holds the owner's session, calls these. The tenant comes
/// from the token's tenant_id claim and a different tenant in the URL is refused (403), so a URL is never believed on its own. Complete
/// also refuses an approval whose sealed state names another tenant, so a connection started for one tenant cannot be finished by a
/// stranger's Google account being tricked into approving it. A refusal is answered with its message and its code.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class McpOAuthController(McpConnectionService connections) : ControllerBase
{
    [HttpGet("api/v1/tenants/{tenantId:guid}/oauth/mcp/{provider}/authorize")]
    public async Task<IActionResult> Authorize(Guid tenantId, string provider, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var result = await connections.BeginAsync(tenantId, provider, ct);
        if (result.IsSuccess) return Ok(new { authorizationUrl = result.Value });

        return Refusal(result.Error, result.ErrorCode, "The connection could not be started.");
    }

    [HttpPost("api/v1/tenants/{tenantId:guid}/oauth/mcp/{provider}/complete")]
    public async Task<IActionResult> Complete(Guid tenantId, string provider, [FromBody] CompleteMcpConnectionRequest request, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        if (request is null)
            return BadRequest(new { error = "The approval Google sent is required.", code = VoiceFailureCodes.InvalidRequest });

        var result = await connections.CompleteAsync(tenantId, provider, request.Code, request.State, request.Error, ct);
        if (result.IsSuccess) return Ok(new { connected = true, provider });

        return Refusal(result.Error, result.ErrorCode, "The connection could not be completed.");
    }

    private ObjectResult Refusal(string? message, string? code, string fallback) =>
        StatusCode(StatusFor(code), new { error = string.IsNullOrWhiteSpace(message) ? fallback : message, code });

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
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
