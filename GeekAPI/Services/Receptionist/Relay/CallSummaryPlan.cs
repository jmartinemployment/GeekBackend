using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// What one call's record needs, from the business's own configuration and the call's setup: where it goes, whose call it was, the
/// caller's number as Twilio gave it, whether the transcript goes with it, and whether a call where the caller said next to nothing
/// still sends one. It lives in memory, on the relay ticket, for the length of the call and is never stored.
/// </summary>
public sealed record CallSummaryRequest(
    string Destination, string BusinessName, string? CallerNumber, bool IncludeTranscript, bool NotifyOnAbandonedCalls);

/// <summary>
/// A call's request together with whatever delivers its record.
///
/// <paramref name="DeliverAlways"/> is true for a business that asked for a record of every call. It is false for the one the plan
/// requires whether or not the business asked: "Failed bookings route automatically as messages". Such a plan sits unused for every
/// call that goes well, and delivers only when the receptionist tried to book and could not, so the lead reaches the owner instead of
/// being lost between a calendar that refused it and a message setting that is off.
/// </summary>
public sealed record CallSummaryPlan(ICallSummaryDelivery Delivery, CallSummaryRequest Request, bool DeliverAlways = true)
{
    /// <summary>
    /// The business's own storage, when it connected some, and whether it asked for the whole transcript there. A write that fails
    /// does not lose the call: the record goes to the owner's email instead, which is the plan's dead-letter fallback.
    /// </summary>
    public ICallRecordStorage? Storage { get; init; }

    /// <summary>The longest a write to the business's storage may take before the record goes to email instead.</summary>
    public static readonly TimeSpan StoreBound = TimeSpan.FromSeconds(15);

    /// <summary>A caller who said fewer words than this across the whole call (dead air, noise, a dropped consent) did not make a call worth a record.</summary>
    public const int MinimumCallerWords = 3;

    /// <summary>The longest the summary request may take once the caller has hung up.</summary>
    public static readonly TimeSpan SummarizeBound = TimeSpan.FromSeconds(10);

    /// <summary>The longest delivery, with its retry, may take.</summary>
    public static readonly TimeSpan DeliverBound = TimeSpan.FromSeconds(20);
}
