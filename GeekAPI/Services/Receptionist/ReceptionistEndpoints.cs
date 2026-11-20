using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Models.Voice;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Twilio.Security;
using Twilio.Clients;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Twilio.Types;
using HttpMethod = Twilio.Http.HttpMethod;

namespace GeekAPI.Services.Receptionist;

/// <summary>Whether Geek-Receptionist is running in this GeekAPI instance, and if not, why.</summary>
public sealed record ReceptionistStatus(bool Enabled, string Reason);

/// <summary>
/// Geek-Receptionist inside GeekAPI: rings the owner from the Twilio number; a call nobody answers is asked for keypad consent to the
/// AI (<see cref="ReceptionistConsent"/>), and a caller who presses 1 is handed to ConversationRelay when the AI is configured; any
/// other answer hears a closing message and ends, with nothing recorded, nobody texted and nothing written anywhere. Twilio calls
/// these webhooks directly, so they sit outside ApiKeyMiddleware (see its public prefixes) and
/// are authenticated by X-Twilio-Signature instead (<see cref="TwilioSignatureFilter"/>).
/// </summary>
public static class ReceptionistEndpoints
{
    /// <summary>Every webhook lives under this prefix. Twilio's number points at it.</summary>
    public const string RoutePrefix = "/api/receptionist/twilio";

