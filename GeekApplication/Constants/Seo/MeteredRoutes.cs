namespace GeekApplication.Constants.Seo;

/// <summary>Maps HTTP method + path to usage feature keys (see UsageLimits.cs).</summary>
public static class MeteredRoutes
{
    private static readonly (string Method, string PathPrefix, string Feature)[] ExactPrefixes =
    [
        ("POST", "/api/seo/content", "content_document"),
        ("POST", "/api/seo/briefs/generate", "content_brief"),
        ("POST", "/api/seo/writing/draft", "ai_draft"),
        ("POST", "/api/seo/writing/full-article", "full_article"),
        ("POST", "/api/seo/writing/humanize", "humanize"),
        ("POST", "/api/seo/writing/detect", "ai_detect"),
        ("POST", "/api/seo/writing/bulk", "bulk_job"),
        ("POST", "/api/seo/keywords/research", "keyword_lookup"),
        ("POST", "/api/seo/audit/page", "page_audit"),
        ("POST", "/api/seo/audit/site", "site_audit"),
        ("GET", "/api/seo/serp/deep", "deep_serp"),
        ("POST", "/api/seo/plagiarism/check", "plagiarism_check"),
        ("POST", "/api/seo/topical-map/generate", "topical_map_refresh"),
    ];

    public static string? GetFeatureKey(string method, string path)
    {
        if (!path.StartsWith("/api/seo", StringComparison.OrdinalIgnoreCase))
            return null;

        var upperMethod = method.ToUpperInvariant();

        if (upperMethod == "POST"
            && path.Contains("/auto-optimize", StringComparison.OrdinalIgnoreCase))
        {
            return "auto_optimize";
        }

        foreach (var (routeMethod, prefix, feature) in ExactPrefixes)
        {
            if (!string.Equals(routeMethod, upperMethod, StringComparison.Ordinal))
                continue;
            if (routeMethod == "POST" && string.Equals(prefix, "/api/seo/content", StringComparison.Ordinal))
            {
                if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return feature;
                continue;
            }
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return feature;
        }

        return null;
    }
}
