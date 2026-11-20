using GeekAPI.Auth;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Onboarding;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The onboarding routes of Geek-Receptionist (docs/Geek-Receptionist.md section 6). The visitor is not signed in, so the call is
/// made by the Geek-Receptionist-Portal's server with the X-API-Key ApiKeyMiddleware checks (and <see cref="RequireApiKeyAttribute"/>
/// requires, so a GeekOAuth token is refused here), and it forwards the visitor's address and
/// country (<see cref="OnboardingVisitor"/>). OnboardingRateLimit caps each visitor address. A refusal answers with
/// its message and its code, and the status comes from the code, never from the wording. An unlisted code is the service being
/// unavailable (503), which covers an unset variable, a PayPal outage and an unusable encryption key.
/// </summary>
[ApiController]
[RequireApiKey]
[Route("api/v1/onboarding")]
[EnableRateLimiting(OnboardingRateLimit.PolicyName)]
public sealed class OnboardingController(OnboardingEligibility eligibility, IndustryRefusals refusals, SignupPayments payments, SubscriptionActivation activation, ReceptionistConfigWriter config) : ControllerBase
{
    /// <summary>
    /// What a business answered about how its receptionist should behave, on one step of the form. Every field is optional, so the
    /// portal decides whether it asks one setting per screen or groups several, and this route does not care which.
    ///
    /// <para>
    /// Refused once the business is active: after that its settings are its owner's to change, through their own token. The answers
    /// are saved here as each step is submitted, which is also why the portal's cookie never carries them.
    /// </para>
    /// </summary>
    [HttpPost("receptionist-config")]
    public async Task<IActionResult> SaveReceptionistConfig([FromBody] ReceptionistConfigStepRequest? request, CancellationToken ct)
    {
        var result = await config.ApplyDuringSignupAsync(request, ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Refuse(result.Error, result.ErrorCode, "The answers could not be saved.");
    }

    /// <summary>
    /// A visitor answered yes to an excluded industry on /signup. The yes and the answers before it are stored and kept forever;
    /// the signup ends there, on the portal, whatever this answers.
    /// </summary>
    [HttpPost("industry-refusals")]
    public async Task<IActionResult> RecordIndustryRefusal([FromBody] IndustryRefusalRequest? request, CancellationToken ct)
    {
        var result = await refusals.RecordAsync(request, OnboardingVisitor.From(HttpContext), ct);
        return result.IsSuccess
            ? Ok(new { recorded = result.Value!.Answers })
            : Refuse(result.Error, result.ErrorCode, "The answers could not be stored.");
    }

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

    /// <summary>A PayPal subscription to the tier's plan for this signup; the visitor approves it on the page this answers.</summary>
    [HttpPost("paypal/subscription")]
    public async Task<IActionResult> StartSubscription([FromBody] StartSignupPaymentRequest? request, CancellationToken ct)
    {
        var result = await payments.StartSubscriptionAsync(request, ct);
        return result.IsSuccess
            ? Ok(new { subscriptionId = result.Value!.Id, approvalUrl = result.Value.ApprovalUrl })
            : Refuse(result.Error, result.ErrorCode, "The subscription could not be started.");
    }

    /// <summary>Saving the visitor's PayPal account for overage charges; the visitor approves it on the page this answers.</summary>
    [HttpPost("paypal/vault")]
    public async Task<IActionResult> StartVault([FromBody] StartSignupPaymentRequest? request, CancellationToken ct)
    {
        var result = await payments.StartVaultAsync(request, ct);
        return result.IsSuccess
            ? Ok(new { setupTokenId = result.Value!.Id, approvalUrl = result.Value.ApprovalUrl })
            : Refuse(result.Error, result.ErrorCode, "The payment method could not be saved.");
    }

    /// <summary>The setup token the visitor approved, turned into the payment token activate-subscription takes.</summary>
    [HttpPost("paypal/vault/complete")]
    public async Task<IActionResult> CompleteVault([FromBody] CompleteSignupVaultRequest? request, CancellationToken ct)
    {
        var result = await payments.CompleteVaultAsync(request, ct);
        return result.IsSuccess ? Ok(result.Value) : Refuse(result.Error, result.ErrorCode, "The payment method could not be saved.");
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

    private ObjectResult Refuse(string? error, string? code, string fallback) =>
        StatusCode(StatusFor(code), new { error = string.IsNullOrWhiteSpace(error) ? fallback : error, code });

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the store being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        OnboardingFailureCodes.CountryNotSupported
            or OnboardingFailureCodes.BusinessDescriptionRequired
            or OnboardingFailureCodes.IndustryNotAnswered
            or OnboardingFailureCodes.ContactPhoneNotUs
            or OnboardingFailureCodes.ContactEmailInvalid
            or OnboardingFailureCodes.IndustryExcluded
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
        TelephonyFailureCodes.ProvisioningFailed => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status503ServiceUnavailable,
    };
}
