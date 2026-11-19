using GeekApplication.Results;

namespace GeekApplication.Interfaces.Receptionist;

/// <summary>What the end-of-call record holds: a summary the model wrote, no summary because the model could not write one, or a call where the caller said too little to summarize.</summary>
public enum FinishedCallKind
{
    Summarized,
    SummaryUnavailable,
    NoCallerSpeech,
}

/// <summary>
/// One finished call as it is handed to delivery. It exists in memory only for as long as delivery takes. <paramref name="Transcript"/> is
/// null unless the business opted in to receive it.
/// </summary>
public sealed record FinishedCall(
    FinishedCallKind Kind,
    ReceptionistCallSummary? Summary,
    IReadOnlyList<ReceptionistTurn>? Transcript,
    DateTimeOffset EndedAt,
    TimeSpan Duration);

/// <summary>
/// Gets a finished call's record to the business that owns the receptionist. The channel is the implementation's business. A failure is a
/// result, never a thrown exception and never a silent success; the record is not kept anywhere if it fails.
/// </summary>
public interface ICallSummaryDelivery
{
    /// <summary>The provider's id for what was delivered, or a coded failure.</summary>
    Task<Result<string>> DeliverAsync(FinishedCall call, CancellationToken ct = default);
}
