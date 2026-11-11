using GeekAPI.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GeekAPI.Controllers.Social.Hubs;

/// <summary>
/// GeekSocial's live updates at <c>/hubs/social-realtime</c>. The server only sends; the screen
/// listens for draftChanged, commentChanged and accountChanged (ids and states, never text) and reads
/// what changed through api/social/*. It never polls, and no raw WebSocket is written for it.
/// </summary>
[Authorize(Policy = GeekSocialAuthConstants.ManagePolicy)]
public sealed class SocialRealtimeHub : Hub
{
}
