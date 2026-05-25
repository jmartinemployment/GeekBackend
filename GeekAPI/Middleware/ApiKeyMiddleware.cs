namespace GeekAPI.Middleware;

public class ApiKeyMiddleware
{
    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/Health",
        "/hello",
        "/favicon.ico",
        "/robots.txt",
        "/api/case-studies",
        "/api/departments",
        "/api/use-cases"
    };

    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var normalizedPath = NormalizePath(context.Request.Path.Value);
        if (PublicPaths.Contains(normalizedPath)
            || normalizedPath.StartsWith("/connect", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/.well-known", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/Consent", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/Redirect", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/Error", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/hubs/sync", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "API key is missing" });
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("GEEK_BACKEND_API_KEY");
        if (apiKey == null || apiKey != extractedApiKey.ToString())
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Invalid API key" });
            return;
        }

        await _next(context);
    }

    // Trims trailing slashes so /favicon.ico/ matches /favicon.ico in PublicPaths.
    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var p = path;
        while (p.Length > 1 && p.EndsWith('/'))
        {
            p = p[..^1];
        }

        return p;
    }
}
