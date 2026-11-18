using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Interfaces.Voice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Receptionist;

/// <summary>A model that answers from a script, or fails, and keeps what it was given so a test can see it.</summary>
public sealed class FakeReceptionistModel(bool configured = true) : IReceptionistModel
{
    public bool IsConfigured => configured;
    public bool Fail { get; init; }
    public string Reply { get; init; } = "How can I help?";
    public List<(string Prompt, List<ReceptionistTurn> Turns)> Calls { get; } = new();

    public Task<string> ReplyAsync(string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default)
    {
        Calls.Add((systemPrompt, turns.ToList()));
        if (Fail) throw new ReceptionistModelException("anthropic answered 500.");
        return Task.FromResult(Reply);
    }
}

/// <summary>A logger that keeps every formatted line, so a test can prove what a caller said never reaches one.</summary>
public sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(logLevel + " " + formatter(state, exception));
}

/// <summary>
/// The keypad consent step, word for word, and what each key does on the company line; the relay's ticket and signature; the
/// receptionist's model settings with no default; and the relay's handling of Twilio's documented messages with nothing kept.
/// </summary>
public sealed class ConsentAndRelayTests
{
    private const string Consent =
        "Thank you for calling Dag Plumbing. You will be interacting with our AI receptionist. Your audio will be temporarily recorded and processed to provide this service, but we do not retain call recordings or transcripts. To consent and continue, press 1. To decline, press 2.";

    private static Dictionary<string, string> Pressed(string? digits)
    {
        var form = new Dictionary<string, string> { ["CallSid"] = "CA1", ["From"] = Settings.Caller, ["To"] = Settings.TwilioNumber };
        if (digits is not null) form["Digits"] = digits;
        return form;
    }

    // ----- Consent TwiML -----

    [Fact]
    public async Task The_consent_step_says_the_approved_wording_and_gathers_one_key_and_no_speech()
    {
        using var app = new App(new() { ["CALL_MODE"] = "Forwarded" });
        var (status, xml) = await app.PostTwilio("/voice/incoming", Pressed(null));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains(Consent, xml);
        Assert.Matches("<Gather[^>]*input=\"dtmf\"", xml);
        Assert.Matches("<Gather[^>]*numDigits=\"1\"", xml);
        Assert.Contains($"action=\"{Settings.Base}/api/receptionist/twilio/voice/consent-result\"", xml);
        Assert.DoesNotContain("speech", xml);
        Assert.DoesNotContain("<Record", xml);
        // No key: the closing message after the Gather, then a hang-up.
        Assert.True(xml.IndexOf("</Gather>", StringComparison.Ordinal) < xml.IndexOf("We can", StringComparison.Ordinal));
        Assert.Contains("<Hangup", xml);
    }

