using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeekAPI.Services.Receptionist;
using GeekAPI.Services.Receptionist.Relay;
using GeekAPI.Services.Voice.Onboarding;
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
    public bool EndCall { get; init; }
    public List<(string Prompt, List<ReceptionistTurn> Turns)> Calls { get; } = new();
    public bool FailSummary { get; init; }
    public ReceptionistCallSummary Summary { get; init; } = new("Message taken", "The caller asked for a callback.", [], [], []);
    public List<List<ReceptionistTurn>> SummaryCalls { get; } = new();

    public Task<ReceptionistReply> ReplyAsync(string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default)
    {
        Calls.Add((systemPrompt, turns.ToList()));
        if (Fail) throw new ReceptionistModelException("anthropic answered 500.");
        return Task.FromResult(new ReceptionistReply(Reply, EndCall));
    }

    public Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default)
    {
        SummaryCalls.Add(turns.ToList());
        if (FailSummary) throw new ReceptionistModelException("anthropic answered 500.");
        return Task.FromResult(Summary);
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

    [Fact]
    public void A_ticket_lives_for_a_minute_so_what_it_carries_does_not_linger_in_memory()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), RelayTickets.Lifetime);

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        var tickets = new RelayTickets(clock);
        var token = tickets.Issue("tenant-a", "p", new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", false, false));

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.NotNull(tickets.Redeem(token, "tenant-a"));

        var late = tickets.Issue("tenant-a", "p", new CallSummaryRequest("owner@example.test", "Dag Plumbing", null, false, false));
        clock.Advance(RelayTickets.Lifetime);
        Assert.Null(tickets.Redeem(late, "tenant-a"));
    }

    [Fact]
    public void A_ticket_hands_back_the_summary_request_it_was_issued_with_and_a_plain_one_carries_none()
    {
        var tickets = new RelayTickets(new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero)));
        var request = new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", true, false);

        Assert.Equal(request, tickets.Redeem(tickets.Issue("tenant-a", "p", request), "tenant-a")!.Summary);
        Assert.Null(tickets.Redeem(tickets.Issue("tenant-a", "p"), "tenant-a")!.Summary);
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
    public void The_script_names_the_two_signals_the_relay_acts_on()
    {
        Assert.Contains("`" + LlmReceptionistModel.EndCallTool + "`", ReceptionistDefaults.ScriptText, StringComparison.Ordinal);
        Assert.Contains("`" + ReceptionistRelaySession.HoldSignal + "`", ReceptionistDefaults.ScriptText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NO_RESPONSE_NEEDED")]
    [InlineData("`NO_RESPONSE_NEEDED`")]
    [InlineData("  NO_RESPONSE_NEEDED\n")]
    public async Task The_hold_signal_is_never_spoken_and_stays_the_models_turn(string reply)
    {
        var model = new FakeReceptionistModel { Reply = reply };
        var session = Session(model);

        Assert.Empty(await session.HandleAsync("""{"type":"prompt","voicePrompt":"Hold on a second.","last":true}""", default));
        Assert.Equal(new ReceptionistTurn(ReceptionistSpeaker.Receptionist, reply), session.Turns[^1]);
        Assert.False(session.Ended);
    }

    [Fact]
    public async Task Words_said_with_the_hold_signal_are_spoken_without_it()
    {
        var session = Session(new FakeReceptionistModel { Reply = "Take your time. `NO_RESPONSE_NEEDED`" });

        var sent = Assert.Single(await session.HandleAsync("""{"type":"prompt","voicePrompt":"One moment.","last":true}""", default));
        Assert.Equal("""{"type":"text","token":"Take your time.","last":true}""", sent);
    }

    [Fact]
    public async Task End_call_speaks_the_reply_then_ends_the_call()
    {
        var session = Session(new FakeReceptionistModel { Reply = "Thanks for calling.", EndCall = true });

        var sent = await session.HandleAsync("""{"type":"prompt","voicePrompt":"That's all.","last":true}""", default);

        Assert.Equal(["""{"type":"text","token":"Thanks for calling.","last":true}""", """{"type":"end"}"""], sent);
        Assert.True(session.Ended);
        Assert.Empty(await session.HandleAsync("""{"type":"prompt","voicePrompt":"Bye.","last":true}""", default));
    }

    [Fact]
    public async Task End_call_with_nothing_to_say_only_ends_the_call_and_logs_no_error()
    {
        var log = new CapturingLogger();
        var session = Session(new FakeReceptionistModel { Reply = "", EndCall = true }, log);

        var sent = Assert.Single(await session.HandleAsync("""{"type":"prompt","voicePrompt":"Bye.","last":true}""", default));
        Assert.Equal("""{"type":"end"}""", sent);
        Assert.True(session.Ended);
        Assert.Equal(ReceptionistSpeaker.Caller, session.Turns[^1].Speaker);
        Assert.DoesNotContain(log.Lines, l => l.StartsWith("Error", StringComparison.Ordinal));
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

        Assert.Equal(new ReceptionistReply("Hi!", false), await model.ReplyAsync("be brief", Turns));
        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("model-1", sent.RootElement.GetProperty("model").GetString());
        var tool = Assert.Single(sent.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("end_call", tool.GetProperty("name").GetString());
        Assert.Equal("object", tool.GetProperty("input_schema").GetProperty("type").GetString());
        Assert.Equal("be brief", sent.RootElement.GetProperty("system").GetString());
        Assert.Equal("user", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("key-1", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task OpenAi_is_sent_a_system_message_first_and_its_first_choice_is_the_reply()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"Hello."}}]}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("OpenAi", "OpenAi"), NullLogger<LlmReceptionistModel>.Instance);

        Assert.Equal(new ReceptionistReply("Hello.", false), await model.ReplyAsync("be brief", Turns));
        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("system", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        var tool = Assert.Single(sent.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal("end_call", tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("Hello there", sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Anthropic_calling_end_call_ends_the_call_with_its_text()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"Goodbye."},{"type":"tool_use","id":"t1","name":"end_call","input":{}}],"stop_reason":"tool_use"}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Anthropic", "Anthropic"), NullLogger<LlmReceptionistModel>.Instance);

        Assert.Equal(new ReceptionistReply("Goodbye.", true), await model.ReplyAsync("be brief", Turns));
    }

    [Fact]
    public async Task OpenAi_calling_end_call_with_no_content_ends_the_call_with_nothing_to_say()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"c1","type":"function","function":{"name":"end_call","arguments":"{}"}}]}}]}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("OpenAi", "OpenAi"), NullLogger<LlmReceptionistModel>.Instance);

        Assert.Equal(new ReceptionistReply("", true), await model.ReplyAsync("be brief", Turns));
    }

    [Fact]
    public async Task A_tool_the_receptionist_was_not_given_throws_and_logs_no_content()
    {
        var log = new CapturingLogger();
        var handler = new Handler(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"Booked for Hello there.","tool_calls":[{"id":"c1","type":"function","function":{"name":"book","arguments":"{}"}}]}}]}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Groq", "Groq"), new LoggerAdapter(log));

        var ex = await Assert.ThrowsAsync<ReceptionistModelException>(() => model.ReplyAsync("be brief", Turns));
        Assert.DoesNotContain("Hello there", ex.Message);
        Assert.DoesNotContain(log.Lines, l => l.Contains("Hello there", StringComparison.Ordinal) || l.Contains("book", StringComparison.Ordinal));
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

    private static readonly IReadOnlyList<ReceptionistTurn> CallTurns =
    [
        new(ReceptionistSpeaker.Caller, "My water heater is leaking, call Jane back on 561-555-0199"),
        new(ReceptionistSpeaker.Receptionist, "I will pass that on."),
    ];

    private const string SummaryJson =
        """{"outcome":"Callback requested","summary":"Jane has a leaking water heater and wants a call back.","fields":[{"label":"Name","value":"Jane"},{"label":"Callback number","value":"561-555-0199"},{"label":"","value":"dropped"},{"label":"Reason","value":" "}],"actions":["Promised to pass on the message"],"unanswered":[]}""";

    private static void AssertJanesSummary(ReceptionistCallSummary summary)
    {
        Assert.Equal("Callback requested", summary.Outcome);
        Assert.Equal("Jane has a leaking water heater and wants a call back.", summary.Summary);
        Assert.Equal([new ReceptionistSummaryField("Name", "Jane"), new ReceptionistSummaryField("Callback number", "561-555-0199")], summary.Fields);
        Assert.Equal(["Promised to pass on the message"], summary.Actions);
        Assert.Empty(summary.Unanswered);
    }

    [Fact]
    public async Task Anthropic_is_forced_to_call_the_summary_tool_and_its_input_is_the_summary()
    {
        var handler = new Handler(HttpStatusCode.OK, $$"""{"content":[{"type":"tool_use","id":"t1","name":"submit_call_summary","input":{{SummaryJson}}}],"stop_reason":"tool_use"}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Anthropic", "Anthropic"), NullLogger<LlmReceptionistModel>.Instance);

        AssertJanesSummary(await model.SummarizeAsync(CallTurns));

        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("tool", sent.RootElement.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("submit_call_summary", sent.RootElement.GetProperty("tool_choice").GetProperty("name").GetString());
        Assert.Equal("submit_call_summary", Assert.Single(sent.RootElement.GetProperty("tools").EnumerateArray()).GetProperty("name").GetString());
        var transcript = sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
        Assert.Contains("Caller: My water heater is leaking", transcript, StringComparison.Ordinal);
        Assert.Contains("Receptionist: I will pass that on.", transcript, StringComparison.Ordinal);
        Assert.Equal("key-1", handler.Request!.Headers.GetValues("x-api-key").Single());
    }

    [Fact]
    public async Task OpenAi_is_asked_for_a_json_object_and_a_code_fenced_answer_is_still_read()
    {
        var content = "Here is the record:\n```json\n" + SummaryJson + "\n```";
        var handler = new Handler(HttpStatusCode.OK, JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } }));
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("OpenAi", "OpenAi"), NullLogger<LlmReceptionistModel>.Instance);

        AssertJanesSummary(await model.SummarizeAsync(CallTurns));

        using var sent = JsonDocument.Parse(handler.Sent!);
        Assert.Equal("json_object", sent.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("system", sent.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.False(sent.RootElement.TryGetProperty("tools", out _));
        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
    }

    [Theory]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":"I could not summarize that."}}]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":"{\"outcome\":\"Message taken\"}"}}]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":"{\"outcome\":\"Message taken\",\"summary\":\"  \"}"}}]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":"{not json}"}}]}""")]
    [InlineData("""{"choices":[{"message":{"role":"assistant","content":null}}]}""")]
    public async Task An_answer_with_no_usable_summary_throws_and_logs_no_content(string body)
    {
        var log = new CapturingLogger();
        var model = new LlmReceptionistModel(new HttpClient(new Handler(HttpStatusCode.OK, body)), Configured("OpenAi", "OpenAi"), new LoggerAdapter(log));

        var ex = await Assert.ThrowsAsync<ReceptionistModelException>(() => model.SummarizeAsync(CallTurns));
        Assert.DoesNotContain("water heater", ex.Message, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("no usable call summary", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("water heater", StringComparison.Ordinal) || l.Contains("561-555-0199", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_summary_provider_failure_throws_and_logs_the_status_but_never_the_call()
    {
        var log = new CapturingLogger();
        var handler = new Handler(HttpStatusCode.ServiceUnavailable, """{"error":"echo: water heater"}""");
        var model = new LlmReceptionistModel(new HttpClient(handler), Configured("Anthropic", "Anthropic"), new LoggerAdapter(log));

        var ex = await Assert.ThrowsAsync<ReceptionistModelException>(() => model.SummarizeAsync(CallTurns));
        Assert.DoesNotContain("water heater", ex.Message, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("503", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("water heater", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unconfigured_model_refuses_to_summarize()
    {
        var model = new LlmReceptionistModel(new HttpClient(new Handler(HttpStatusCode.OK, "{}")), Settings_(new()), NullLogger<LlmReceptionistModel>.Instance);
        await Assert.ThrowsAsync<ReceptionistModelException>(() => model.SummarizeAsync(CallTurns));
    }

    private sealed class LoggerAdapter(CapturingLogger inner) : ILogger<LlmReceptionistModel>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }
}
