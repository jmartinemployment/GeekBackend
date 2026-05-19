using System.Security.Claims;

namespace GeekAPI.Middleware;

/// <summary>
/// When JWT_KEY is not set, allows local dev by reading X-User-Id header (Guid).
/// Never enable in production without JWT.
/// </summary>
public sealed class DevUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true
            && context.Request.Headers.TryGetValue("X-User-Id", out var userIdHeader)
            && Guid.TryParse(userIdHeader.ToString(), out var userId))
        {
            var identity = new ClaimsIdentity(
                [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                "DevUser");
            context.User = new ClaimsPrincipal(identity);
        }

        await next(context);
    }
}
