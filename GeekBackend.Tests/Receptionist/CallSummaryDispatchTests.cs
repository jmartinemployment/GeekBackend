using System.Net.WebSockets;
using System.Text.Json;
using GeekAPI.Services.Voice.Calls;
using Microsoft.Extensions.Time.Testing;
using System.Text;
using GeekAPI.Services.Receptionist.Relay;
using GeekApplication.Interfaces.Receptionist;
using GeekApplication.Results;

namespace GeekBackend.Tests.Receptionist;

/// <summary>
/// What happens to a finished call's record: handed to delivery once however the call ends, only when the business asked for one, built
/// from the caller's words, never delivered for a call with nothing in it unless the business wants those, with the transcript only on
/// opt-in, retried once, and never leaving a word of the call in a log or in the session afterwards.
/// </summary>
public sealed class CallSummaryDispatchTests
{
    private const string Said = "My water heater is leaking badly";

    private sealed class ScriptedSocket(IEnumerable<string> messages, bool hangUpAbruptly = false) : WebSocket
    {
        private readonly Queue<string> _messages = new(messages);
        private WebSocketState _state = WebSocketState.Open;

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (_messages.Count == 0)
            {
                if (hangUpAbruptly) throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            var bytes = Encoding.UTF8.GetBytes(_messages.Dequeue());
            bytes.CopyTo(buffer.Array!, buffer.Offset);
            return Task.FromResult(new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private class FakeDelivery(params Result<string>[] results) : ICallSummaryDelivery
    {
        private int _next;
        public List<FinishedCall> Calls { get; } = new();

        public virtual Task<Result<string>> DeliverAsync(FinishedCall call, CancellationToken ct = default)
        {
            Calls.Add(call);
            var result = results.Length == 0 ? Result<string>.Success("delivered-1") : results[Math.Min(_next++, results.Length - 1)];
            return Task.FromResult(result);
        }
    }

    private static string Prompt(string text) => $$"""{"type":"prompt","voicePrompt":"{{text}}","last":true}""";

    private static (ReceptionistRelaySession Session, FakeDelivery Delivery, FakeReceptionistModel Model, CapturingLogger Log) Make(
        FakeDelivery? delivery = null, FakeReceptionistModel? model = null, bool includeTranscript = false, bool notifyAbandoned = false, bool withPlan = true,
        CallOutcomes? outcomes = null)
    {
        delivery ??= new FakeDelivery();
        model ??= new FakeReceptionistModel();
        var log = new CapturingLogger();
        var session = new ReceptionistRelaySession(model, "prompt", log)
        {
            Summary = withPlan ? new CallSummaryPlan(delivery, new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", includeTranscript, notifyAbandoned)) : null,
            Outcomes = outcomes,
        };
        return (session, delivery, model, log);
    }

    [Fact]
    public async Task A_call_that_closes_normally_delivers_one_summary_and_no_transcript()
    {
        var (session, delivery, model, _) = Make();

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        var call = Assert.Single(delivery.Calls);
        Assert.Equal(FinishedCallKind.Summarized, call.Kind);
        Assert.Equal(model.Summary, call.Summary);
        Assert.Null(call.Transcript);
        Assert.Single(model.SummaryCalls);
        Assert.Contains(model.SummaryCalls[0], t => t.Speaker == ReceptionistSpeaker.Caller && t.Text == Said);
    }

    [Fact]
    public async Task A_caller_who_just_hangs_up_still_delivers_once_and_the_failure_still_surfaces()
    {
        var (session, delivery, _, _) = Make();

        await Assert.ThrowsAsync<WebSocketException>(() =>
            session.RunAsync(new ScriptedSocket([Prompt(Said)], hangUpAbruptly: true), CancellationToken.None));

        Assert.Single(delivery.Calls);
    }

    [Fact]
    public async Task A_call_the_model_ends_delivers_once()
    {
        var (session, delivery, _, _) = Make(model: new FakeReceptionistModel { EndCall = true });

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

    [Fact]
    public async Task A_business_with_no_plan_gets_no_summary_and_the_model_is_never_asked_for_one()
    {
        var (session, delivery, model, _) = Make(withPlan: false);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Empty(delivery.Calls);
        Assert.Empty(model.SummaryCalls);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("hi there")]
    public async Task A_call_with_fewer_than_three_caller_words_delivers_nothing(string said)
    {
        var (session, delivery, model, _) = Make();

        await session.RunAsync(new ScriptedSocket([Prompt(said)]), CancellationToken.None);

        Assert.Empty(delivery.Calls);
        Assert.Empty(model.SummaryCalls);
    }

    [Fact]
    public async Task A_business_that_wants_empty_calls_gets_one_marked_so_without_a_model_request()
    {
        var (session, delivery, model, _) = Make(notifyAbandoned: true);

        await session.RunAsync(new ScriptedSocket([]), CancellationToken.None);

        var call = Assert.Single(delivery.Calls);
        Assert.Equal(FinishedCallKind.NoCallerSpeech, call.Kind);
        Assert.Null(call.Summary);
        Assert.Empty(model.SummaryCalls);
    }

    [Fact]
    public async Task When_the_summary_cannot_be_written_the_owner_is_still_sent_a_record_that_says_so()
    {
        var (session, delivery, _, log) = Make(model: new FakeReceptionistModel { FailSummary = true });

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        var call = Assert.Single(delivery.Calls);
        Assert.Equal(FinishedCallKind.SummaryUnavailable, call.Kind);
        Assert.Null(call.Summary);
        Assert.Contains(log.Lines, l => l.Contains("could not be written", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_transcript_goes_with_the_record_only_when_the_business_opted_in()
    {
        var (session, delivery, _, _) = Make(includeTranscript: true);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        var transcript = Assert.Single(delivery.Calls).Transcript;
        Assert.NotNull(transcript);
        Assert.Contains(transcript, t => t.Text == Said);
    }

    [Fact]
    public async Task A_failed_delivery_is_tried_once_more_and_a_second_success_ends_it()
    {
        var delivery = new FakeDelivery(Result<string>.Failure("down", "delivery_failed"), Result<string>.Success("id-2"));
        var (session, _, _, log) = Make(delivery);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Equal(2, delivery.Calls.Count);
        Assert.Contains(log.Lines, l => l.Contains("attempt 1", StringComparison.Ordinal) && l.Contains("delivery_failed", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("dropped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_delivery_that_fails_twice_is_logged_loudly_dropped_and_leaves_no_call_content_anywhere()
    {
        var delivery = new FakeDelivery(Result<string>.Failure("down", "delivery_failed"));
        var (session, _, _, log) = Make(delivery);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Equal(2, delivery.Calls.Count);
        Assert.Contains(log.Lines, l => l.StartsWith("Error", StringComparison.Ordinal) && l.Contains("dropped", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lines, l => l.Contains("water heater", StringComparison.Ordinal));
        Assert.Empty(session.Turns);
    }

    [Fact]
    public async Task A_delivery_that_throws_does_not_escape_the_end_of_the_call()
    {
        var (session, _, _, log) = Make(new ThrowingDelivery());

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Contains(log.Lines, l => l.Contains("dropped", StringComparison.Ordinal));
        Assert.Empty(session.Turns);
    }

    [Fact]
    public async Task Running_a_session_twice_delivers_only_once()
    {
        var (session, delivery, _, _) = Make();

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);
        await session.RunAsync(new ScriptedSocket([]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

    private sealed class ThrowingDelivery : FakeDelivery
    {
        public override Task<Result<string>> DeliverAsync(FinishedCall call, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
    private static string Setup(string callSid) => $$"""{"type":"setup","callSid":"{{callSid}}"}""";

    // ----- The flags the call row keeps, for billing and for what the AI did -----

    [Fact]
    public async Task A_call_the_ai_answered_leaves_its_flags_under_the_call_sid_from_setup()
    {
        var outcomes = new CallOutcomes(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var (session, _, _, _) = Make(outcomes: outcomes);

        await session.RunAsync(new ScriptedSocket([Setup("CAtest1"), Prompt(Said)]), CancellationToken.None);

        var outcome = outcomes.Take("CAtest1");
        Assert.NotNull(outcome);
        Assert.True(outcome!.Answered);
        Assert.True(outcome.RecordDelivered);
        Assert.False(outcome.Booked);
        Assert.False(outcome.Failed);
        Assert.Equal("answered,record_delivered", outcome.Disposition());
    }

    [Fact]
    public async Task A_call_whose_setup_never_named_it_leaves_no_flags_so_no_other_row_is_billed()
    {
        var outcomes = new CallOutcomes(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var (session, _, _, _) = Make(outcomes: outcomes);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Null(outcomes.Take("CAtest1"));
    }

    [Fact]
    public async Task A_receptionist_that_had_to_fall_back_says_so_in_the_flags()
    {
        var outcomes = new CallOutcomes(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var (session, _, _, _) = Make(model: new FakeReceptionistModel { Fail = true }, outcomes: outcomes);

        await session.RunAsync(new ScriptedSocket([Setup("CAtest2"), Prompt(Said)]), CancellationToken.None);

        var outcome = outcomes.Take("CAtest2");
        Assert.NotNull(outcome);
        Assert.True(outcome!.Failed);
        Assert.Contains("failed", outcome.Disposition()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_word_of_the_call_is_in_the_flags()
    {
        var outcomes = new CallOutcomes(new FakeTimeProvider(DateTimeOffset.UtcNow));
        var (session, _, _, _) = Make(outcomes: outcomes);

        await session.RunAsync(new ScriptedSocket([Setup("CAtest3"), Prompt(Said)]), CancellationToken.None);

        var flags = outcomes.Take("CAtest3")!.Disposition()!;
        Assert.DoesNotContain(Said, flags, StringComparison.OrdinalIgnoreCase);
        Assert.All(flags.Split(','), f => Assert.Contains(f, new[] { "answered", "booked", "record_delivered", "failed" }));
    }

    // ----- "Failed bookings route automatically as messages" -----

    /// <summary>A toolbox whose one tool books, and either succeeds or fails as the test asks.</summary>
    private sealed class BookingToolbox(bool succeeds) : IReceptionistToolbox
    {
        public const string Name = "create_event";

        public IReadOnlyList<ReceptionistToolDefinition> Tools { get; } =
            [new ReceptionistToolDefinition(Name, "Books an appointment.", JsonDocument.Parse("{}").RootElement)];

        public bool IsBooking(string toolName) => string.Equals(toolName, Name, StringComparison.Ordinal);

        public Task<Result<string>> RunAsync(ReceptionistToolCall call, CancellationToken ct = default) =>
            Task.FromResult(succeeds
                ? Result<string>.Success("""{"id":"evt_1"}""")
                : Result<string>.Failure("The calendar refused the booking.", "gateway_unavailable"));
    }

    private static (ReceptionistRelaySession Session, FakeDelivery Delivery) MakeBooking(bool succeeds, bool deliverAlways)
    {
        var delivery = new FakeDelivery();
        var model = new FakeReceptionistModel();
        model.Script.Enqueue(new ReceptionistReply("", false, [new ReceptionistToolCall("c1", BookingToolbox.Name, "{}")]));
        model.Script.Enqueue(new ReceptionistReply("All set.", true));
        var session = new ReceptionistRelaySession(model, "prompt", new CapturingLogger())
        {
            Toolbox = new BookingToolbox(succeeds),
            Summary = new CallSummaryPlan(
                delivery,
                new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", false, false),
                DeliverAlways: deliverAlways),
        };
        return (session, delivery);
    }

    [Fact]
    public async Task A_booking_that_failed_reaches_the_owner_even_though_the_business_asked_for_no_messages()
    {
        var (session, delivery) = MakeBooking(succeeds: false, deliverAlways: false);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

    [Fact]
    public async Task A_booking_that_went_through_sends_nothing_to_a_business_that_asked_for_no_messages()
    {
        var (session, delivery) = MakeBooking(succeeds: true, deliverAlways: false);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Empty(delivery.Calls);
    }

    [Fact]
    public async Task A_call_that_never_tried_to_book_sends_nothing_to_that_business_either()
    {
        var delivery = new FakeDelivery();
        var session = new ReceptionistRelaySession(new FakeReceptionistModel(), "prompt", new CapturingLogger())
        {
            Summary = new CallSummaryPlan(
                delivery,
                new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", false, false),
                DeliverAlways: false),
        };

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Empty(delivery.Calls);
    }

    [Fact]
    public async Task A_business_that_asked_for_messages_still_gets_every_call_booking_or_not()
    {
        var (session, delivery) = MakeBooking(succeeds: true, deliverAlways: true);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

    // ----- The plan's dead-letter fallback -----

    private sealed class FakeStorage(Result<string> result) : ICallRecordStorage
    {
        public List<CallRecordObject> Written { get; } = new();
        public string Provider => "google_drive";

        public Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default)
        {
            Written.Add(record);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingStorage : ICallRecordStorage
    {
        public string Provider => "google_drive";

        public Task<Result<string>> WriteAsync(Guid tenantId, CallRecordObject record, CancellationToken ct = default) =>
            throw new InvalidOperationException("the network went away");
    }

    private static (ReceptionistRelaySession Session, FakeDelivery Delivery) WithStorage(
        ICallRecordStorage? storage, bool includeTranscript = false)
    {
        var delivery = new FakeDelivery();
        var session = new ReceptionistRelaySession(new FakeReceptionistModel(), "prompt", new CapturingLogger())
        {
            Summary = new CallSummaryPlan(
                delivery,
                new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550133", includeTranscript, false))
            {
                Storage = storage,
            },
        };
        return (session, delivery);
    }

    [Fact]
    public async Task A_record_written_to_the_businesss_own_storage_is_not_also_emailed()
    {
        var storage = new FakeStorage(Result<string>.Success("file-1"));
        var (session, delivery) = WithStorage(storage);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(storage.Written);
        Assert.Empty(delivery.Calls);
    }

    [Fact]
    public async Task A_storage_write_that_fails_routes_the_record_to_the_owners_email_instead()
    {
        var storage = new FakeStorage(Result<string>.Failure("The quota is full.", "storage_write_failed"));
        var (session, delivery) = WithStorage(storage);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(storage.Written);
        Assert.Single(delivery.Calls);
    }

    [Fact]
    public async Task A_storage_that_throws_loses_nothing_either()
    {
        var (session, delivery) = WithStorage(new ThrowingStorage());

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

    [Fact]
    public async Task A_business_that_asked_for_transcripts_has_them_written_as_json()
    {
        var storage = new FakeStorage(Result<string>.Success("file-1"));
        var (session, _) = WithStorage(storage, includeTranscript: true);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.EndsWith(".json", Assert.Single(storage.Written).Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_business_with_no_storage_connected_is_emailed_as_before()
    {
        var (session, delivery) = WithStorage(null);

        await session.RunAsync(new ScriptedSocket([Prompt(Said)]), CancellationToken.None);

        Assert.Single(delivery.Calls);
    }

}
