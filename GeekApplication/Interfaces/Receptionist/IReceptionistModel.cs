namespace GeekApplication.Interfaces.Receptionist;

/// <summary>Who said a turn of a call.</summary>
public enum ReceptionistSpeaker
{
    Caller,
    Receptionist,
}

/// <summary>One turn of a call, held in memory for the life of the call and never stored.</summary>
public sealed record ReceptionistTurn(ReceptionistSpeaker Speaker, string Text);

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
    /// Throws <see cref="ReceptionistModelException"/> when no usable reply came back.
    /// </summary>
    Task<string> ReplyAsync(string systemPrompt, IReadOnlyList<ReceptionistTurn> turns, CancellationToken ct = default);
}
