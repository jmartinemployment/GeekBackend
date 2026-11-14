using GeekAPI.Services.Voice.Onboarding;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The onboarding routes of Geek-Receptionist (docs/Geek-Receptionist.md section 6). The visitor is not signed in, so the call is
/// made by the Geek-Receptionist-Portal's server with the X-API-Key ApiKeyMiddleware checks, and it forwards the visitor's address and
/// country (<see cref="OnboardingVisitor"/>). OnboardingRateLimit caps each visitor address. A refusal answers with
/// its message and its code, and the status comes from the code, never from the wording.
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
[EnableRateLimiting(OnboardingRateLimit.PolicyName)]
public sealed class OnboardingController(OnboardingEligibility eligibility) : ControllerBase
{
    [HttpPost("validate-eligibility")]
    public async Task<IActionResult> ValidateEligibility([FromBody] ValidateEligibilityRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "A signup is required.", code = VoiceFailureCodes.InvalidRequest });

        var result = await eligibility.ValidateAsync(request, OnboardingVisitor.From(HttpContext), ct);
        if (result.IsSuccess) return Ok(result.Value);

        var error = string.IsNullOrWhiteSpace(result.Error) ? "The signup failed." : result.Error;
        return StatusCode(StatusFor(result.ErrorCode), new { error, code = result.ErrorCode });
    }

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the store being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        OnboardingFailureCodes.CountryNotSupported
            or OnboardingFailureCodes.IndustryExcluded
            or OnboardingFailureCodes.IndustryAddendumNotAvailable
            or OnboardingFailureCodes.AddendumRequired
            or OnboardingFailureCodes.AttestationRequired
            or OnboardingFailureCodes.VisitorUnverified
            or OnboardingFailureCodes.VisitorCountryNotSupported
            or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
