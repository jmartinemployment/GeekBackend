namespace GeekAPI.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string ApiKeyHeader = "X-API-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation for /health
        if (context.Request.Path == "/health")
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
}
