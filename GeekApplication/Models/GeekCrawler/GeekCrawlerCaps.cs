namespace GeekApplication.Models.GeekCrawler;

/// <summary>Polite crawl timing limits for Geek-Crawler.</summary>
public static class GeekCrawlerCaps
{
    public const int BatchSaveSize = 20;
    public const int DefaultHostDelaySeconds = 12;
    public const int NavigationTimeoutMs = 15_000;
    public const int RenderQuiescenceCapMs = 5_000;

    public const string BotName = "geekatyourspotbot";
    public const string BotContactEmail = "jeffm@geekatyourspot.com";
    public const string BotContactUrl = "https://geekatyourspot.com";

    public const string UserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/134.0.6998.35 Mobile Safari/537.36 (compatible; geekatyourspotbot/1.0; " +
        "+mailto:jeffm@geekatyourspot.com; +https://geekatyourspot.com)";
}
