using GeekAPI.Auth;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// Connecting a calendar, a Drive folder or a bucket while a business is still signing up, and showing that the connection works. Called
/// by the portal's server with the onboarding key, like every other onboarding route, because the visitor has no sign-in yet. Only for a
/// business waiting for payment (<see cref="SignupConnections"/>). A refusal answers with its message and its code, and the status
/// comes from the code.
/// </summary>
[ApiController]
[RequireApiKey]
[Route("api/v1/onboarding/connections")]
[EnableRateLimiting(OnboardingRateLimit.PolicyName)]
public sealed class SignupConnectionsController(SignupConnections connections) : ControllerBase
{
    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] BeginSignupConnectionRequest? request, CancellationToken ct)
    {
        var result = await connections.BeginAsync(request, ct);
        return result.IsSuccess ? Ok(new { authorizationUrl = result.Value }) : Refuse(result.Error, result.ErrorCode, "The connection could not be started.");
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete([FromBody] CompleteSignupConnectionRequest? request, CancellationToken ct)
    {
        var result = await connections.CompleteAsync(request, ct);
        return result.IsSuccess ? Ok(new { connected = true, provider = request!.Provider }) : Refuse(result.Error, result.ErrorCode, "The connection could not be completed.");
    }

    [HttpPost("bucket")]
    public async Task<IActionResult> SaveBucket([FromBody] SaveSignupBucketRequest? request, CancellationToken ct)
    {
        var result = await connections.SaveBucketAsync(request, ct);
        return result.IsSuccess ? Ok(new { connected = true, bucket = result.Value }) : Refuse(result.Error, result.ErrorCode, "The bucket could not be saved.");
    }

    /// <summary>Uses the connection: the calendar lists its calendars, a Drive folder or a bucket is written a small file.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] CheckSignupConnectionRequest? request, CancellationToken ct)
    {
        var result = await connections.CheckAsync(request?.TenantId, request?.Provider, ct);
        return result.IsSuccess ? Ok(new { connected = true, provider = request!.Provider, shown = result.Value }) : Refuse(result.Error, result.ErrorCode, "The connection could not be checked.");
    }

    private ObjectResult Refuse(string? error, string? code, string fallback) =>
        StatusCode(StatusFor(code), new { error = string.IsNullOrWhiteSpace(error) ? fallback : error, code });

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        McpFailureCodes.ProviderNotSupported
            or McpFailureCodes.StateInvalid
            or McpFailureCodes.StateExpired
            or McpFailureCodes.AuthorizationDenied
            or McpFailureCodes.CodeExchangeFailed
            or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        McpFailureCodes.TenantForbidden => StatusCodes.Status403Forbidden,
        McpFailureCodes.StorageNotConnected
            or McpFailureCodes.CalendarNotConnected
            or VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        McpFailureCodes.ReauthorizationRequired or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        McpFailureCodes.StorageWriteFailed or McpFailureCodes.ToolError => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
