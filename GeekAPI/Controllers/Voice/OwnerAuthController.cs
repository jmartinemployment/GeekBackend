using GeekAPI.Auth;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.OwnerAuth;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// Geek-Receptionist's owner accounts (docs/Geek-Receptionist.md sections 5 and 7). Every route is called by the portal's server with
/// X-API-Key, and <see cref="RequireApiKeyAttribute"/> refuses a call without it, a GeekOAuth token included; the owner's browser never
/// calls GeekAPI. Creating a login carries the signup cap and signing in its own cap of the same size, because
/// both need no token; refresh and sign-out need a refresh token, which cannot be guessed. Answers holding tokens are never cached. A refusal is
/// answered with its message and its code.
/// </summary>
[ApiController]
[RequireApiKey]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OwnerAuthController(OwnerAuthService owners) : ControllerBase
{
    [HttpPost("api/v1/onboarding/create-owner-login")]
    [EnableRateLimiting(OnboardingRateLimit.PolicyName)]
    public async Task<IActionResult> Create([FromBody] CreateOwnerLoginRequest request, CancellationToken ct)
    {
        var result = await owners.CreateLoginAsync(request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Refusal(result, "The owner login could not be created.");
    }

    [HttpPost("api/v1/owner-auth/sign-in")]
    [EnableRateLimiting(OnboardingRateLimit.SignInPolicyName)]
    public async Task<IActionResult> SignIn([FromBody] OwnerSignInRequest request, CancellationToken ct)
    {
        var result = await owners.SignInAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : Refusal(result, "Sign-in failed.");
    }

    [HttpPost("api/v1/owner-auth/refresh")]
    public async Task<IActionResult> Refresh([FromBody] OwnerRefreshRequest request, CancellationToken ct)
    {
        var result = await owners.RefreshAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : Refusal(result, "The sign-in could not be renewed.");
    }

    [HttpPost("api/v1/owner-auth/sign-out")]
    public async Task<IActionResult> SignOut([FromBody] OwnerRefreshRequest request, CancellationToken ct)
    {
        var result = await owners.SignOutAsync(request, ct);
        return result.IsSuccess ? NoContent() : Refusal(result, "The sign-in could not be ended.");
    }

    private ObjectResult Refusal<T>(Result<T> result, string fallback) =>
        StatusCode(StatusFor(result.ErrorCode), new { error = string.IsNullOrWhiteSpace(result.Error) ? fallback : result.Error, code = result.ErrorCode });

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        OwnerAuthFailureCodes.InvalidCredentials
            or OwnerAuthFailureCodes.RefreshTokenInvalid
            or VoiceOwnerFailureCodes.RefreshTokenReused => StatusCodes.Status401Unauthorized,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        OwnerAuthFailureCodes.OwnerExists
            or OwnerAuthFailureCodes.TenantNotActive
            or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
