namespace GeekRepository.Middleware;

/// <summary>
/// Returns 410 Gone for removed <c>repo/auth/*</c> routes (M5). Auth storage is GeekOAuth only.
/// </summary>
public sealed class LegacyAuthRetiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/repo/auth", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Legacy platform auth storage was retired. Use GeekOAuth.",
                code = "legacy_auth_retired",
            });
            return;
        }

        await next(context);
    }
}
