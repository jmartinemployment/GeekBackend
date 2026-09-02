namespace GeekApplication.Models.GeekCrawler;

public static class GeekCrawlerUrlKeys
{
    /// <summary>
    /// Stable dedup key for BFS enqueue. Includes query string when present so distinct
    /// pages like <c>/pricing?tab=annual</c> are not collapsed with <c>/pricing</c>.
    /// </summary>
    public static string CrawlKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
            path = "/";

        var key = $"{uri.Scheme}://{uri.Host}{path}";
        if (!string.IsNullOrEmpty(uri.Query))
            key += uri.Query;

        return key;
    }
}
