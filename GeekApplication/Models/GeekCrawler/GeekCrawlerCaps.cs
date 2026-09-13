namespace GeekApplication.Models.GeekCrawler;

/// <summary>Polite crawl timing and resource budgets for Geek-Crawler.</summary>
public static class GeekCrawlerCaps
{
    public const int BatchSaveSize = 5;
    public const int MaxBatchSaveSize = 20;
    public const int DefaultHostDelaySeconds = 12;
    public const int NavigationTimeoutMs = 30_000;
    public const int RenderQuiescenceCapMs = 5_000;
    public const int MaxSitemapUrlsPerOrigin = 5_000;

    /// <summary>Server-side admission: max seed URLs on StartCrawl / ingest.</summary>
    public const int MaxSeedsPerRequest = 25;

    /// <summary>Hard cap on pages attempted across all origins in one run.</summary>
    public const int MaxUrlsPerRun = 10_000;

    /// <summary>Max concurrent crawl runs per owner (enforced at start).</summary>
    public const int MaxConcurrentCrawlsPerOwner = 3;

    /// <summary>Wall-clock cancel for a single run.</summary>
    public const int MaxCrawlDurationMinutes = 180;

    /// <summary>Cumulative response body bytes retained per run (HTML snapshots).</summary>
    public const long MaxBytesFetchedPerRun = 500L * 1024 * 1024;

    /// <summary>Redirect hops allowed per navigation (Playwright / HTTP).</summary>
    public const int MaxRedirectsPerNavigation = 5;

    public const string BotName = "geekatyourspotbot";
    public const string BotContactEmail = "jeffm@geekatyourspot.com";
    public const string BotContactUrl = "https://geekatyourspot.com";

    public const string UserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/134.0.6998.35 Mobile Safari/537.36 (compatible; geekatyourspotbot/1.0; " +
        "+mailto:jeffm@geekatyourspot.com; +https://geekatyourspot.com)";
}
