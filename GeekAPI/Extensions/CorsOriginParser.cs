namespace GeekAPI.Extensions;

public static class CorsOriginParser
{
    private static readonly string[] DefaultOrigins =
    [
        "http://localhost:3000",
        "https://seo.geekatyourspot.com",
    ];

    /// <summary>
    /// Reads comma-separated absolute origins from CORS_ORIGINS, or returns defaults when unset.
    /// </summary>
    public static string[] GetAllowedOrigins()
    {
        var raw = Environment.GetEnvironmentVariable("CORS_ORIGINS");
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultOrigins;

        var parsed = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeOrigin)
            .Where(o => Uri.TryCreate(o, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parsed.Length > 0 ? parsed : DefaultOrigins;
    }

    private static string NormalizeOrigin(string origin) =>
        origin.EndsWith('/') ? origin[..^1] : origin;
}
