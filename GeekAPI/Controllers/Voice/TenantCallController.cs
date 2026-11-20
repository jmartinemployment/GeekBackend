using GeekAPI.Services.Receptionist;
using GeekAPI.Services.GeekCrawler;
using GeekAPI.Services.Receptionist.Knowledge;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Billing;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Mcp;
using GeekAPI.Services.Voice.Storage;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Receptionist;
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
/// (<see cref="TenantStanding.MayAnswer"/>). A call the standing does not allow hears the closing message the company line plays, with
/// the tenant's business name, and is hung up. A call it allows is asked for keypad consent (<see cref="ReceptionistConsent"/>).</item>
/// <item><c>POST .../consent-result</c>: the key the caller pressed. A 1, with the AI configured and the standing still allowing it, is
/// handed to ConversationRelay; anything else hears the closing message. Nothing is recorded, no voicemail is offered, nobody is texted.</item>
/// <item><c>GET .../relay</c>: ConversationRelay's WebSocket, opened with the one-time ticket the consent post issued
/// (<see cref="RelayTickets"/>) and a handshake signed with the subaccount's auth token.</item>
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
    private readonly IReceptionistModel _model;
    private readonly RelayTickets _tickets;
    private readonly TwilioOptions _twilio;
    private readonly CalendarToolCatalog _catalog;
    private readonly CallOutcomes _outcomes;
    private readonly ILogger<TenantCallController> _logger;

    public TenantCallController(
        TenantTwilioWebhook webhook,
        TwilioCallStatusRecorder recorder,
        IVoiceStore store,
        IVoiceBillingStore billing,
        TimeProvider clock,
        IReceptionistModel model,
        RelayTickets tickets,
        TwilioOptions twilio,
        CalendarToolCatalog catalog,
        CallOutcomes outcomes,
        ILogger<TenantCallController> logger)
    {
        _model = model;
        _tickets = tickets;
        _twilio = twilio;
        _catalog = catalog;
        _outcomes = outcomes;
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

        var settings = await _store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!settings.IsSuccess) return Failed(tenantId, "its receptionist settings", settings.Error, settings.ErrorCode);

        if (settings.Value!.ConsentMode == VoiceConsentModes.StrictKeypress)
        {
            _logger.LogInformation("Tenant {TenantId}: call {CallSid} may be answered; it is asked for consent.", tenantId, callSid);
            return Twiml(ReceptionistConsent.Ask(tenant.Value!.BusinessName, new Uri(_twilio.PublicBaseUrl + ConsentResultPath(tenantId))));
        }

        // Passive disclosure, the plan's default: the caller is told as the call connects and is not asked to press anything.
        _logger.LogInformation("Tenant {TenantId}: call {CallSid} may be answered; it hears the disclosure and is handed to the AI.", tenantId, callSid);
        return HandToAi(tenantId, tenant.Value!, settings.Value, callSid, ReceptionistConsent.DisclosureText(tenant.Value!.BusinessName), form);
    }

    [HttpPost("consent-result")]
    public async Task<IActionResult> ConsentResult(Guid tenantId, CancellationToken ct)
    {
        if (!Request.HasFormContentType) return Refuse(StatusCodes.Status400BadRequest, "Twilio posts a form.", TenantCallFailureCodes.InvalidCall);
        var form = await ReadFormAsync(ct).ConfigureAwait(false);

        var verified = await _webhook.VerifyAsync(tenantId, Request.Path + Request.QueryString, Request.Headers[TenantTwilioWebhook.SignatureHeader], form, ct).ConfigureAwait(false);
        if (!verified.IsSuccess) return Refuse(StatusFor(verified.ErrorCode), verified.Error!, verified.ErrorCode!);

        var tenant = await _store.GetTenantAsync(tenantId, ct).ConfigureAwait(false);
        if (!tenant.IsSuccess) return Failed(tenantId, "its tenant", tenant.Error, tenant.ErrorCode);
        var closing = Twiml(ReceptionistEndpoints.Unanswered(tenant.Value!.BusinessName));

        form.TryGetValue("CallSid", out var callSid);
        form.TryGetValue("Digits", out var digits);
        if (!ReceptionistConsent.Consented(digits))
        {
            _logger.LogInformation("Tenant {TenantId}: call {CallSid} did not consent to the AI; it hears the closing message.", tenantId, callSid);
            return closing;
        }
        if (!_model.IsConfigured)
        {
            _logger.LogWarning("Tenant {TenantId}: call {CallSid} consented, and the AI is not configured; it hears the closing message.", tenantId, callSid);
            return closing;
        }

        var standing = await _billing.GetStandingAsync(tenantId, ct).ConfigureAwait(false);
        if (!standing.IsSuccess) return Failed(tenantId, "its standing", standing.Error, standing.ErrorCode);
        if (!TenantStanding.MayAnswer(standing.Value!, _clock.GetUtcNow()))
        {
            _logger.LogInformation("Tenant {TenantId}: call {CallSid} consented, and the tenant is {Status}; it hears the closing message.",
                tenantId, callSid, standing.Value!.TenantStatus);
            return closing;
        }

        var config = await _store.GetConfigAsync(tenantId, ct).ConfigureAwait(false);
        if (!config.IsSuccess) return Failed(tenantId, "its receptionist settings", config.Error, config.ErrorCode);

        _logger.LogInformation("Tenant {TenantId}: call {CallSid} consented; it is handed to the AI.", tenantId, callSid);
        return HandToAi(tenantId, tenant.Value!, config.Value!, callSid, config.Value!.GreetingMessage, form);
    }

    /// <summary>
    /// Hands the call to ConversationRelay, speaking <paramref name="spokenGreeting"/> as it connects: the plan's disclosure in passive
    /// mode, the business's own greeting after a keypress in strict mode. The prompt is told which of the two the caller actually heard,
    /// so the AI does not greet a second time. Settings that cannot be used are the closing message, never a call answered wrongly.
    /// </summary>
    private IActionResult HandToAi(
        Guid tenantId, VoiceTenant tenant, VoiceReceptionistConfig config, string? callSid, string spokenGreeting,
        IReadOnlyDictionary<string, string> form)
    {
        var prompt = ReceptionistConsent.SystemPrompt(config, spokenGreeting);
        if (!prompt.IsSuccess)
        {
            _logger.LogError("Tenant {TenantId}: call {CallSid} cannot be answered, its receptionist settings cannot be used ({Error}); it hears the closing message.",
                tenantId, callSid, prompt.Error);
            return Twiml(ReceptionistEndpoints.Unanswered(tenant.BusinessName));
        }

        form.TryGetValue("From", out var callerNumber);

        // Where a record of this call would go, and whether every call gets one. A business that asked for messages names its own
        // address and gets all of them. A business that only books gets one address anyway, its own contact email, used for nothing
        // unless a booking fails: "Failed bookings route automatically as messages".
        var deliverAlways = config.MessageEmailEnabled && !string.IsNullOrWhiteSpace(config.MessageDestinationEmail);
        var destination = deliverAlways ? config.MessageDestinationEmail : tenant.ContactEmail;
        var request = deliverAlways || (config.BookingEnabled && !string.IsNullOrWhiteSpace(tenant.ContactEmail))
            ? new CallSummaryRequest(
                destination!, tenant.BusinessName, callerNumber, config.IncludeTranscript, config.NotifyOnAbandonedCalls)
            : null;
        // A business with somebody to hand a failing call to hears the plan's transfer line rather than the take-a-message one.
        var transferConfigured = config.TransferMode != VoiceTransferModes.VoicemailOnly && !string.IsNullOrWhiteSpace(config.TransferTargetNumber);
        var ticket = _tickets.Issue(tenantId.ToString(), prompt.Value!, request, config.BookingEnabled, transferConfigured, deliverAlways, config.KnowledgeRunId);
        return Twiml(ReceptionistConsent.Connect(ReceptionistConsent.RelayUrl(_twilio.PublicBaseUrl!, RelayPath(tenantId), ticket), spokenGreeting));
    }

    [HttpGet("relay")]
    public async Task<IActionResult> Relay(Guid tenantId, CancellationToken ct)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return Refuse(StatusCodes.Status400BadRequest, "ConversationRelay opens a WebSocket.", TenantCallFailureCodes.InvalidCall);
        if (!_model.IsConfigured)
        {
            _logger.LogWarning("A relay connection for tenant {TenantId} was refused: the AI is not configured.", tenantId);
            return Refuse(StatusCodes.Status503ServiceUnavailable, "The AI is not configured.", RelayNotConfigured);
        }
        if (string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl))
        {
            _logger.LogError("A relay connection for tenant {TenantId} was refused: PUBLIC_BASE_URL is not set, so its signature cannot be checked.", tenantId);
            return Refuse(StatusCodes.Status503ServiceUnavailable, "Twilio connections cannot be checked.", TelephonyFailureCodes.TelephonyNotConfigured);
        }

        var url = ReceptionistEndpoints.HandshakeUrl(Request, _twilio.PublicBaseUrl);
        var verified = await _webhook.VerifyHandshakeAsync(tenantId, url, Request.Headers[TenantTwilioWebhook.SignatureHeader], ct).ConfigureAwait(false);
        if (!verified.IsSuccess) return Refuse(StatusFor(verified.ErrorCode), verified.Error!, verified.ErrorCode!);

        var ticket = _tickets.Redeem(Request.Query["ticket"].ToString(), tenantId.ToString());
        if (ticket is null)
        {
            _logger.LogWarning("A relay connection for tenant {TenantId} was refused: its ticket is unknown, used or expired.", tenantId);
            return Refuse(StatusCodes.Status403Forbidden, "The connection has no valid ticket.", TenantCallFailureCodes.SignatureInvalid);
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        CallSummaryPlan? plan = null;
        if (ticket.Summary is { } request)
        {
            var delivery = HttpContext.RequestServices.GetService<ICallSummaryDelivery>();
            if (delivery is null)
                _logger.LogError("Tenant {TenantId} asked for an end-of-call record and nothing is registered to deliver it; this call will send none.", tenantId);
            else
                plan = new CallSummaryPlan(delivery, request, ticket.DeliverSummaryAlways)
                {
                    // A business that connected its own bucket or Drive folder has its records written there, and only a failed write
                    // falls back to email.
                    Storage = await HttpContext.RequestServices.GetRequiredService<CallRecordStorages>().ForAsync(tenantId, ct).ConfigureAwait(false),
                };
        }

        var calendar = await CalendarToolbox.CreateAsync(
            tenantId, _store, HttpContext.RequestServices.GetRequiredService<CalendarTools>(), _catalog,
            HttpContext.RequestServices.GetRequiredService<McpGatewayClient>(), _logger, ticket.BookingEnabled, ct).ConfigureAwait(false);

        // What the business has published about itself, searched through the Library that already holds it. A business with no indexed
        // site gets no lookup tool, which is the ordinary case before its signup crawl has run.
        var knowledge = KnowledgeToolbox.Create(
            tenantId, ticket.KnowledgeRunId, HttpContext.RequestServices.GetRequiredService<IGeekCrawlerRagClient>(), _logger);

        var toolbox = CompositeToolbox.Of(calendar, knowledge);

        await new ReceptionistRelaySession(_model, ticket.SystemPrompt, _logger) { Summary = plan, Toolbox = toolbox, TransferConfigured = ticket.TransferConfigured, Outcomes = _outcomes, TenantId = tenantId }
            .RunAsync(socket, ct).ConfigureAwait(false);
        return new EmptyResult();
    }

    /// <summary>Refused because no model is configured for the receptionist.</summary>
    public const string RelayNotConfigured = "receptionist_ai_not_configured";

    public static string ConsentResultPath(Guid tenantId) => $"/{RoutePrefix}/{tenantId}/consent-result";

    public static string RelayPath(Guid tenantId) => $"/{RoutePrefix}/{tenantId}/relay";

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
