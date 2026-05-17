namespace GeekRepository.Middleware;

public class RepoApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string Header = "X-Repo-Key";

    public RepoApiKeyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var expectedKey = Environment.GetEnvironmentVariable("REPO_API_KEY");

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(Header, out var provided)
            || !string.Equals(provided, expectedKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }

        await _next(context);
    }
}
