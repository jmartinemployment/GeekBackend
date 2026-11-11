using Microsoft.Extensions.DependencyInjection.Extensions;
using Twilio.Clients;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
using Twilio.Types;
using HttpMethod = Twilio.Http.HttpMethod;

namespace GeekAPI.Services.Receptionist;

/// <summary>Whether Geek-Receptionist is running in this GeekAPI instance, and if not, why.</summary>
public sealed record ReceptionistStatus(bool Enabled, string Reason);

/// <summary>
/// Geek-Receptionist inside GeekAPI: a missed-call text-back for a Twilio number. Twilio calls
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
        services.AddSingleton<IReceptionistCrm, CrmReceptionistRecorder>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    public static WebApplication MapReceptionist(this WebApplication app)
    {
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
        voice.MapPost("/incoming", async (HttpRequest req, MissedCallService missed, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var callSid = form["CallSid"].ToString();

            if (o.Mode == CallMode.Forwarded)
            {
                // The client's own line already rang and went unanswered before forwarding here.
                var outcome = await missed.HandleMissedCallAsync(callSid, form["From"], ct);
                return Twiml(VoicemailPrompt(o, outcome));
            }

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
        voice.MapPost("/dial-complete", async (HttpRequest req, MissedCallService missed, CancellationToken ct) =>
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

            var outcome = await missed.HandleMissedCallAsync(callSid, form["From"], ct);
            return Twiml(VoicemailPrompt(o, outcome));
        });

        // 5. Voicemail recorded: send the owner the recording link.
        voice.MapPost("/voicemail-complete", async (HttpRequest req, MissedCallService missed, IReceptionistCrm crm, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var url = form["RecordingUrl"].ToString();
            if (!string.IsNullOrEmpty(url) && form["RecordingDuration"].ToString() is var duration && duration != "0")
            {
                await missed.NotifyVoicemailAsync(form["From"], url, duration, ct);
                await crm.RecordVoicemailAsync(form["CallSid"].ToString(), form["From"].ToString(), url,
                    int.TryParse(duration, out var seconds) ? seconds : null, ct);
            }

            var response = new VoiceResponse();
            response.Say("Thank you. Goodbye.");
            response.Hangup();
            return Twiml(response);
        });

        // 6. A text arrives at the business number (usually a reply to the missed-call text).
        app.MapPost(RoutePrefix + "/sms/incoming", async (HttpRequest req, InboundSmsService inbound, CancellationToken ct) =>
        {
            await inbound.HandleAsync(await req.ReadFormAsync(ct), ct);
            // Empty response: no automatic reply. Twilio itself handles STOP/HELP keywords.
            return Results.Content(new MessagingResponse().ToString(), "application/xml");
        }).AddEndpointFilter<TwilioSignatureFilter>();

        return app;
    }

    private static VoiceResponse VoicemailPrompt(ReceptionistOptions o, MissedCallOutcome outcome)
    {
        var response = new VoiceResponse();
        response.Say(GreetingText(o.CompanyName, outcome));
        response.Record(
            action: Url(o, "/voice/voicemail-complete"),
            method: HttpMethod.Post,
            maxLength: 120,
            playBeep: true,
            finishOnKey: "#");
        return response;
    }

    // The texted wording is the verbal consent disclosure registered with the A2P 10DLC campaign.
    // Keep it identical to the campaign's Message flow text; change both together.
    internal static string GreetingText(string company, MissedCallOutcome outcome) => outcome == MissedCallOutcome.Texted
        ? $"Sorry we missed you. {company} is texting you at this number about your call. " +
          "Message and data rates may apply. Reply STOP to opt out. " +
          "You can also leave a message after the tone."
        : "Sorry we missed you. Please leave a message after the tone.";

    private static Uri Url(ReceptionistOptions o, string pathAndQuery) => new(o.PublicBaseUrl + RoutePrefix + pathAndQuery);

    private static IResult Twiml(VoiceResponse response) => Results.Content(response.ToString(), "application/xml");
}
