using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Calls;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;

namespace GeekAPI.Controllers.Voice;

/// <summary>
/// The two routes Twilio posts a tenant's number to (docs/Geek-Receptionist.md section 5); TwilioProvisioner sets both on the number
/// when it buys it. Twilio cannot send our API key, so ApiKeyMiddleware lets this prefix through and every post is authenticated by
/// X-Twilio-Signature made with the tenant's subaccount auth token, and must name that subaccount (<see cref="TenantTwilioWebhook"/>).
/// <list type="bullet">
/// <item><c>POST .../voice</c>: a call to the tenant's number. The tenant's standing is read on every call
/// (<see cref="TenantStanding.MayAnswer"/>). The voice AI is not built, so every call, whatever the standing, hears the closing message
/// the company line plays, with the tenant's business name, and is hung up: nothing is recorded, no voicemail is offered and nobody is
/// texted. Only a call the standing allows may ever be handed to an AI.</item>
/// <item><c>POST .../call-status</c>: Twilio's status callback once a call has ended, recorded for billing by
/// <see cref="TwilioCallStatusRecorder"/>. A recorded call answers 204.</item>
/// </list>
/// A store that is down answers 503, so Twilio plays its own error to the caller or posts the status again. No caller number is logged.
/// </summary>
[ApiController]
[Route(RoutePrefix + "/{tenantId:guid}")]
public sealed class TenantCallController : ControllerBase
{
    public const string RoutePrefix = "api/receptionist/tenant";

    private readonly TenantTwilioWebhook _webhook;
    private readonly TwilioCallStatusRecorder _recorder;
    private readonly IVoiceStore _store;
    private readonly IVoiceBillingStore _billing;
    private readonly TimeProvider _clock;
    private readonly ILogger<TenantCallController> _logger;

    public TenantCallController(
        TenantTwilioWebhook webhook,
        TwilioCallStatusRecorder recorder,
        IVoiceStore store,
        IVoiceBillingStore billing,
        TimeProvider clock,
        ILogger<TenantCallController> logger)
    {
        _webhook = webhook;
        _recorder = recorder;
        _store = store;
        _billing = billing;
        _clock = clock;
        _logger = logger;
    }

    [HttpPost("voice")]
    public async Task<IActionResult> Voice(Guid tenantId, CancellationToken ct)
    {
        if (!Request.HasFormContentType) return Refuse(StatusCodes.Status400BadRequest, "Twilio posts a form.", TenantCallFailureCodes.InvalidCall);
        var form = await ReadFormAsync(ct).ConfigureAwait(false);

        var verified = await _webhook.VerifyAsync(tenantId, Request.Path + Request.QueryString, Request.Headers[TenantTwilioWebhook.SignatureHeader], form, ct).ConfigureAwait(false);
        if (!verified.IsSuccess) return Refuse(StatusFor(verified.ErrorCode), verified.Error!, verified.ErrorCode!);

        var tenant = await _store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess) return Failed(tenantId, "its tenant", tenant.Error, tenant.ErrorCode);

        var standing = await _billing.GetStandingAsync(tenantId, ct).ConfigureAwait(false);
        if (!standing.IsSuccess) return Failed(tenantId, "its standing", standing.Error, standing.ErrorCode);

        form.TryGetValue("CallSid", out var callSid);
        if (!TenantStanding.MayAnswer(standing.Value!, _clock.GetUtcNow()))
        {
            _logger.LogInformation("Tenant {TenantId}: call {CallSid} is not answered, the tenant is {Status}; it hears the closing message.",
                tenantId, callSid, standing.Value!.TenantStatus);
            return Twiml(ReceptionistEndpoints.Unanswered(tenant.Value!.BusinessName));
        }

        _logger.LogInformation("Tenant {TenantId}: call {CallSid} may be answered, and no voice AI is built; it hears the closing message.", tenantId, callSid);
        return Twiml(ReceptionistEndpoints.Unanswered(tenant.Value!.BusinessName));
    }

    [HttpPost("call-status")]
    public async Task<IActionResult> CallStatus(Guid tenantId, CancellationToken ct)
    {
        if (!Request.HasFormContentType) return Refuse(StatusCodes.Status400BadRequest, "Twilio posts a form.", TenantCallFailureCodes.InvalidCall);
        var form = await ReadFormAsync(ct).ConfigureAwait(false);

        var verified = await _webhook.VerifyAsync(tenantId, Request.Path + Request.QueryString, Request.Headers[TenantTwilioWebhook.SignatureHeader], form, ct).ConfigureAwait(false);
        if (!verified.IsSuccess) return Refuse(StatusFor(verified.ErrorCode), verified.Error!, verified.ErrorCode!);

        var recorded = await _recorder.RecordAsync(tenantId, form, _clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (recorded.IsSuccess) return NoContent();

        var status = StatusFor(recorded.ErrorCode);
        _logger.LogError("A signed Twilio status for tenant {TenantId} was not recorded ({Status} {Code}): {Error}", tenantId, status, recorded.ErrorCode, recorded.Error);
        return Refuse(status, recorded.Error!, recorded.ErrorCode!);
    }

    /// <summary>The HTTP status a failure code is answered with. Anything unlisted is the store or the key being unavailable.</summary>
    internal static int StatusFor(string? code) => code switch
    {
        TenantCallFailureCodes.InvalidCall or VoiceFailureCodes.InvalidRequest => StatusCodes.Status400BadRequest,
        TenantCallFailureCodes.SignatureInvalid or TenantCallFailureCodes.AccountMismatch => StatusCodes.Status403Forbidden,
        VoiceFailureCodes.NotFound => StatusCodes.Status404NotFound,
        VoiceFailureCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable,
    };

    private async Task<IReadOnlyDictionary<string, string>> ReadFormAsync(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct).ConfigureAwait(false);
        return form.ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal);
    }

    private ObjectResult Failed(Guid tenantId, string what, string? error, string? code)
    {
        var status = StatusFor(code);
        _logger.LogError("A signed call for tenant {TenantId} could not be answered: {What} could not be read ({Status} {Code}).", tenantId, what, status, code);
        return Refuse(status, error ?? "The call could not be answered.", code ?? VoiceFailureCodes.StoreUnavailable);
    }

    private ObjectResult Refuse(int status, string error, string code) => StatusCode(status, new { error, code });

    private static ContentResult Twiml(VoiceResponse response) => new() { Content = response.ToString(), ContentType = "application/xml", StatusCode = StatusCodes.Status200OK };
}
