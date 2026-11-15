using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The last step of /signup (docs/Geek-Receptionist.md section 5, item 4): the portal's server, with X-API-Key, asks for the owner's
/// login once the subscription is active. The owner is not signed in yet, so there is no token to check; the tenant must already be
/// active, which only a verified PayPal subscription makes it. A refusal answers with its message and its code.
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
[EnableRateLimiting(OnboardingRateLimit.PolicyName)]
public sealed class OwnerLoginController(OwnerLoginProvisioner owners) : ControllerBase
{
    [HttpPost("create-owner-login")]
    public async Task<IActionResult> Create([FromBody] CreateOwnerLoginRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "An owner login request is required.", code = VoiceFailureCodes.InvalidRequest });

        var result = await owners.CreateAsync(request, ct);
        if (result.IsSuccess) return StatusCode(StatusCodes.Status201Created, result.Value);

        var error = string.IsNullOrWhiteSpace(result.Error) ? "The owner login could not be created." : result.Error;
        return StatusCode(StatusFor(result.ErrorCode), new { error, code = result.ErrorCode });
    }

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the service being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        OwnerLoginFailureCodes.CredentialsRefused or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        OwnerLoginFailureCodes.OwnerExists
            or OwnerLoginFailureCodes.TenantNotActive
            or VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
