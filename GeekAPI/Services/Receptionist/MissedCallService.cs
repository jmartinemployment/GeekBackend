using System.Collections.Concurrent;

namespace GeekAPI.Services.Receptionist;

/// <summary>
/// What happens when a call is missed: it is recorded in Geek-CRM and nothing else. No text is sent to
/// the caller or the owner and no voicemail is taken (Jeff, 2026-11-18: the company keeps no call
/// content, and the missed-call text-back campaign was withdrawn). State is in-memory on purpose: only
/// the screening handshake lives here.
/// </summary>
public sealed class MissedCallService(TimeProvider clock, IReceptionistCrm crm)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _acceptedCalls = new();

    /// <summary>Records that the owner pressed a key to take the call (screening).</summary>
    public void MarkAccepted(string parentCallSid)
    {
        _acceptedCalls[parentCallSid] = clock.GetUtcNow();
        Prune(_acceptedCalls, TimeSpan.FromHours(1));
    }

    /// <summary>True (once) if the owner accepted this call via screening.</summary>
    public bool ConsumeAccepted(string callSid) => _acceptedCalls.TryRemove(callSid, out _);

    /// <summary>Records the missed call in Geek-CRM.</summary>
    public Task RecordMissedCallAsync(string callSid, string? caller, CancellationToken ct) =>
        crm.RecordMissedCallAsync(callSid, caller, ct);

    private void Prune(ConcurrentDictionary<string, DateTimeOffset> map, TimeSpan maxAge)
    {
        var cutoff = clock.GetUtcNow() - maxAge;
        foreach (var (key, at) in map)
            if (at < cutoff) map.TryRemove(key, out _);
    }
}
