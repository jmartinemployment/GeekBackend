namespace GeekAPI.Extensions;

public static class CorsOriginParser
{
    private static readonly string[] DefaultOrigins =
    [
        "http://localhost:3000",
        "http://localhost:3003",
        "http://localhost:3004",
        "https://geekatyourspot.com",
        "https://www.geekatyourspot.com",
        "https://admin.geekatyourspot.com",
        "https://content-writer-v3.vercel.app",
        "https://content-writer-eta.vercel.app",
        "https://content-writer-jeff-martins-projects-66716453.vercel.app",
        "https://geek-content-creator.vercel.app",
        "https://content-creator-v2.vercel.app",
        "https://content-creator-v2-phi.vercel.app",
    ];

    /// <summary>
    /// Reads comma-separated absolute origins from CORS_ORIGINS, or returns defaults when unset.
    /// Always unions with <see cref="DefaultOrigins"/> so product apps stay allowed when the env var is narrow.
    /// </summary>
    public static string[] GetAllowedOrigins()
    {
        var raw = Environment.GetEnvironmentVariable("CORS_ORIGINS");
        IEnumerable<string> fromEnv = [];
        if (!string.IsNullOrWhiteSpace(raw))
        {
            fromEnv = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeOrigin)
                .Where(o => Uri.TryCreate(o, UriKind.Absolute, out var uri)
                            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        }

        return DefaultOrigins
            .Concat(fromEnv)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Exact list match, plus Content Writer / Content Creator Vercel production/preview hostnames.
    /// </summary>
    public static bool IsOriginAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        var normalized = NormalizeOrigin(origin);
        if (GetAllowedOrigins().Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps)
            return false;

        // content-writer-v3.vercel.app and content-writer-v3-*.vercel.app (preview deploys)
        if (string.Equals(uri.Host, "content-writer-v3.vercel.app", StringComparison.OrdinalIgnoreCase))
            return true;

        if (uri.Host.StartsWith("content-writer-v3-", StringComparison.OrdinalIgnoreCase)
               && uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(uri.Host, "geek-content-creator.vercel.app", StringComparison.OrdinalIgnoreCase))
            return true;

        if (uri.Host.StartsWith("geek-content-creator-", StringComparison.OrdinalIgnoreCase)
               && uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(uri.Host, "content-creator-v2.vercel.app", StringComparison.OrdinalIgnoreCase))
            return true;

        return uri.Host.StartsWith("content-creator-v2-", StringComparison.OrdinalIgnoreCase)
               && uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeOrigin(string origin) =>
        origin.EndsWith('/') ? origin[..^1] : origin;
}
