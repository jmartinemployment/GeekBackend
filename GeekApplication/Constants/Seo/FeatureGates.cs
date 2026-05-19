namespace GeekApplication.Constants.Seo;

/// <summary>Minimum subscription tier per API path prefix (longest match wins).</summary>
public static class FeatureGates
{
    public static readonly (string PathPrefix, SubscriptionTier MinTier)[] Gates =
    [
        ("/api/seo/gsc", SubscriptionTier.Professional),
        ("/api/seo/rankings", SubscriptionTier.Professional),
        ("/api/seo/audit/site", SubscriptionTier.Professional),
        ("/api/seo/reports", SubscriptionTier.Professional),
        ("/api/seo/analytics/ga4", SubscriptionTier.Professional),
        ("/api/seo/content-audit", SubscriptionTier.Professional),
        ("/api/seo/topical-map", SubscriptionTier.Professional),
        ("/api/seo/cannibalization", SubscriptionTier.Professional),
        ("/api/seo/geo", SubscriptionTier.Professional),
        ("/api/seo/writing/bulk", SubscriptionTier.Professional),
        ("/api/seo/v1", SubscriptionTier.Agency),
        ("/api/seo/serp/deep", SubscriptionTier.Starter),
        ("/api/seo/writing/full-article", SubscriptionTier.Starter),
        ("/api/seo/writing/humanize", SubscriptionTier.Starter),
        ("/api/seo/writing/detect", SubscriptionTier.Starter),
        ("/api/seo/plagiarism", SubscriptionTier.Starter),
        ("/api/seo/wordpress", SubscriptionTier.Starter),
        ("/api/seo/links", SubscriptionTier.Starter),
        ("/api/seo/keywords/cluster", SubscriptionTier.Starter),
        ("/api/seo/writing/outline", SubscriptionTier.Starter),
        ("/api/seo/writing/draft", SubscriptionTier.Starter),
        ("/api/seo/writing/optimize", SubscriptionTier.Starter),
    ];

    public static SubscriptionTier? GetRequiredTier(string path)
    {
        SubscriptionTier? required = null;
        var bestLen = -1;
        foreach (var (prefix, tier) in Gates)
        {
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (prefix.Length <= bestLen)
                continue;
            bestLen = prefix.Length;
            required = tier;
        }
        return required;
    }
}
