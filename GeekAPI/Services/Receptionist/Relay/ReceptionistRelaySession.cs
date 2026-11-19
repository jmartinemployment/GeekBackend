using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GeekApplication.Interfaces.Receptionist;

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

    private readonly List<ReceptionistTurn> _turns = [];

    public IReadOnlyList<ReceptionistTurn> Turns => _turns;

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

        ReceptionistReply reply;
        try
        {
            reply = await model.ReplyAsync(systemPrompt, _turns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("The receptionist's model gave no reply ({Failure}); the call is ended.", ex.GetType().Name);
            return End();
        }

        if (string.IsNullOrWhiteSpace(reply.Text) && !reply.EndCall)
        {
            logger.LogError("The receptionist's model gave an empty reply; the call is ended.");
            return End();
        }

        // The model's own words stay its turn, signal and all, so the next reply sees what it chose. The caller hears only the rest.
        var sent = new List<string>();
        if (!string.IsNullOrWhiteSpace(reply.Text))
        {
            _turns.Add(new ReceptionistTurn(ReceptionistSpeaker.Receptionist, reply.Text));
            var spoken = Hold().Replace(reply.Text, " ").Trim();
            if (spoken.Length > 0) sent.Add(Text(spoken));
        }

        if (reply.EndCall) sent.AddRange(End());
        return sent;
    }

    /// <summary>The hold signal, with the backticks the script prints it in when the model copies them.</summary>
    [GeneratedRegex("`?" + HoldSignal + "`?", RegexOptions.CultureInvariant)]
    private static partial Regex Hold();

    private IReadOnlyList<string> End()
    {
        Ended = true;
        return [new JsonObject { ["type"] = "end" }.ToJsonString()];
    }

    internal static string Text(string reply) => new JsonObject { ["type"] = "text", ["token"] = reply, ["last"] = true }.ToJsonString();

    /// <summary>Reads Twilio's messages until the socket closes or the call is ended, answering each.</summary>
    public async Task RunAsync(WebSocket socket, CancellationToken ct)
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

    private static async Task SendAsync(WebSocket socket, IReadOnlyList<string> messages, CancellationToken ct)
    {
        foreach (var m in messages)
            await socket.SendAsync(Encoding.UTF8.GetBytes(m), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }
}
