using GeekAPI.Controllers.Social.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.Social.Realtime;

/// <summary>
/// Sends GeekSocial's messages to every connection on <see cref="SocialRealtimeHub"/>. Only a caller
/// holding geeksocial.manage can connect, so every connection is an operator of the same Pages.
/// </summary>
public sealed class SocialRealtimeNotifier : ISocialRealtime
{
    public const string DraftChanged = "draftChanged";
    public const string CommentChanged = "commentChanged";
    public const string AccountChanged = "accountChanged";

    private readonly IHubContext<SocialRealtimeHub> _hub;
    private readonly ILogger<SocialRealtimeNotifier> _logger;

    public SocialRealtimeNotifier(IHubContext<SocialRealtimeHub> hub, ILogger<SocialRealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public Task DraftChangedAsync(Guid draftId, Guid accountId, string status, CancellationToken ct = default) =>
        SendAsync(DraftChanged, new { draftId, accountId, status }, ct);

    public Task CommentChangedAsync(Guid eventId, Guid accountId, string outcome, CancellationToken ct = default) =>
        SendAsync(CommentChanged, new { eventId, accountId, outcome }, ct);

    public Task AccountChangedAsync(Guid accountId, string status, CancellationToken ct = default) =>
        SendAsync(AccountChanged, new { accountId, status }, ct);

    private async Task SendAsync(string message, object body, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.All.SendAsync(message, body, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The change is stored already; the screen sees it at its next read. Logged, not swallowed.
            _logger.LogError(ex, "GeekSocial could not send the {Message} message to the screen.", message);
        }
    }
}
