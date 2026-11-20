using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekAPI.Services.Voice.Storage;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The owner's own check that the storage they connected still works (plans/client-and-app-plan.md, "Call logs and transcripts").
///
/// It asks for the tenant's live access token, which renews the token when it is close to expiring, so it exercises the one thing most
/// likely to have broken since: an owner who withdrew this application's access in their Google account learns it here rather than from
/// a call record that could not be written. It writes nothing, so checking leaves nothing behind in the owner's folder.
///
/// The tenant comes from the owner's tenant_id claim and a URL naming another tenant is refused (403).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ReceptionistAuth.Scheme)]
public sealed class StorageController(GoogleAccessTokens tokens) : ControllerBase
{
    [HttpGet("api/v1/tenants/{tenantId:guid}/storage/check")]
    public async Task<IActionResult> Check(Guid tenantId, CancellationToken ct)
    {
        var refused = OwnerTenantClaim.Refuse(User, tenantId);
        if (refused is not null) return refused;

        var token = await tokens.ForAsync(tenantId, GoogleDriveOptions.Provider, McpFailureCodes.StorageNotConnected, ct);
        if (token.IsSuccess) return Ok(new { connected = true, provider = GoogleDriveOptions.Provider, folder = GoogleDriveOptions.FolderName });

        var error = string.IsNullOrWhiteSpace(token.Error) ? "The storage connection could not be checked." : token.Error;
        return StatusCode(StatusFor(token.ErrorCode), new { error, code = token.ErrorCode });
    }

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        McpFailureCodes.StorageNotConnected or VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        McpFailureCodes.ReauthorizationRequired or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        McpFailureCodes.StorageWriteFailed => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
