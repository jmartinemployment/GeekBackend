using System.Collections.Concurrent;

namespace GeekAPI.Services.Voice.Calls;

/// <summary>
/// What the AI did on one call, as flags and nothing more: whether it answered at all, whether it booked, whether the owner was sent
/// the record, and whether it had to fall back. No word of the call is here, by the same rule as everywhere else.
/// </summary>
public sealed record CallOutcome(bool Answered, bool Booked, bool RecordDelivered, bool Failed)
{
    /// <summary>The flags as the call row keeps them, shortest first; null when the AI did nothing worth a flag.</summary>
    public string? Disposition()
    {
        var flags = new List<string>();
        if (Answered) flags.Add("answered");
        if (Booked) flags.Add("booked");
        if (RecordDelivered) flags.Add("record_delivered");
        if (Failed) flags.Add("failed");
        return flags.Count == 0 ? null : string.Join(',', flags);
    }
}

/// <summary>
/// Carries a call's outcome flags from the relay, which knows them, to Twilio's status callback, which writes the call row and arrives
/// separately (plans/client-and-app-plan.md: "Only non-content metadata (timestamp, duration, billable minutes, and tool outcome flags)
/// is saved to the platform database", and the metering rule that a call ending at or before the consent gate is not billed).
///
/// In memory on purpose and for minutes only: a flag that is never collected is pruned, and nothing here outlives the process. A call
/// with no entry never reached the AI, which is what makes it unbillable: that is the ordinary shape of a caller who hung up during the
/// disclosure or declined at the keypad.
/// </summary>
public sealed class CallOutcomes(TimeProvider clock)
{
    /// <summary>Long enough for Twilio's status callback, short enough that nothing lingers.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, (CallOutcome Outcome, DateTimeOffset At)> _outcomes = new(StringComparer.Ordinal);

    public void Record(string callSid, CallOutcome outcome)
    {
        if (string.IsNullOrWhiteSpace(callSid)) return;

        var now = clock.GetUtcNow();
        _outcomes[callSid] = (outcome, now);

        var cutoff = now - Lifetime;
        foreach (var (key, held) in _outcomes)
            if (held.At < cutoff) _outcomes.TryRemove(key, out _);
    }

    /// <summary>This call's flags, removed so they are taken once; null when the AI never answered it.</summary>
    public CallOutcome? Take(string callSid) =>
        !string.IsNullOrWhiteSpace(callSid) && _outcomes.TryRemove(callSid, out var held) ? held.Outcome : null;
}