    [Theory]
    [InlineData("2")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9")]
    [InlineData("*")]
    public async Task Anything_but_1_hears_the_closing_message_and_is_never_connected(string? digits)
    {
        var model = new FakeReceptionistModel();
        using var app = new App(model: model);
        var (status, xml) = await app.PostTwilio("/voice/consent-result", Pressed(digits));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("We can", xml);
        Assert.Contains("<Hangup", xml);
        Assert.DoesNotContain("ConversationRelay", xml);
        Assert.Empty(model.Calls);
    }

    [Fact]
    public async Task Pressing_1_with_no_model_configured_hears_the_closing_message()
    {
        using var app = new App();
        var (_, xml) = await app.PostTwilio("/voice/consent-result", Pressed("1"));

        Assert.Contains("We can", xml);
        Assert.DoesNotContain("ConversationRelay", xml);
    }

    [Fact]
    public async Task Pressing_1_with_a_model_connects_conversation_relay_on_a_wss_address_with_a_ticket_and_no_transcript_service()
    {
        using var app = new App(model: new FakeReceptionistModel());
        var (_, xml) = await app.PostTwilio("/voice/consent-result", Pressed("1"));

        Assert.Contains("<Connect>", xml);
        Assert.Matches("<ConversationRelay[^>]*url=\"wss://receptionist\\.example\\.com/api/receptionist/twilio/relay\\?ticket=[0-9A-F]{64}\"", xml);
        Assert.Contains("welcomeGreeting=\"Thank you for calling Dag Plumbing. I&apos;m the AI receptionist", xml.Replace("'", "&apos;"));
        Assert.DoesNotContain("intelligenceService", xml);
        Assert.DoesNotContain("We can", xml);
    }

    [Fact]
    public async Task An_unsigned_consent_post_is_refused()
    {
        using var app = new App(model: new FakeReceptionistModel());
        var (status, _) = await app.PostTwilio("/voice/consent-result", Pressed("1"), sign: false);
        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    // ----- The company line's relay WebSocket -----

    private static string TicketUrl(string xml) =>
        Regex.Match(xml, "url=\"(wss://[^\"]+)\"").Groups[1].Value.Replace("&amp;", "&");

    private static async Task<WebSocket> Open(App app, string wssUrl, string? signature)
    {
        var client = app.Server.CreateWebSocketClient();
        if (signature is not null) client.ConfigureRequest = r => r.Headers["X-Twilio-Signature"] = signature;
        var http = new Uri(wssUrl.Replace("wss://", "ws://"));
        return await client.ConnectAsync(http, CancellationToken.None);
    }

    private static async Task<string> Receive(WebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static Task Send(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task A_signed_relay_with_its_ticket_answers_a_final_prompt_with_the_models_reply()
    {
        var model = new FakeReceptionistModel { Reply = "Which area would you like help with?" };
        using var app = new App(model: model);
        var (_, xml) = await app.PostTwilio("/voice/consent-result", Pressed("1"));
        var url = TicketUrl(xml);

        using var socket = await Open(app, url, App.SignUrl(url));
        await Send(socket, """{"type":"setup","sessionId":"VX1","callSid":"CA1","from":"+15615551234","to":"+15615550199"}""");
        await Send(socket, """{"type":"prompt","voicePrompt":"I need help with payroll","lang":"en-US","last":true}""");

        using var reply = JsonDocument.Parse(await Receive(socket));
        Assert.Equal("text", reply.RootElement.GetProperty("type").GetString());
        Assert.Equal("Which area would you like help with?", reply.RootElement.GetProperty("token").GetString());
        Assert.True(reply.RootElement.GetProperty("last").GetBoolean());

        var call = Assert.Single(model.Calls);
        Assert.Contains("Qualifying questions", call.Prompt);
        Assert.Contains(GeekAPI.Services.Voice.Onboarding.CompanyCallerQuestions.OpeningQuestion, call.Prompt);
        Assert.Equal("I need help with payroll", Assert.Single(call.Turns).Text);
    }

    [Fact]
    public async Task A_ticket_opens_one_relay_only()
    {
        using var app = new App(model: new FakeReceptionistModel());
        var (_, xml) = await app.PostTwilio("/voice/consent-result", Pressed("1"));
        var url = TicketUrl(xml);

        using (await Open(app, url, App.SignUrl(url))) { }
        await Assert.ThrowsAnyAsync<Exception>(() => Open(app, url, App.SignUrl(url)));
    }

    [Fact]
    public async Task A_relay_without_a_valid_signature_or_ticket_is_refused()
    {
        using var app = new App(model: new FakeReceptionistModel());
        var (_, xml) = await app.PostTwilio("/voice/consent-result", Pressed("1"));
        var url = TicketUrl(xml);

        await Assert.ThrowsAnyAsync<Exception>(() => Open(app, url, null));
        await Assert.ThrowsAnyAsync<Exception>(() => Open(app, url, "bm90LWEtcmVhbC1zaWduYXR1cmU="));
        var forged = "wss://receptionist.example.com/api/receptionist/twilio/relay?ticket=00";
        await Assert.ThrowsAnyAsync<Exception>(() => Open(app, forged, App.SignUrl(forged)));
    }

    [Fact]
    public async Task With_no_model_the_relay_refuses_every_connection()
    {
        using var app = new App();
        var url = "wss://receptionist.example.com/api/receptionist/twilio/relay?ticket=00";
        await Assert.ThrowsAnyAsync<Exception>(() => Open(app, url, App.SignUrl(url)));
    }

    // ----- Tickets -----

    [Fact]
    public void A_ticket_is_for_its_own_line_and_expires()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        var tickets = new RelayTickets(clock);

        Assert.Null(tickets.Redeem(tickets.Issue("tenant-a", "p"), "tenant-b"));

        var late = tickets.Issue("tenant-a", "p");
        clock.Advance(RelayTickets.Lifetime);
        Assert.Null(tickets.Redeem(late, "tenant-a"));

        var ok = tickets.Issue("tenant-a", "p");
        Assert.Equal("p", tickets.Redeem(ok, "tenant-a")!.SystemPrompt);
        Assert.Null(tickets.Redeem(ok, "tenant-a"));
    }

    // ----- Model settings -----

    private static ReceptionistModelSettings Settings_(Dictionary<string, string> env) => ReceptionistModelSettings.From(k => env.GetValueOrDefault(k));

    [Fact]
    public void With_no_provider_set_the_model_is_not_configured_and_nothing_is_chosen()
    {
        var settings = Settings_(new());
        Assert.False(settings.IsConfigured);
        Assert.Null(settings.Model);
        Assert.Contains("LlmProviders__DefaultProvider", settings.Reason);
    }

    [Theory]
    [InlineData("Mistral")]
    [InlineData("gemini")]
    [InlineData("perplexity")]
    public void A_provider_the_receptionist_cannot_call_is_not_configured(string provider)
    {
        var settings = Settings_(new() { ["LlmProviders__DefaultProvider"] = provider, ["LlmProviders__Gemini__ApiKey"] = "k", ["LlmProviders__Gemini__Model"] = "m" });
        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void A_provider_without_its_key_or_model_is_not_configured_and_the_setting_is_named()
    {
        var noKey = Settings_(new() { ["LlmProviders__DefaultProvider"] = "OpenAi", ["LlmProviders__OpenAi__Model"] = "m" });
        Assert.False(noKey.IsConfigured);
        Assert.Contains("LlmProviders__OpenAi__ApiKey", noKey.Reason);

        var noModel = Settings_(new() { ["LlmProviders__DefaultProvider"] = "Anthropic", ["LlmProviders__Anthropic__ApiKey"] = "k" });
        Assert.False(noModel.IsConfigured);
        Assert.Contains("LlmProviders__Anthropic__Model", noModel.Reason);
    }

    [Theory]
    [InlineData("Anthropic", "Anthropic", "anthropic")]
    [InlineData("openai", "OpenAi", "openai")]
    [InlineData("Groq", "Groq", "groq")]
    public void The_default_provider_with_its_key_and_model_is_configured(string value, string section, string provider)
    {
        var settings = Settings_(new()
        {
            ["LlmProviders__DefaultProvider"] = value,
            [$"LlmProviders__{section}__ApiKey"] = "k",
            [$"LlmProviders__{section}__Model"] = "the-model",
        });
        Assert.True(settings.IsConfigured);
        Assert.Equal(provider, settings.Model!.Provider);
        Assert.Equal("the-model", settings.Model.Model);
    }

    // ----- The relay session -----

    private static ReceptionistRelaySession Session(FakeReceptionistModel model, ILogger? log = null) =>
        new(model, "system prompt", log ?? NullLogger.Instance);

    [Fact]
    public async Task A_final_prompt_gets_one_text_reply_and_a_partial_prompt_gets_none()
    {
        var model = new FakeReceptionistModel { Reply = "Sure." };
        var session = Session(model);

        Assert.Empty(await session.HandleAsync("""{"type":"prompt","voicePrompt":"Hel","last":false}""", default));
        var sent = Assert.Single(await session.HandleAsync("""{"type":"prompt","voicePrompt":"Hello","last":true}""", default));

        Assert.Equal("""{"type":"text","token":"Sure.","last":true}""", sent);
        Assert.Equal("system prompt", Assert.Single(model.Calls).Prompt);
        Assert.Equal(2, session.Turns.Count);
    }

    [Fact]
    public async Task An_interrupt_keeps_only_what_the_caller_heard_of_the_reply()
    {
        var model = new FakeReceptionistModel { Reply = "We have three plans, the first is" };
        var session = Session(model);
        await session.HandleAsync("""{"type":"prompt","voicePrompt":"Prices?","last":true}""", default);

        Assert.Empty(await session.HandleAsync("""{"type":"interrupt","utteranceUntilInterrupt":"We have three","durationUntilInterruptMs":900}""", default));
        Assert.Equal(new ReceptionistTurn(ReceptionistSpeaker.Receptionist, "We have three"), session.Turns[^1]);
    }

    [Fact]
    public async Task A_model_failure_ends_the_call_and_logs_no_content()
    {
        var log = new CapturingLogger();
        var session = Session(new FakeReceptionistModel { Fail = true }, log);

        var sent = Assert.Single(await session.HandleAsync("""{"type":"prompt","voicePrompt":"my card is 4111","last":true}""", default));

        Assert.Equal("""{"type":"end"}""", sent);
        Assert.True(session.Ended);
        Assert.Empty(await session.HandleAsync("""{"type":"prompt","voicePrompt":"hello?","last":true}""", default));
        Assert.Contains(log.Lines, l => l.StartsWith("Error", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("4111", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Setup_dtmf_and_error_send_nothing_and_log_no_caller_detail()
    {
        var log = new CapturingLogger();
        var session = Session(new FakeReceptionistModel(), log);

        Assert.Empty(await session.HandleAsync("""{"type":"setup","from":"+15615551234","to":"+15615550199","callSid":"CA1"}""", default));
        Assert.Empty(await session.HandleAsync("""{"type":"dtmf","digit":"5"}""", default));
        Assert.Empty(await session.HandleAsync("""{"type":"error","description":"caller said 4111"}""", default));
        Assert.DoesNotContain(log.Lines, l => l.Contains("5551234", StringComparison.Ordinal) || l.Contains("4111", StringComparison.Ordinal));
        Assert.False(session.Ended);
    }

    [Fact]
    public void The_session_has_no_store_to_persist_to()
    {
        var parameters = typeof(ReceptionistRelaySession).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
        Assert.Equal([typeof(IReceptionistModel), typeof(string), typeof(ILogger)], parameters);
        Assert.DoesNotContain(parameters, t => t == typeof(IVoiceStore));
    }

    // ----- The model adapter -----

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Sent { get; private set; }
        public HttpRequestMessage? Request { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Sent = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static readonly IReadOnlyList<ReceptionistTurn> Turns = [new(ReceptionistSpeaker.Caller, "Hello there")];

    private static ReceptionistModelSettings Configured(string provider, string section) => Settings_(new()
    {
        ["LlmProviders__DefaultProvider"] = provider,
        [$"LlmProviders__{section}__ApiKey"] = "key-1",
        [$"LlmProviders__{section}__Model"] = "model-1",
    });

    [Fact]
    public async Task Anthropic_is_sent_the_system_prompt_and_turns_and_its_text_is_the_reply()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"Hi! "}],"stop_reason":"end_turn"}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Anthropic", "Anthropic"), NullLogger<LlmReceptionistModel>.Instance);

        Assert.Equal("Hi!", await model.ReplyAsync("be brief", Turns));
        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("model-1", sent.RootElement.GetProperty("model").GetString());
        Assert.Equal("be brief", sent.RootElement.GetProperty("system").GetString());
        Assert.Equal("user", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("key-1", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task OpenAi_is_sent_a_system_message_first_and_its_first_choice_is_the_reply()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"Hello."}}]}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("OpenAi", "OpenAi"), NullLogger<LlmReceptionistModel>.Instance);

        Assert.Equal("Hello.", await model.ReplyAsync("be brief", Turns));
        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("system", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("Hello there", sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task A_provider_failure_throws_and_logs_the_status_but_never_the_body_or_the_call()
    {
        var log = new CapturingLogger();
        var handler = new Handler(HttpStatusCode.BadRequest, """{"error":"echo: Hello there"}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Groq", "Groq"), new LoggerAdapter(log));

        var ex = await Assert.ThrowsAsync<ReceptionistModelException>(() => model.ReplyAsync("be brief", Turns));
        Assert.DoesNotContain("Hello there", ex.Message);
        Assert.Contains(log.Lines, l => l.Contains("400", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("Hello there", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unconfigured_model_refuses_to_call()
    {
        var model = new LlmReceptionistModel(new HttpClient(new Handler(HttpStatusCode.OK, "{}")), Settings_(new()), NullLogger<LlmReceptionistModel>.Instance);
        Assert.False(model.IsConfigured);
        await Assert.ThrowsAsync<ReceptionistModelException>(() => model.ReplyAsync("p", Turns));
    }

    private sealed class LoggerAdapter(CapturingLogger inner) : ILogger<LlmReceptionistModel>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
