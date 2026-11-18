using System.Collections.Concurrent;

namespace GeekAPI.Services.Receptionist;

/// <summary>
/// Screening state for the owner's ring: whether the owner pressed a key to take a call. A missed call
/// is not recorded anywhere: no text is sent to the caller or the owner, no voicemail is taken, and
/// nothing is written to a CRM, which is in development and not part of Geek-Receptionist (Jeff, 2026-11-18: the company keeps no call
/// content, and the missed-call text-back campaign was withdrawn). State is in-memory on purpose: only
/// the screening handshake lives here.
/// </summary>
public sealed class MissedCallService(TimeProvider clock)
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

    private void Prune(ConcurrentDictionary<string, DateTimeOffset> map, TimeSpan maxAge)
    {
        var cutoff = clock.GetUtcNow() - maxAge;
        foreach (var (key, at) in map)
            if (at < cutoff) map.TryRemove(key, out _);
    }
}
