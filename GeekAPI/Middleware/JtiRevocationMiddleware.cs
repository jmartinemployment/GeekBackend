using System.Security.Claims;
using GeekApplication.Interfaces;
using OpenIddict.Abstractions;

namespace GeekAPI.Middleware;

public sealed class JtiRevocationMiddleware
{
    private readonly RequestDelegate _next;

    public JtiRevocationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOAuthTokenRepository tokens)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirst(OpenIddictConstants.Claims.JwtId)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrWhiteSpace(jti))
            {
                var revoked = await tokens.IsTokenBlacklistedAsync(jti);
                if (revoked.IsSuccess && revoked.Value)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { success = false, error = "Token revoked" });
                    return;
                }
            }
        }

        await _next(context);
    }
}
