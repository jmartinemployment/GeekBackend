namespace GeekApplication.Models.GeekCrawler;

public static class GeekCrawlerUrlKeys
{
    public static string CrawlKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var key = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (key.Length == 0)
            key = uri.GetLeftPart(UriPartial.Authority);
        return key;
    }
}
