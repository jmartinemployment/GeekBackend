using System.Text;
using System.Text.Json;
using GeekAPI.Controllers.Voice;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Telephony;
using GeekApplication.Interfaces.Voice;
using GeekApplication.Models.Voice;
using GeekApplication.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace GeekBackend.Tests.Voice;

/// <summary>
/// Recording calls from Retell's webhook: only a delivery signed with the Retell key within five minutes is read; a call is recorded
/// from call_ended and call_analyzed by Retell's call id, its minutes rounded up per call, with nothing of what was said; a call
/// from another tenant's agent is refused; and a call Retell says ended but sends with no length is refused rather than recorded as zero minutes.
/// </summary>
public sealed class RetellCallRecorderTests
{
    private const string Key = "key_retell_webhook";
    private const string Agent = "agent_abc";
    private static readonly Guid Tenant = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    private sealed class FakeVoice : IVoiceStore
    {
        public string? AgentId { get; set; } = Agent;
        public bool NoTelephony { get; set; }
        public List<UpsertVoiceCallCommand> Calls { get; } = new();

        public Task<Result<VoiceTelephony>> GetTelephonyAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(NoTelephony
            ? Result<VoiceTelephony>.Failure("The tenant has no telephony.", VoiceFailureCodes.NotFound)
            : Result<VoiceTelephony>.Success(new VoiceTelephony(tenantId, "AC1", "enc", "+15615550100", "PN1", AgentId, null, null, "pending", false)));

        public Task<Result<VoiceCallStored>> UpsertCallAsync(Guid tenantId, UpsertVoiceCallCommand command, CancellationToken ct = default)
        {
            Calls.Add(command);
            return Task.FromResult(Result<VoiceCallStored>.Success(new VoiceCallStored(Guid.NewGuid())));
        }

        public Task<Result<VoiceTenant>> CreateTenantAsync(CreateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> GetTenantAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceTenant>> ActivateTenantAsync(Guid tenantId, ActivateVoiceTenantCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> GetConfigAsync(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceReceptionistConfig>> SaveConfigAsync(Guid tenantId, VoiceReceptionistConfig config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> GetMcpConnectionAsync(Guid tenantId, string provider, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceMcpConnection>> SaveMcpConnectionAsync(Guid tenantId, string provider, SaveVoiceMcpConnectionCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceKnowledgeDocumentCreated>> CreateKnowledgeDocumentAsync(Guid tenantId, CreateVoiceKnowledgeDocumentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VoiceKnowledgeHit>>> SearchKnowledgeAsync(Guid tenantId, SearchVoiceKnowledgeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VoiceAppointmentCreated>> CreateAppointmentAsync(Guid tenantId, CreateVoiceAppointmentCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static string Delivery(string evt = "call_ended", long? durationMs = 61_000, string status = "ended", string agent = Agent, string? summary = null, bool twilioSid = true)
    {
        var call = new Dictionary<string, object?>
        {
            ["call_type"] = "phone_call",
            ["call_id"] = "call_123",
            ["agent_id"] = agent,
            ["from_number"] = "+15615550111",
            ["to_number"] = "+15615550100",
            ["direction"] = "inbound",
            ["call_status"] = status,
            ["start_timestamp"] = Now.AddMinutes(-2).ToUnixTimeMilliseconds(),
            ["end_timestamp"] = Now.AddMinutes(-1).ToUnixTimeMilliseconds(),
            ["transcript_object"] = new object[]
            {
                new { role = "agent", content = "Thanks for calling.", words = new object[] { new { word = "Thanks", start = 0.1, end = 0.3 } } },
                new { role = "user", content = "My AC is out." },
            },
            ["recording_url"] = "https://recordings.example/call_123.wav",
        };
        if (durationMs is not null) call["duration_ms"] = durationMs;
        if (twilioSid) call["telephony_identifier"] = new { twilio_call_sid = "CA5d0d" };
        if (summary is not null) call["call_analysis"] = new { call_summary = summary, user_sentiment = "Neutral" };
        return JsonSerializer.Serialize(new { @event = evt, call });
    }

    private static async Task<(Result<RetellCallRecorded> Result, FakeVoice Store)> Record(string json, FakeVoice? store = null)
    {
        store ??= new FakeVoice();
        using var doc = JsonDocument.Parse(json);
        return (await new RetellCallRecorder(store, NullLogger<RetellCallRecorder>.Instance).RecordAsync(Tenant, doc.RootElement), store);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(1L, 1)]
    [InlineData(60_000L, 1)]
    [InlineData(60_001L, 2)]
    [InlineData(3_599_999L, 60)]
    public void Every_started_minute_is_billable(long durationMs, int minutes) =>
        Assert.Equal(minutes, RetellCallRecorder.BillableMinutes(durationMs));

    [Fact]
    public async Task An_ended_call_is_recorded_by_its_retell_id_with_its_minutes_rounded_up()
    {
        var (result, store) = await Record(Delivery(durationMs: 61_000));
        Assert.True(result.IsSuccess, result.Error);

        var call = Assert.Single(store.Calls);
        Assert.Equal("call_123", call.RetellCallId);
        Assert.Equal("CA5d0d", call.TwilioCallSid);
        Assert.Equal(61, call.DurationSeconds);
        Assert.Equal(2, call.BillableMinutes);
        Assert.Equal(Now.AddMinutes(-2), call.StartedAt);
        Assert.DoesNotContain(typeof(UpsertVoiceCallCommand).GetProperties(), p => p.Name is "CallerNumber" or "Transcript" or "CallSummary" or "RecordingUrl");
    }

    [Fact]
    public async Task The_analysis_records_the_call_again_and_a_missing_twilio_sid_is_left_empty()
    {
        var (_, store) = await Record(Delivery("call_analyzed", summary: "Caller's AC is out; asked for a visit.", twilioSid: false));
        var call = Assert.Single(store.Calls);
        Assert.Null(call.TwilioCallSid);
        Assert.Null(call.Disposition);
    }

    [Theory]
    [InlineData("call_started")]
    [InlineData("transcript_updated")]
    public async Task Other_events_are_accepted_and_not_recorded(string evt)
    {
        var (result, store) = await Record(Delivery(evt));
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.CallId);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_call_from_another_agent_is_refused()
    {
        var (result, store) = await Record(Delivery(agent: "agent_someone_else"));
        Assert.Equal(RetellCallFailureCodes.AgentMismatch, result.ErrorCode);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_tenant_with_no_number_is_not_found()
    {
        var (result, store) = await Record(Delivery(), new FakeVoice { NoTelephony = true });
        Assert.Equal(VoiceFailureCodes.NotFound, result.ErrorCode);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task An_ended_call_without_a_length_is_refused_not_recorded_as_zero()
    {
        var (result, store) = await Record(Delivery(durationMs: null));
        Assert.Equal(RetellCallFailureCodes.InvalidCall, result.ErrorCode);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task A_call_that_never_connected_has_no_minutes()
    {
        var (result, store) = await Record(Delivery(durationMs: null, status: RetellCallRecorder.NotConnected));
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(0, Assert.Single(store.Calls).BillableMinutes);
    }

    // ----- Signature and route -----

    [Fact]
    public void A_signature_holds_only_for_the_same_body_key_and_a_recent_time()
    {
        var body = Encoding.UTF8.GetBytes(Delivery());
        var header = RetellWebhookSignature.Sign(body, Key, Now);

        Assert.True(RetellWebhookSignature.IsValid(body, header, Key, Now));
        Assert.True(RetellWebhookSignature.IsValid(body, header, Key, Now.AddMinutes(4)));
        Assert.False(RetellWebhookSignature.IsValid(body, header, Key, Now.AddMinutes(6)));
        Assert.False(RetellWebhookSignature.IsValid(body, header, Key, Now.AddMinutes(-6)));
        Assert.False(RetellWebhookSignature.IsValid(body, header, "another_key", Now));
        Assert.False(RetellWebhookSignature.IsValid(Encoding.UTF8.GetBytes(Delivery(durationMs: 600_000)), header, Key, Now));
        Assert.False(RetellWebhookSignature.IsValid(body, null, Key, Now));
        Assert.False(RetellWebhookSignature.IsValid(body, "v=1,d=zz", Key, Now));
    }

    private static async Task<(int Status, FakeVoice Store)> Post(string json, string? signature, string? webhookKey = Key)
    {
        var store = new FakeVoice();
        var controller = new RetellWebhookController(
            new RetellOptions("api_key_without_the_webhook_badge", "gpt", "https://api.example", webhookKey),
            new RetellCallRecorder(store, NullLogger<RetellCallRecorder>.Instance),
            new FakeTimeProvider(Now),
            NullLogger<RetellWebhookController>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (signature is not null) context.Request.Headers[RetellWebhookSignature.Header] = signature;
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        var result = await controller.Receive(Tenant, CancellationToken.None);
        return (result switch { StatusCodeResult s => s.StatusCode, ObjectResult o => o.StatusCode ?? 200, _ => 0 }, store);
    }

    [Fact]
    public async Task The_route_records_a_signed_delivery_and_answers_204()
    {
        var json = Delivery();
        var (status, store) = await Post(json, RetellWebhookSignature.Sign(Encoding.UTF8.GetBytes(json), Key, Now));
        Assert.Equal(204, status);
        Assert.Single(store.Calls);
    }

    [Fact]
    public async Task The_route_refuses_an_unsigned_or_missigned_delivery_without_reading_it()
    {
        var json = Delivery();
        Assert.Equal(401, (await Post(json, null)).Status);
        var (status, store) = await Post(json, RetellWebhookSignature.Sign(Encoding.UTF8.GetBytes(json), "wrong", Now));
        Assert.Equal(401, status);
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task Without_the_retell_key_nothing_is_accepted()
    {
        var json = Delivery();
        Assert.Equal(503, (await Post(json, RetellWebhookSignature.Sign(Encoding.UTF8.GetBytes(json), Key, Now), webhookKey: null)).Status);
    }

    [Fact]
    public async Task Another_agents_call_answers_403()
    {
        var json = Delivery(agent: "agent_someone_else");
        Assert.Equal(403, (await Post(json, RetellWebhookSignature.Sign(Encoding.UTF8.GetBytes(json), Key, Now))).Status);
    }
}
