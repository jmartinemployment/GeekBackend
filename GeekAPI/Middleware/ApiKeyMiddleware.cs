using System.Security.Claims;

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
        if (PublicPaths.Contains(normalizedPath) || IsPublicBlogRead(context))
        {
            await _next(context);
            return;
        }

        if (normalizedPath.StartsWith("/api/seo/internal", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("/api/gtm/internal", StringComparison.OrdinalIgnoreCase))
        {
            if (TryAuthenticateSeoInternal(context))
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, error = "Authentication required for internal API." });
            return;
        }

        // Try Bearer token authentication first (for NextJS frontend)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var authValue = authHeader.ToString();
            if (authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authValue["Bearer ".Length..].Trim();
                if (TryAuthenticateBearer(context, token))
                {
                    await _next(context);
                    return;
                }
            }
        }

        // Fall back to X-API-Key authentication (for internal services)
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

        if (context.Request.Headers.TryGetValue("X-Geek-User-Id", out var userIdHeader)
            && Guid.TryParse(userIdHeader, out var userId))
        {
            var identity = new ClaimsIdentity("ApiKey");
            identity.AddClaim(new Claim("sub", userId.ToString()));
            identity.AddClaim(new Claim("geek_user_id", userId.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
            context.User = new ClaimsPrincipal(identity);
        }

        await _next(context);
    }

    private static bool TryAuthenticateBearer(HttpContext context, string token)
    {
        try
        {
            // For development: accept any non-empty token as valid
            // In production, validate JWT signature using a key
            if (string.IsNullOrWhiteSpace(token))
                return false;

            // Try to parse as direct UUID first (for testing)
            if (Guid.TryParse(token, out var directUserId))
            {
                ApplyBearerAuth(context, directUserId);
                return true;
            }

            // Try to parse JWT to extract claims
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            // For simplicity in dev, extract sub claim from JWT payload
            // This is NOT secure for production - you need proper JWT validation
            try
            {
                var payload = parts[1];
                // Add padding if needed
                var paddedPayload = payload.Length % 4 == 0 ? payload : payload + new string('=', 4 - payload.Length % 4);
                var decodedBytes = Convert.FromBase64String(paddedPayload);
                var json = System.Text.Encoding.UTF8.GetString(decodedBytes);

                // Simple JSON parsing to extract sub
                if (json.Contains("\"sub\""))
                {
                    var subMatch = System.Text.RegularExpressions.Regex.Match(json, @"""sub""\s*:\s*""([^""]+)""");
                    if (subMatch.Success && Guid.TryParse(subMatch.Groups[1].Value, out var userId))
                    {
                        ApplyBearerAuth(context, userId);
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyBearerAuth(HttpContext context, Guid userId)
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim("sub", userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        context.User = new ClaimsPrincipal(identity);
    }

    private static bool TryAuthenticateSeoInternal(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var extractedApiKey))
            return false;

        var apiKey = Environment.GetEnvironmentVariable("GEEK_BACKEND_API_KEY");
        if (apiKey is null || apiKey != extractedApiKey.ToString())
            return false;

        ApplyUserFromHeaders(context);
        return true;
    }

    private static void ApplyUserFromHeaders(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Geek-User-Id", out var userIdHeader)
            && Guid.TryParse(userIdHeader, out var userId))
        {
            var identity = new ClaimsIdentity("SeoInternal");
            identity.AddClaim(new Claim("sub", userId.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
            context.User = new ClaimsPrincipal(identity);
        }
    }

    private static bool IsPublicBlogRead(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;
        var path = NormalizePath(context.Request.Path.Value);
        if (!path.StartsWith("/api/blog/", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.Equals("/api/blog/all", StringComparison.OrdinalIgnoreCase))
        {
            // Published catalog only — used by geekatyourspot SSG/ISR at build and runtime.
            return string.Equals(
                context.Request.Query["status"],
                "published",
                StringComparison.OrdinalIgnoreCase);
        }
        if (path.StartsWith("/api/blog/by-id/", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
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
