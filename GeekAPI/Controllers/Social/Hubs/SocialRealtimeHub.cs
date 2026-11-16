using System.Security.Claims;
using GeekAPI.Auth;
using GeekApplication.Interfaces.Social;
using GeekApplication.Models.Social;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.Social.Hubs;

/// <summary>
/// GeekSocial's live updates at <c>/hubs/social-realtime</c>. The server only sends; the screen
/// listens for draftChanged, commentChanged and accountChanged (ids and states, never text) and reads
/// what changed through api/social/*. It never polls, and no raw WebSocket is written for it.
/// A connection joins the group of the tenant its signed-in person owns, and a message is sent to that
/// group alone, so one tenant never hears about another's accounts or drafts. A person who has not signed
/// up has no tenant and is joined to no group: they receive nothing, and the connection is refused so that
/// the screen shows the failure instead of sitting quiet.
/// </summary>
[Authorize(Policy = GeekSocialAuthConstants.ManagePolicy)]
public sealed class SocialRealtimeHub : Hub
{
    private readonly ISocialStore _store;
    private readonly ILogger<SocialRealtimeHub> _logger;

    public SocialRealtimeHub(ISocialStore store, ILogger<SocialRealtimeHub> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>The SignalR group of one tenant's connections.</summary>
    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:N}";

    public override async Task OnConnectedAsync()
    {
        var subject = Context.User?.FindFirstValue("sub") ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var owner) || owner == Guid.Empty)
            throw new HubException("The token's subject is not a user id.");

        var tenant = await _store.GetTenantByOwnerAsync(owner, Context.ConnectionAborted).ConfigureAwait(false);
        if (!tenant.IsSuccess)
        {
            if (tenant.ErrorCode != SocialFailureCodes.NotFound)
                _logger.LogError("GeekSocial could not find the tenant for a live connection: {Code}. {Error}", tenant.ErrorCode, tenant.Error);
            throw new HubException(tenant.ErrorCode == SocialFailureCodes.NotFound
                ? "Sign up for GeekSocial before connecting."
                : "GeekSocial could not find your account. Try again.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenant.Value!.Id)).ConfigureAwait(false);
        await base.OnConnectedAsync().ConfigureAwait(false);
    }
}
