using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>What a relay connection needs, issued when a caller pressed 1 on a signed consent post.</summary>
/// <param name="Line">"company" for the company line, or the tenant's id.</param>
public sealed record RelayTicket(string Line, string SystemPrompt, DateTimeOffset ExpiresAt);

/// <summary>
/// One-time tickets for ConversationRelay's WebSocket. A ticket is issued only from a consent post Twilio signed, travels in the relay
/// URL Twilio is told to open, is held in memory only, may be redeemed once, for the line it was issued for, within
/// <see cref="Lifetime"/>. Twilio's docs say the WebSocket handshake carries X-Twilio-Signature but do not say which URL or parameters
/// it signs, so the ticket is what ties a WebSocket to a call this service accepted; the signature is checked as well.
/// </summary>
public sealed class RelayTickets(TimeProvider clock)
{
    public const string CompanyLine = "company";

    /// <summary>Twilio opens the WebSocket as soon as it reads the TwiML; this only has to cover that.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, RelayTicket> _tickets = new(StringComparer.Ordinal);

    public string Issue(string line, string systemPrompt)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, ticket) in _tickets)
            if (ticket.ExpiresAt <= now) _tickets.TryRemove(key, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[token] = new RelayTicket(line, systemPrompt, now + Lifetime);
        return token;
    }

    /// <summary>The ticket, removed so it cannot be used again; null when unknown, expired or issued for another line.</summary>
    public RelayTicket? Redeem(string? token, string line)
    {
        if (string.IsNullOrEmpty(token) || !_tickets.TryRemove(token, out var ticket)) return null;
        if (ticket.ExpiresAt <= clock.GetUtcNow() || !string.Equals(ticket.Line, line, StringComparison.Ordinal)) return null;
        return ticket;
    }
}
