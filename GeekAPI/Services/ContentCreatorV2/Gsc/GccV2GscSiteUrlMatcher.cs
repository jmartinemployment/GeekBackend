namespace GeekAPI.Services.ContentCreatorV2.Gsc;

/// <summary>
/// Maps a preferred URL to the exact siteUrl string returned by the Search Console API.
/// Copied from Geek-SEO GscSiteUrlMatcher; GSC rejects queries when the path segment mismatches.
/// </summary>
public static class GccV2GscSiteUrlMatcher
{
    public static string? Match(IReadOnlyList<string> accessibleSites, string? preferredSiteUrl)
    {
        if (accessibleSites.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(preferredSiteUrl))
            return accessibleSites[0];

        var preferred = preferredSiteUrl.Trim();

        foreach (var site in accessibleSites)
        {
            if (string.Equals(site, preferred, StringComparison.Ordinal))
                return site;
        }

        var matches = accessibleSites.Where(site => SitesEquivalent(site, preferred)).ToList();
        if (matches.Count == 0)
            return null;

        return matches
            .OrderByDescending(IsUrlPrefixProperty)
            .ThenByDescending(site => site.EndsWith('/') ? 1 : 0)
            .First();
    }

    internal static bool SitesEquivalent(string gscSiteUrl, string preferredSiteUrl)
    {
        if (string.Equals(gscSiteUrl, preferredSiteUrl, StringComparison.OrdinalIgnoreCase))
            return true;

        if (gscSiteUrl.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
        {
            var domain = gscSiteUrl["sc-domain:".Length..].Trim().ToLowerInvariant();
            var preferredHost = HostFromUrl(preferredSiteUrl);
            if (preferredHost is null)
                return false;

            var normalizedPreferred = NormalizeHost(preferredHost);
            return normalizedPreferred == domain
                || normalizedPreferred == $"www.{domain}";
        }

        var gscHost = HostFromUrl(gscSiteUrl);
        var host = HostFromUrl(preferredSiteUrl);
        if (gscHost is null || host is null)
            return false;

        return NormalizeHost(gscHost) == NormalizeHost(host);
    }

    private static bool IsUrlPrefixProperty(string siteUrl) =>
        siteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || siteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? HostFromUrl(string siteUrl)
    {
        if (siteUrl.StartsWith("sc-domain:", StringComparison.OrdinalIgnoreCase))
            return siteUrl["sc-domain:".Length..].Trim();

        if (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var uri))
            return null;

        return uri.Host;
    }

    private static string NormalizeHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized.StartsWith("www.", StringComparison.Ordinal)
            ? normalized[4..]
            : normalized;
    }
}
