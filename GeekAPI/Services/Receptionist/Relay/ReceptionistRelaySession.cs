using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Receptionist;

using GeekAPI.Services.Receptionist.Agents;
using GeekAPI.Services.Voice.Calls;
using GeekAPI.Services.Voice.Storage;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// One call on ConversationRelay's WebSocket, by the message types Twilio documents
/// (https://www.twilio.com/docs/voice/conversationrelay/websocket-messages, read 2026-11-18). A final prompt (last: true) is given to
/// the model with this call's turns and its reply is sent as one text message. The turns live in this object only and go when the
/// socket closes: nothing of the conversation is stored or logged, not the caller's number from setup, not a prompt, not a reply.
/// When the model fails the call is ended with Twilio's end message and the failure is logged without content.
/// The script's two signals are acted on, never spoken: <see cref="HoldSignal"/> (the caller asked the receptionist to wait) sends
/// nothing, and a call to the end_call tool sends whatever the reply says and then the end message.
/// </summary>
public sealed partial class ReceptionistRelaySession(IReceptionistModel model, string systemPrompt, ILogger logger)
{
    /// <summary>Larger than any documented message; a bigger one ends the call rather than growing memory without bound.</summary>
    internal const int MaxMessageBytes = 64 * 1024;

    /// <summary>What the script tells the model to output, exactly, when the caller asks it to wait.</summary>
    internal const string HoldSignal = "NO_RESPONSE_NEEDED";

    /// <summary>How many times in one turn the model may ask for tools before the call is ended; a model that never stops asking is not a conversation.</summary>
    internal const int MaxToolRounds = 4;

    /// <summary>
    /// What a caller hears when the receptionist fails mid-call and a person can be reached, word for word from the plan
    /// (plans/client-and-app-plan.md, "Spoken Mid-Call Failure Fallbacks"). The call is not dropped on them in silence.
    /// </summary>
    internal const string FailureWithTransfer =
        "I'm having a quick connection issue on my end-please hold for just a moment while I connect you directly with our team.";

    /// <summary>The same, for a business with no one to transfer to, word for word from the plan.</summary>
    internal const string FailureWithoutTransfer =
        "I'm having trouble completing that request right now. Please leave your name, phone number, and a brief message after the tone, and our team will call you right back.";

    private readonly List<ReceptionistTurn> _turns = [];
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private bool _finished;

    public IReadOnlyList<ReceptionistTurn> Turns => _turns;

    /// <summary>What to do with the finished call; null for a business that has not asked for a record, which then gets none.</summary>
    public CallSummaryPlan? Summary { get; init; }

    /// <summary>The tools this business's receptionist may use; null for a receptionist that can only talk and hang up.</summary>
    public IReceptionistToolbox? Toolbox { get; init; }

    /// <summary>
    /// The roles this business's reception is made of, when it has more than the one. Null is a reception of one, which is what every
    /// call was before roles existed: <see cref="Toolbox"/> and the constructor's prompt are used as they always were. There is no mode
    /// here — <see cref="Prompt"/> and <see cref="Tools"/> simply read from the roster when there is one.
    /// </summary>
    public ReceptionistAgents? Agents { get; init; }

    /// <summary>What the model is told this turn: the speaking role's own instructions, or the one prompt when there are no roles.</summary>
    private string Prompt => Agents?.Current.SystemPrompt ?? systemPrompt;

    /// <summary>What the model may do this turn: the speaking role's tools, or the one toolbox when there are no roles.</summary>
    private IReceptionistToolbox? Tools => Agents is null ? Toolbox : Agents.Toolbox;

    /// <summary>Whether this business has somebody a failing call can be handed to, which decides which failure line the caller hears.</summary>
    public bool TransferConfigured { get; init; }

    /// <summary>
    /// Where this call's outcome flags are left for Twilio's status callback to write with the call row; null for a line that keeps no
    /// call rows. Flags only: the plan stores timestamp, duration, billable minutes and tool outcomes, and nothing of what was said.
    /// </summary>
    public CallOutcomes? Outcomes { get; init; }

    /// <summary>Whose call this is, which the business's own storage needs to know whose connection to write through.</summary>
    public Guid TenantId { get; init; }

