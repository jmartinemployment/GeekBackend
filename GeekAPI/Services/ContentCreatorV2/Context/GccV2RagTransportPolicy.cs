namespace GeekAPI.Services.ContentCreatorV2.Context;

/// <summary>
/// Transport policy for Geek-Crawler-Rag base URLs.
/// Prefer HTTPS. Localhost HTTP is for local/dev. A temporary host allowlist covers the
/// plaintext VPS IP until S6 TLS cutover completes.
/// </summary>
public static class GccV2RagTransportPolicy
{
    public const string InsecureHttpHostsEnv = "GEEK_CRAWLER_RAG_ALLOW_INSECURE_HTTP_HOSTS";

    public static bool IsAllowedBaseAddress(Uri baseAddress)
    {
        if (baseAddress.Scheme == Uri.UriSchemeHttps)
            return true;

        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(baseAddress.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || baseAddress.Host is "127.0.0.1" or "::1")
            return true;

        var raw = Environment.GetEnvironmentVariable(InsecureHttpHostsEnv) ?? "";
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, baseAddress.Host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
