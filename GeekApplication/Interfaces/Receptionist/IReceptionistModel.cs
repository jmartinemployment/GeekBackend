namespace GeekApplication.Interfaces.Receptionist;

/// <summary>Who said a turn of a call.</summary>
public enum ReceptionistSpeaker
{
    Caller,
    Receptionist,

    /// <summary>The receptionist asked for a tool; the turn's text is the arguments as JSON.</summary>
    ToolCall,

    /// <summary>What the tool answered; the turn's text is its result as JSON.</summary>
    ToolResult,
}

/// <summary>
/// One turn of a call, held in memory for the life of the call and never stored. A tool call and its result are turns too, paired by
/// <paramref name="ToolCallId"/>, so the model sees what it did and what came of it.
/// </summary>
public sealed record ReceptionistTurn(ReceptionistSpeaker Speaker, string Text, string? ToolCallId = null, string? ToolName = null);

/// <summary>A tool the receptionist may call in a call: its name, what it is for, and the JSON schema of its arguments.</summary>
public sealed record ReceptionistToolDefinition(string Name, string Description, System.Text.Json.JsonElement InputSchema);

/// <summary>A request from the model to run a tool: the model's id for the call, the tool's name and its arguments as JSON.</summary>
public sealed record ReceptionistToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// The receptionist's answer to one turn: what it says, which may be nothing, and whether it called the <c>end_call</c> tool its script
/// names to hang up, and any tools it asked for (null when it asked for none).
/// </summary>
public sealed record ReceptionistReply(string Text, bool EndCall, IReadOnlyList<ReceptionistToolCall>? ToolCalls = null);

/// <summary>One fact the caller gave, as the model labelled it (a name, a callback number, a reason). The labels are the model's, not a fixed list.</summary>
public sealed record ReceptionistSummaryField(string Label, string Value);

/// <summary>
/// The end-of-call record the model writes from one call's turns: a short outcome label, a plain summary, the facts the caller gave, what
/// the receptionist promised or did, and the questions it could not answer. Held in memory for as long as it takes to deliver it.
/// </summary>
public sealed record ReceptionistCallSummary(
    string Outcome,
    string Summary,
    IReadOnlyList<ReceptionistSummaryField> Fields,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> Unanswered);

/// <summary>Why the model gave no reply. The message names the provider and the HTTP status only, never what anyone said.</summary>
public sealed class ReceptionistModelException : Exception
{
    public ReceptionistModelException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// The model behind Geek-Receptionist's voice calls. Vendor-neutral: an implementation is chosen by registration, and no model is
/// named here. Nothing given to it is stored or logged by the caller of this interface.
/// </summary>
public interface IReceptionistModel
{
    /// <summary>False when the implementation has no provider, key or model to call; then no call is handed to the AI.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The receptionist's next reply, given its system prompt and the turns of this call so far (the last is the caller's).
    /// Throws <see cref="ReceptionistModelException"/> when no usable reply came back: no text and no call to end.
    /// </summary>
    Task<ReceptionistReply> ReplyAsync(
        string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, IReadOnlyList<ReceptionistToolDefinition>? tools = null, CancellationToken ct = default);

    /// <summary>
    /// The end-of-call record for a finished call, from its turns, in one structured request. Throws <see cref="ReceptionistModelException"/>
    /// when the provider fails or answers with no usable summary; the caller decides what to do then, this never invents one.
    /// </summary>
    Task<ReceptionistCallSummary> SummarizeAsync(IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default);
}
