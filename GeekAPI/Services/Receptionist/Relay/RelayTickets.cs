using System.Collections.Concurrent;
using System.Security.Cryptography;
using GeekApplication.Models.Voice;

namespace GeekAPI.Services.Receptionist.Relay;

/// <summary>What a relay connection needs, issued when a caller pressed 1 on a signed consent post.</summary>
/// <param name="Line">"company" for the company line, or the tenant's id.</param>
/// <param name="Summary">What the call's end-of-call record needs; null when the business has not asked for one.</param>
/// <param name="BookingEnabled">Whether this business lets its receptionist put an appointment on its calendar.</param>
/// <param name="TransferConfigured">Whether this business has somebody a failing call can be handed to.</param>
/// <param name="DeliverSummaryAlways">True when the business asked for a record of every call; false when one is kept only for a failed booking.</param>
/// <param name="KnowledgeRunId">The business's own indexed site, which its receptionist answers questions from; null when it has none.</param>
/// <param name="Config">
/// The business's own receptionist settings, which the roles are built from when the call connects. Null on the company line, which has
/// no stored settings and answers with one role. It is held in memory for the ticket's sixty seconds and is business configuration, not
/// call content, so Zero Data Retention is untouched.
/// </param>
/// <param name="SpokenGreeting">What this caller actually heard as the call connected, so no role greets them a second time.</param>
public sealed record RelayTicket(string Line, string SystemPrompt, DateTimeOffset ExpiresAt, CallSummaryRequest? Summary = null, bool BookingEnabled = false, bool TransferConfigured = false, bool DeliverSummaryAlways = true, Guid? KnowledgeRunId = null, VoiceReceptionistConfig? Config = null, string? SpokenGreeting = null);

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
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, RelayTicket> _tickets = new(StringComparer.Ordinal);

    public string Issue(
        string line, string systemPrompt, CallSummaryRequest? summary = null, bool bookingEnabled = false, bool transferConfigured = false,
        bool deliverSummaryAlways = true, Guid? knowledgeRunId = null, VoiceReceptionistConfig? config = null, string? spokenGreeting = null)
    {
        var now = clock.GetUtcNow();
        foreach (var (key, ticket) in _tickets)
            if (ticket.ExpiresAt <= now) _tickets.TryRemove(key, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[token] = new RelayTicket(line, systemPrompt, now + Lifetime, summary, bookingEnabled, transferConfigured, deliverSummaryAlways, knowledgeRunId, config, spokenGreeting);
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
