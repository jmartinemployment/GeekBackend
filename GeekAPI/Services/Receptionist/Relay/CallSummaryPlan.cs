using GeekApplication.Interfaces.Receptionist;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>
/// What one call's record needs, from the business's own configuration and the call's setup: where it goes, whose call it was, the
/// caller's number as Twilio gave it, whether the transcript goes with it, and whether a call where the caller said next to nothing
/// still sends one. It lives in memory, on the relay ticket, for the length of the call and is never stored.
/// </summary>
public sealed record CallSummaryRequest(
    string Destination, string BusinessName, string? CallerNumber, bool IncludeTranscript, bool NotifyOnAbandonedCalls);

/// <summary>A call's request together with whatever delivers its record.</summary>
public sealed record CallSummaryPlan(ICallSummaryDelivery Delivery, CallSummaryRequest Request)
{
    /// <summary>A caller who said fewer words than this across the whole call (dead air, noise, a dropped consent) did not make a call worth a record.</summary>
    public const int MinimumCallerWords = 3;

    /// <summary>The longest the summary request may take once the caller has hung up.</summary>
    public static readonly TimeSpan SummarizeBound = TimeSpan.FromSeconds(10);

    /// <summary>The longest delivery, with its retry, may take.</summary>
    public static readonly TimeSpan DeliverBound = TimeSpan.FromSeconds(20);
}
