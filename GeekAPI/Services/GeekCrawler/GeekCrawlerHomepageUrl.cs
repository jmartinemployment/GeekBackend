namespace GeekAPI.Services.GeekCrawler;

/// <summary>Normalize operator site URL → origin homepage.</summary>
public static class GeekCrawlerHomepageUrl
{
    public static bool TryNormalize(string? siteUrl, out string homepageUrl)
    {
        homepageUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(siteUrl))
            return false;

        var raw = siteUrl.Trim();
        if (!raw.Contains("://", StringComparison.Ordinal))
            raw = "https://" + raw;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        homepageUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";
        return true;
    }
}
