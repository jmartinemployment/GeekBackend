namespace GeekAPI.Middleware;

/// <summary>
/// Returns 410 Gone for removed legacy platform auth routes (M4–M6). Identity is GeekOAuth only.
/// </summary>
public sealed class LegacyAuthRetiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (IsLegacyAuthPath(path))
        {
            context.Response.StatusCode = StatusCodes.Status410Gone;
            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = "Legacy platform auth was retired. Use GeekOAuth for login and tokens.",
                code = "legacy_auth_retired",
            });
            return;
        }

        await next(context);
    }

    private static bool IsLegacyAuthPath(string path) =>
        path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/hubs/sync", StringComparison.OrdinalIgnoreCase);
}
