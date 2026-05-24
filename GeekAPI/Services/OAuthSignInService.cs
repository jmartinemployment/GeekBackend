using System.Security.Claims;
using GeekAPI.Auth;
using GeekApplication.Entities;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace GeekAPI.Services;

public sealed class OAuthSignInService
{
    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        var identity = new ClaimsIdentity(
            authenticationType: GeekAuthConstants.CookieScheme,
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, user.Subject);
        identity.SetClaim(Claims.Name, user.Username);
        if (!string.IsNullOrWhiteSpace(user.Email))
            identity.SetClaim(Claims.Email, user.Email);

        identity.SetClaim("geek_user_id", user.Id.ToString());
        return new ClaimsPrincipal(identity);
    }

    public static async Task SignInAsync(HttpContext httpContext, User user, bool rememberMe, CancellationToken cancellationToken = default)
    {
        var principal = CreatePrincipal(user);
        await httpContext.SignInAsync(
            GeekAuthConstants.CookieScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = rememberMe,
                ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(14) : DateTimeOffset.UtcNow.AddHours(8)
            });
    }

    public static Task SignOutAsync(HttpContext httpContext, CancellationToken cancellationToken = default) =>
        httpContext.SignOutAsync(GeekAuthConstants.CookieScheme);
}
