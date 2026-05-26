using Microsoft.AspNetCore.Mvc;

namespace GeekAPI.Controllers.Seo;

/// <summary>
/// Authorized data pipe: forwards product calls to GeekRepository <c>repo/seo/*</c>.
/// No SERP, scoring, or AI logic — GeekSeoBackend owns providers.
/// </summary>
[ApiController]
[Route("api/seo/internal")]
public sealed class SeoInternalProxyController(IHttpClientFactory httpClientFactory) : ControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    [Route("{**path}")]
    public Task Proxy(string path, CancellationToken ct) =>
        ProxyAsync($"repo/seo/{path}", ct);

    private async Task ProxyAsync(string repoPath, CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            await Response.WriteAsJsonAsync(new { error = "Authentication required." }, ct);
            return;
        }

        var http = httpClientFactory.CreateClient("GeekRepository");
        var target = new Uri(http.BaseAddress!, repoPath);
        var uriBuilder = new UriBuilder(target) { Query = BuildQueryString(userId) };

        using var request = new HttpRequestMessage(new HttpMethod(Request.Method), uriBuilder.Uri);

        if (Request.ContentLength > 0 || Request.Headers.ContainsKey("Content-Type"))
        {
            request.Content = new StreamContent(Request.Body);
            if (Request.ContentType is not null)
                request.Content.Headers.TryAddWithoutValidation("Content-Type", Request.ContentType);
        }

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        foreach (var header in response.Content.Headers)
            Response.Headers[header.Key] = header.Value.ToArray();
        Response.Headers.Remove("transfer-encoding");

        await response.Content.CopyToAsync(Response.Body, ct);
    }

    private Guid ResolveUserId()
    {
        var sub = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(sub, out var fromClaim) && fromClaim != Guid.Empty)
            return fromClaim;

        if (Guid.TryParse(Request.Query["userId"], out var fromQuery) && fromQuery != Guid.Empty)
            return fromQuery;

        return Guid.Empty;
    }

    private string BuildQueryString(Guid userId)
    {
        var pairs = Request.Query
            .Where(q => !string.Equals(q.Key, "userId", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"))
            .ToList();
        pairs.Insert(0, $"userId={userId}");
        return string.Join('&', pairs);
    }
}
