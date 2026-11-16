using GeekAPI.Controllers.Social.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Services.Social.Realtime;

/// <summary>
/// Sends GeekSocial's messages to the connections of one tenant on <see cref="SocialRealtimeHub"/>, and to no one
/// else: a message names accounts, drafts and states that belong to that tenant alone.
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

    public Task DraftChangedAsync(Guid tenantId, Guid draftId, Guid accountId, string status, CancellationToken ct = default) =>
        SendAsync(tenantId, DraftChanged, new { draftId, accountId, status }, ct);

    public Task CommentChangedAsync(Guid tenantId, Guid eventId, Guid accountId, string outcome, CancellationToken ct = default) =>
        SendAsync(tenantId, CommentChanged, new { eventId, accountId, outcome }, ct);

    public Task AccountChangedAsync(Guid tenantId, Guid accountId, string status, CancellationToken ct = default) =>
        SendAsync(tenantId, AccountChanged, new { accountId, status }, ct);

    private async Task SendAsync(Guid tenantId, string message, object body, CancellationToken ct)
    {
        try
        {
            await _hub.Clients.Group(SocialRealtimeHub.TenantGroup(tenantId)).SendAsync(message, body, ct).ConfigureAwait(false);
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