    public static IServiceCollection AddReceptionist(this IServiceCollection services, IConfiguration config)
    {
        if (!ReceptionistOptions.IsRequested(config))
        {
            services.AddSingleton(new ReceptionistStatus(false, "TWILIO_ACCOUNT_SID is not set"));
            return services;
        }

        ReceptionistOptions options;
        try
        {
            options = ReceptionistOptions.FromConfiguration(config);
        }
        catch (InvalidOperationException ex)
        {
            // GeekAPI serves far more than the receptionist; a Twilio typo must not take it down.
            services.AddSingleton(new ReceptionistStatus(false, ex.Message));
            return services;
        }

        services.AddSingleton(new ReceptionistStatus(true, "configured"));
        services.AddSingleton(options);
        services.AddSingleton<ITwilioRestClient>(_ =>
            // Outbound REST calls authenticate with the API key. The auth token only verifies webhooks.
            new TwilioRestClient(options.TwilioApiKeySid, options.TwilioApiKeySecret, accountSid: options.TwilioAccountSid));
        services.AddSingleton<ISmsSender, TwilioSmsSender>();
        services.AddSingleton<MissedCallService>();
        services.AddSingleton<IMediaDeleter, TwilioMediaDeleter>();
        services.AddSingleton<InboundSmsService>();
        services.AddSingleton<TwilioSignatureFilter>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    public static WebApplication MapReceptionist(this WebApplication app)
    {
        app.LogReceptionistModel();
        var status = app.Services.GetRequiredService<ReceptionistStatus>();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GeekAPI.Receptionist");

        if (!status.Enabled)
        {
            if (status.Reason.StartsWith("TWILIO_ACCOUNT_SID", StringComparison.Ordinal))
                log.LogInformation("Geek-Receptionist is off: {Reason}", status.Reason);
            else
                log.LogError("Geek-Receptionist is off because its configuration is invalid. {Reason}", status.Reason);

            // Twilio gets a clear 503 (and retries / falls back) rather than a 404 that looks like a typo.
            app.Map(RoutePrefix + "/{**rest}", () => Results.Problem(
                title: "receptionist_not_configured",
                statusCode: StatusCodes.Status503ServiceUnavailable));
            return app;
        }

        var o = app.Services.GetRequiredService<ReceptionistOptions>();
        log.LogInformation(
            "Geek-Receptionist on for {Company}: mode {Mode}, ring {Ring}s, screening {Screen}, signature check {Sig}",
            o.CompanyName, o.Mode, o.RingSeconds, o.ScreenCalls, o.ValidateTwilioSignature);

        var voice = app.MapGroup(RoutePrefix + "/voice").AddEndpointFilter<TwilioSignatureFilter>();

        // 1. A call arrives at the Twilio number.
        voice.MapPost("/incoming", async (HttpRequest req, MissedCallService missed, IReceptionistModel model, RelayTickets tickets, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var callSid = form["CallSid"].ToString();

            if (o.Mode == CallMode.Forwarded)
                // The client's own line already rang and went unanswered before forwarding here: the caller meets the consent gate.
                return Twiml(CompanyConsent(o, model, tickets, log, callSid));

            var response = new VoiceResponse();
            var dial = new Dial(
                action: Url(o, "/voice/dial-complete"),
                method: HttpMethod.Post,
                timeout: o.RingSeconds);
            dial.Number(
                phoneNumber: new PhoneNumber(o.OwnerCell),
                url: o.ScreenCalls ? Url(o, $"/voice/screen?parent={Uri.EscapeDataString(callSid)}") : null,
                method: o.ScreenCalls ? HttpMethod.Post : null);
            response.Append(dial);
            return Twiml(response);
        });

        // 2. (Screening) Played to the owner when they pick up, before the caller is connected.
        voice.MapPost("/screen", (string parent) =>
        {
            var response = new VoiceResponse();
            var gather = new Gather(
                numDigits: 1,
                timeout: 6,
                action: Url(o, $"/voice/screen-accepted?parent={Uri.EscapeDataString(parent)}"),
                method: HttpMethod.Post);
            gather.Say($"Call for {o.CompanyName}. Press any key to accept.");
            response.Append(gather);
            // No key pressed (e.g. the owner's voicemail answered): drop this leg so the call counts as missed.
            response.Hangup();
            return Twiml(response);
        });

        // 3. (Screening) Owner pressed a key: mark accepted; empty TwiML connects the two legs.
        voice.MapPost("/screen-accepted", (string parent, MissedCallService missed) =>
        {
            missed.MarkAccepted(parent);
            return Twiml(new VoiceResponse());
        });

        // 4. The <Dial> to the owner finished. Decide whether the call was missed.
        voice.MapPost("/dial-complete", async (HttpRequest req, MissedCallService missed, IReceptionistModel model, RelayTickets tickets, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var callSid = form["CallSid"].ToString();
            var dialStatus = form["DialCallStatus"].ToString();

            var answered = o.ScreenCalls
                ? missed.ConsumeAccepted(callSid)
                : dialStatus is "completed" or "answered";

            log.LogInformation("Call {CallSid} dial status {Status}; answered by owner: {Answered}", callSid, dialStatus, answered);

            if (answered)
            {
                var done = new VoiceResponse();
                done.Hangup();
                return Twiml(done);
            }

            return Twiml(CompanyConsent(o, model, tickets, log, callSid));
        });

        // 4a. The caller pressed a key at the consent step, in strict keypress mode. Only a 1, with the AI configured, is handed to
        // ConversationRelay; a 2, any other key, or an AI that is not configured hears the closing message. The key is not recorded.
        voice.MapPost("/consent-result", async (HttpRequest req, IReceptionistModel model, RelayTickets tickets, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var callSid = form["CallSid"].ToString();
            if (!ReceptionistConsent.Consented(form["Digits"].ToString()))
            {
                log.LogInformation("Call {CallSid} did not consent to the AI; it hears the closing message.", callSid);
                return Twiml(Unanswered(o.CompanyName));
            }
            if (!model.IsConfigured)
            {
                log.LogWarning("Call {CallSid} consented, and the AI is not configured; it hears the closing message.", callSid);
                return Twiml(Unanswered(o.CompanyName));
            }

            var config = ReceptionistConsent.CompanyConfig(o.CompanyName);
            var prompt = config.IsSuccess ? ReceptionistConsent.SystemPrompt(config.Value!) : null;
            if (prompt is null || !prompt.IsSuccess)
            {
                log.LogError("Call {CallSid} consented, and the company line's prompt could not be built; it hears the closing message.", callSid);
                return Twiml(Unanswered(o.CompanyName));
            }

            var ticket = tickets.Issue(RelayTickets.CompanyLine, prompt.Value!);
            log.LogInformation("Call {CallSid} consented; it is handed to the AI.", callSid);
            return Twiml(ReceptionistConsent.Connect(ReceptionistConsent.RelayUrl(o.PublicBaseUrl, RelayPath, ticket), config.Value!.GreetingMessage));
        });

        // 4b. ConversationRelay's WebSocket for the company line. Opened only with a ticket from a signed consent post, and, unless the
        // company line's signature check is off, with an X-Twilio-Signature over the wss:// address made with the account's auth token.
        app.Map(RelayPath, async (HttpContext context, IReceptionistModel model, RelayTickets tickets, CancellationToken ct) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            if (!model.IsConfigured)
            {
                log.LogWarning("A relay connection to the company line was refused: the AI is not configured.");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            if (o.ValidateTwilioSignature && !ValidHandshake(context.Request, o.PublicBaseUrl, o.TwilioAuthToken))
            {
                log.LogWarning("A relay connection to the company line was refused: missing or invalid Twilio signature.");
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            var ticket = tickets.Redeem(context.Request.Query["ticket"].ToString(), RelayTickets.CompanyLine);
            if (ticket is null)
            {
                log.LogWarning("A relay connection to the company line was refused: its ticket is unknown, used or expired.");
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await new ReceptionistRelaySession(model, ticket.SystemPrompt, log).RunAsync(socket, ct);
        });

        // 5. A text arrives at the business number.
        app.MapPost(RoutePrefix + "/sms/incoming", async (HttpRequest req, InboundSmsService inbound, CancellationToken ct) =>
        {
            await inbound.HandleAsync(await req.ReadFormAsync(ct), ct);
            // Empty response: no automatic reply. Twilio itself handles STOP/HELP keywords.
            return Results.Content(new MessagingResponse().ToString(), "application/xml");
        }).AddEndpointFilter<TwilioSignatureFilter>();

        return app;
    }

    // No <Record>: the company keeps no call content (Zero Data Retention), so a missed caller is told
    // to call again rather than offered voicemail. A tenant's call route plays the same message with the tenant's business name.
    internal static VoiceResponse Unanswered(string company)
    {
        var response = new VoiceResponse();
        response.Say(UnansweredText(company));
        response.Hangup();
        return response;
    }

    internal static string UnansweredText(string company) =>
        $"Thank you for calling {company}. We can't take your call right now. Please call again later. Goodbye.";

    /// <summary>ConversationRelay's WebSocket for the company line.</summary>
    public const string RelayPath = RoutePrefix + "/relay";

    /// <summary>
    /// Twilio's docs say ConversationRelay sends X-Twilio-Signature on the WebSocket handshake and do not say what it signs. Checked
    /// as Twilio's webhook signatures are: the full address Twilio requested (the wss:// URL given in the TwiML, with its query) and no
    /// parameters, since a handshake has no form.
    /// </summary>
    internal static bool ValidHandshake(HttpRequest request, string publicBaseUrl, string authToken)
    {
        var signature = request.Headers["X-Twilio-Signature"].ToString();
        if (string.IsNullOrEmpty(signature)) return false;
        return new RequestValidator(authToken).Validate(HandshakeUrl(request, publicBaseUrl), new Dictionary<string, string>(), signature);
    }

    /// <summary>
    /// The company line's consent gate, in whichever mode its settings name. Passive disclosure, the plan's default, speaks the
    /// disclosure as ConversationRelay connects and asks for no key; strict keypress asks as before. Anything that cannot be built is
    /// the closing message, never a call answered wrongly.
    /// </summary>
    private static VoiceResponse CompanyConsent(
        ReceptionistOptions o, IReceptionistModel model, RelayTickets tickets, ILogger log, string callSid)
    {
        var config = ReceptionistConsent.CompanyConfig(o.CompanyName);
        if (!config.IsSuccess)
        {
            log.LogError("Call {CallSid} could not be answered, the company line's settings could not be built; it hears the closing message.", callSid);
            return Unanswered(o.CompanyName);
        }

        if (config.Value!.ConsentMode == VoiceConsentModes.StrictKeypress)
            return ReceptionistConsent.Ask(o.CompanyName, Url(o, "/voice/consent-result"));

        if (!model.IsConfigured)
        {
            log.LogWarning("Call {CallSid} reached the AI step, and the AI is not configured; it hears the closing message.", callSid);
            return Unanswered(o.CompanyName);
        }

        var disclosure = ReceptionistConsent.DisclosureText(o.CompanyName);
        var prompt = ReceptionistConsent.SystemPrompt(config.Value, disclosure);
        if (!prompt.IsSuccess)
        {
            log.LogError("Call {CallSid} could not be answered, the company line's prompt could not be built; it hears the closing message.", callSid);
            return Unanswered(o.CompanyName);
        }

        var ticket = tickets.Issue(RelayTickets.CompanyLine, prompt.Value!);
        log.LogInformation("Call {CallSid} hears the disclosure and is handed to the AI.", callSid);
        return ReceptionistConsent.Connect(ReceptionistConsent.RelayUrl(o.PublicBaseUrl, RelayPath, ticket), disclosure);
    }

    internal static string HandshakeUrl(HttpRequest request, string publicBaseUrl) =>
        $"wss://{new Uri(publicBaseUrl).Authority}{request.Path}{request.QueryString}";

    private static Uri Url(ReceptionistOptions o, string pathAndQuery) => new(o.PublicBaseUrl + RoutePrefix + pathAndQuery);

    private static IResult Twiml(VoiceResponse response) => Results.Content(response.ToString(), "application/xml");
}
