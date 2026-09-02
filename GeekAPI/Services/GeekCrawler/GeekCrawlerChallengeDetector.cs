namespace GeekAPI.Services.GeekCrawler;

/// <summary>Detects Cloudflare (and similar) challenge pages — fail clearly, no bypass.</summary>
public static class GeekCrawlerChallengeDetector
{
    public const string CloudflareChallengeReason =
        "Cloudflare challenge page detected — crawl cannot proceed without bypass tooling.";

    public static bool IsCloudflareChallenge(
        int statusCode,
        string? html,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        if (responseHeaders is not null)
        {
            foreach (var (name, value) in responseHeaders)
            {
                if (name.Equals("cf-mitigated", StringComparison.OrdinalIgnoreCase)
                    && value.Contains("challenge", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (string.IsNullOrWhiteSpace(html))
            return false;

        if (html.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase)
            || html.Contains("challenge-platform", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Checking your browser", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Just a moment...", StringComparison.OrdinalIgnoreCase))
            return true;

        return statusCode is 403 or 503
               && html.Contains("cloudflare", StringComparison.OrdinalIgnoreCase)
               && html.Contains("<title", StringComparison.OrdinalIgnoreCase);
    }
}
