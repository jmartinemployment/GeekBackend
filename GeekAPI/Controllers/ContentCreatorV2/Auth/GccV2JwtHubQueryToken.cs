using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace GeekAPI.Controllers.ContentCreatorV2.Auth;

/// <summary>
/// SignalR WebSockets/SSE cannot set the <c>Authorization</c> header, so the JS client sends the
/// JWT as <c>access_token</c> on the query string instead. Pattern copied from Geek-SEO's
/// <c>JwtHubQueryToken</c> — new file, does not touch that project.
/// </summary>
internal static class GccV2JwtHubQueryToken
{
    public static void AcceptAccessTokenFromQuery(JwtBearerOptions options)
    {
        var previous = options.Events?.OnMessageReceived;
        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            if (previous is not null)
                await previous(context);

            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                context.Token = accessToken;
        };
    }
}
