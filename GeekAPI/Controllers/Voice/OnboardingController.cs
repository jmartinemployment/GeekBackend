using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The onboarding routes of Geek-Receptionist (docs/Geek-Receptionist.md section 6). The visitor is not signed in, so the call is
/// made by the Geek-Receptionist-Portal's server with the X-API-Key ApiKeyMiddleware checks, and it forwards the visitor's address and
/// country (<see cref="OnboardingVisitor"/>). OnboardingRateLimit caps each visitor address. A refusal answers with
/// its message and its code, and the status comes from the code, never from the wording. An unlisted code is the service being
/// unavailable (503), which covers an unset variable, a PayPal outage and an unusable encryption key.
/// </summary>
[ApiController]
[Route("api/v1/onboarding")]
[EnableRateLimiting(OnboardingRateLimit.PolicyName)]
public sealed class OnboardingController(OnboardingEligibility eligibility, SubscriptionActivation activation) : ControllerBase
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

    [HttpPost("activate-subscription")]
    public async Task<IActionResult> ActivateSubscription([FromBody] ActivateSubscriptionRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "An activation is required.", code = VoiceFailureCodes.InvalidRequest });

        var result = await activation.ActivateAsync(request, ct);
        if (result.IsSuccess) return Ok(result.Value);

        var error = string.IsNullOrWhiteSpace(result.Error) ? "The activation failed." : result.Error;
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
            or BillingFailureCodes.TierUnknown
            or BillingFailureCodes.SubscriptionNotFound
            or BillingFailureCodes.SubscriptionNotActive
            or BillingFailureCodes.SubscriptionUnpaid
            or BillingFailureCodes.PlanMismatch
            or BillingFailureCodes.VaultTokenInvalid
            or TelephonyFailureCodes.AreaCodeInvalid
            or ActivationFailureCodes.SmsNotificationsNotAvailable
            or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        VoiceFailureCodes.Conflict
            or ActivationFailureCodes.TenantNotPending => StatusCodes.Status409Conflict,
        TelephonyFailureCodes.ProvisioningFailed
            or RetellFailureCodes.ProvisioningFailed => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
