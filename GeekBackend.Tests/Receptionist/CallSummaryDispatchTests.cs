using System.Net.WebSockets;
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
        FakeDelivery? delivery = null, FakeReceptionistModel? model = null, bool includeTranscript = false, bool notifyAbandoned = false, bool withPlan = true)
    {
        delivery ??= new FakeDelivery();
        model ??= new FakeReceptionistModel();
        var log = new CapturingLogger();
        var session = new ReceptionistRelaySession(model, "prompt", log)
        {
            Summary = withPlan ? new CallSummaryPlan(delivery, new CallSummaryRequest("owner@example.test", "Dag Plumbing", "+15615550100", includeTranscript, notifyAbandoned)) : null,
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
}