    private string? _callSid;

    private bool _booked;
    private bool _recordDelivered;
    private bool _failed;
    private bool _bookingFailed;

    /// <summary>True once the end message has been sent; nothing more is sent after it.</summary>
    public bool Ended { get; private set; }

    /// <summary>The messages to send back for one message from Twilio, in order.</summary>
    public async Task<IReadOnlyList<string>> HandleAsync(string message, CancellationToken ct)
    {
        if (Ended) return [];

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(message);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            logger.LogError("A ConversationRelay message was not JSON; the call is ended.");
            return End();
        }

        var type = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

        switch (type)
        {
            case "prompt":
                return await PromptAsync(root, ct).ConfigureAwait(false);

            case "interrupt":
                // The caller spoke over the reply and heard only this much of it, so only this much is a turn of the call.
                if (_turns.Count > 0 && _turns[^1].Speaker == ReceptionistSpeaker.Receptionist
                    && root.TryGetProperty("utteranceUntilInterrupt", out var heard) && heard.ValueKind == JsonValueKind.String)
                {
                    var text = heard.GetString()!;
                    if (string.IsNullOrWhiteSpace(text)) _turns.RemoveAt(_turns.Count - 1);
                    else _turns[^1] = _turns[^1] with { Text = text };
                }
                return [];

            case "error":
                logger.LogError("ConversationRelay reported an error on a call.");
                return [];

            case "setup":
                // The only place the call's Twilio id reaches this code, and the one thing taken from the message.
                if (root.TryGetProperty("callSid", out var sid) && sid.ValueKind == JsonValueKind.String) _callSid = sid.GetString();
                return [];

            case "dtmf":
            case "info":
                return [];

            default:
                logger.LogWarning("A ConversationRelay message of type {Type} is not one this code handles; it is ignored.", type ?? "(none)");
                return [];
        }
    }

    private async Task<IReadOnlyList<string>> PromptAsync(JsonElement root, CancellationToken ct)
    {
        // Partial prompts are not asked for; a prompt that is not the last of its turn is not a turn yet.
        if (!root.TryGetProperty("last", out var last) || last.ValueKind != JsonValueKind.True) return [];
        if (!root.TryGetProperty("voicePrompt", out var said) || said.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(said.GetString()))
            return [];

        _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.Caller, said.GetString()!));

        var sent = new List<string>();
        for (var round = 0; ; round++)
        {
            ReceptionistReply reply;
            try
            {
                reply = await model.ReplyAsync(Prompt, _turns, Tools?.Tools, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError("The receptionist's model gave no reply ({Failure}); the caller is told so.", ex.GetType().Name);
                return Failed(sent);
            }

            var requested = reply.ToolCalls ?? [];
            if (string.IsNullOrWhiteSpace(reply.Text) && !reply.EndCall && requested.Count == 0)
            {
                logger.LogError("The receptionist's model gave an empty reply; the caller is told so.");
                return Failed(sent);
            }

            // The model's own words stay its turn, signal and all, so the next reply sees what it chose. The caller hears only the rest.
            if (!string.IsNullOrWhiteSpace(reply.Text))
            {
                _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.Receptionist, reply.Text));
                var spoken = Hold().Replace(reply.Text, " ").Trim();
                if (spoken.Length > 0) sent.Add(Text(spoken));
            }

            if (requested.Count > 0)
            {
                var toolbox = Tools;
                if (toolbox is null || round >= MaxToolRounds)
                {
                    logger.LogError("The receptionist's model asked for tools it may not use in this call (round {Round}); the caller is told so.", round);
                    return Failed(sent);
                }

                foreach (var call in requested)
                {
                    _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.ToolCall, call.ArgumentsJson, call.Id, call.Name));
                    _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.ToolResult, await RunToolAsync(toolbox, call, ct).ConfigureAwait(false), call.Id, call.Name));

                    // A handoff that named somewhere real changes who answers the next round. The turns are not touched: the new role
                    // reads everything the caller has already said, which is the whole reason a handoff is silent to them. Asked after
                    // the tool ran, because only a handoff that succeeded moves anybody.
                    if (Agents?.TookOver(call.Name) == true) toolbox = Tools;
                }
                if (!reply.EndCall) continue;
            }

            if (reply.EndCall) sent.AddRange(End());
            return sent;
        }
    }

    /// <summary>What the model is told a tool did: its result, or only the code it failed with. Nothing the tool took or returned is logged.</summary>
    private async Task<string> RunToolAsync(IReceptionistToolbox toolbox, ReceptionistToolCall call, CancellationToken ct)
    {
        try
        {
            var result = await toolbox.RunAsync(call, ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // A booking that went through is the one tool outcome the call row keeps, as a flag and nothing more.
                if (toolbox.IsBooking(call.Name)) _booked = true;
                return result.Value!;
            }
            if (toolbox.IsBooking(call.Name)) _bookingFailed = true;
            logger.LogError("The receptionist's tool {Tool} failed ({Code}).", call.Name, result.ErrorCode);
            return new JsonObject { ["error"] = result.ErrorCode }.ToJsonString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (toolbox.IsBooking(call.Name)) _bookingFailed = true;
            logger.LogError("The receptionist's tool {Tool} failed ({Failure}).", call.Name, ex.GetType().Name);
            return new JsonObject { ["error"] = "tool_failed" }.ToJsonString();
        }
    }

    /// <summary>The hold signal, with the backticks the script prints it in when the model copies them.</summary>
    [GeneratedRegex("`?" + HoldSignal + "`?", RegexOptions.CultureInvariant)]
    private static partial Regex Hold();

    /// <summary>
    /// The receptionist could not go on. The caller hears the plan's failure line for this business rather than silence and a dropped
    /// call, and the call then ends; the end-of-call record is still built and delivered, so the lead is not lost either way.
    /// </summary>
    private IReadOnlyList<string> Failed(List<string> sent)
    {
        _failed = true;
        var spoken = TransferConfigured ? FailureWithTransfer : FailureWithoutTransfer;
        _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.Receptionist, spoken));
        sent.Add(Text(spoken));
        sent.AddRange(End());
        return sent;
    }

    private IReadOnlyList<string> End()
    {
        Ended = true;
        return [new JsonObject { ["type"] = "end" }.ToJsonString()];
    }

    internal static string Text(string reply) => new JsonObject { ["type"] = "text", ["token"] = reply, ["last"] = true }.ToJsonString();

    /// <summary>
    /// Reads Twilio's messages until the socket closes or the call is ended, answering each, then, once, hands the finished call to
    /// <see cref="Summary"/>'s delivery when the business has one. Every way a call ends passes through here, including a caller who
    /// simply hangs up.
    /// </summary>
    public async Task RunAsync(WebSocket socket, CancellationToken ct)
    {
        try
        {
            await RunLoopAsync(socket, ct).ConfigureAwait(false);
        }
        finally
        {
            await FinishAsync().ConfigureAwait(false);
            RecordOutcome();
        }
    }

    private async Task RunLoopAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var received = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
                return;
            }

            message.Write(buffer, 0, received.Count);
            if (message.Length > MaxMessageBytes)
            {
                logger.LogError("A ConversationRelay message exceeded {Max} bytes; the call is ended.", MaxMessageBytes);
                await SendAsync(socket, End(), ct).ConfigureAwait(false);
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, null, ct).ConfigureAwait(false);
                return;
            }
            if (!received.EndOfMessage) continue;

            var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            await SendAsync(socket, await HandleAsync(text, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            if (Ended)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
                return;
            }
        }
    }

    /// <summary>
    /// Builds the finished call's record from the turns, in memory, hands it to delivery, and empties the turns. Runs on a call's end,
    /// so it uses no token the hang-up could cancel and is bounded instead. It never throws: a failure is logged by its code, without
    /// content, and the record is dropped, not kept.
    /// </summary>
    private async Task FinishAsync()
    {
        var plan = Summary;
        if (plan is null || _finished) return;
        _finished = true;

        try
        {
            // A plan kept only for a failed booking stays unused when the booking went through, or was never attempted.
            if (!plan.DeliverAlways && !_bookingFailed) return;

            var callerWords = _turns.Where(t => t.Speaker == ReceptionistSpeaker.Caller)
                .Sum(t => t.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
            if (callerWords < CallSummaryPlan.MinimumCallerWords && !plan.Request.NotifyOnAbandonedCalls) return;

            var kind = FinishedCallKind.NoCallerSpeech;
            ReceptionistCallSummary? summary = null;
            if (callerWords >= CallSummaryPlan.MinimumCallerWords)
            {
                using var bound = new CancellationTokenSource(CallSummaryPlan.SummarizeBound);
                try
                {
                    summary = await model.SummarizeAsync(_turns, bound.Token).ConfigureAwait(false);
                    kind = FinishedCallKind.Summarized;
                }
                catch (Exception ex) when (ex is ReceptionistModelException || ex is OperationCanceledException)
                {
                    logger.LogError("The call's summary could not be written ({Failure}); the owner is told so.", ex.GetType().Name);
                    kind = FinishedCallKind.SummaryUnavailable;
                }
            }

            var ended = DateTimeOffset.UtcNow;
            var call = new FinishedCall(
                kind, summary, plan.Request.IncludeTranscript ? _turns.ToList() : null, ended, ended - _startedAt,
                plan.Request.Destination, plan.Request.BusinessName, plan.Request.CallerNumber);

            // The business's own storage first, when it has some. Only a write that fails falls through to email.
            var stored = await StoreAsync(plan, call).ConfigureAwait(false);
            if (stored) return;

            using var deliveryBound = new CancellationTokenSource(CallSummaryPlan.DeliverBound);
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var delivered = await plan.Delivery.DeliverAsync(call, deliveryBound.Token).ConfigureAwait(false);
                if (delivered.IsSuccess)
                {
                    _recordDelivered = true;
                    return;
                }
                logger.LogError("Delivering the call's record failed on attempt {Attempt} ({Code}).", attempt, delivered.ErrorCode);
            }

            logger.LogError("The call's record was not delivered after two attempts and is dropped.");
        }
        catch (Exception ex)
        {
            logger.LogError("Finishing the call's record failed ({Failure}); it is dropped.", ex.GetType().Name);
        }
        finally
        {
            _turns.Clear();
        }
    }

    /// <summary>
    /// Writes the call into the business's own storage, and says whether that is where it ended up. False sends it on to the owner's
    /// email: the plan's dead-letter fallback, "the payload routes immediately to the owner's contact email before RAM is cleared".
    /// A business that connected no storage is not a failure and falls straight through.
    /// </summary>
    private async Task<bool> StoreAsync(CallSummaryPlan plan, FinishedCall call)
    {
        if (plan.Storage is null) return false;

        var record = plan.Request.IncludeTranscript ? CallRecordObjects.Json(call) : CallRecordObjects.Markdown(call);
        using var bound = new CancellationTokenSource(CallSummaryPlan.StoreBound);
        try
        {
            var written = await plan.Storage.WriteAsync(TenantId, record, bound.Token).ConfigureAwait(false);
            if (written.IsSuccess)
            {
                _recordDelivered = true;
                return true;
            }

            logger.LogError("Writing the call's record to {Provider} failed ({Code}); it goes to the owner's email instead.", plan.Storage.Provider, written.ErrorCode);
        }
        catch (Exception ex)
        {
            logger.LogError("Writing the call's record to {Provider} failed ({Failure}); it goes to the owner's email instead.", plan.Storage.Provider, ex.GetType().Name);
        }

        return false;
    }

    /// <summary>
    /// Leaves this call's flags for the status callback, which writes the call row and arrives separately. An entry here is what marks
    /// the call as answered by the AI, and so as billable: a caller who hung up during the disclosure leaves none.
    /// </summary>
    private void RecordOutcome()
    {
        if (Outcomes is null || string.IsNullOrWhiteSpace(_callSid)) return;
        Outcomes.Record(_callSid, new CallOutcome(Answered: true, _booked, _recordDelivered, _failed));
    }

    private static async Task SendAsync(WebSocket socket, IReadOnlyList<string> messages, CancellationToken ct)
    {
        foreach (var m in messages)
            await socket.SendAsync(Encoding.UTF8.GetBytes(m), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }
}
