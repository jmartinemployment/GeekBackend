namespace GeekAPI.Middleware;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        if (context.Request.IsHttps)
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        if (IsHtmlResponse(context))
        {
            headers["Content-Security-Policy"] =
                "default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'";
        }

        await _next(context);
    }

    private static bool IsHtmlResponse(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/Account", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/Consent", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/Redirect", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase);
}
