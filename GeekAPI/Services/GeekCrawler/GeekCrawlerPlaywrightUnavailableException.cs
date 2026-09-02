namespace GeekAPI.Services.GeekCrawler;

/// <summary>Playwright browser could not be started or is unavailable.</summary>
public sealed class GeekCrawlerPlaywrightUnavailableException : Exception
{
    public GeekCrawlerPlaywrightUnavailableException(string message)
        : base(message)
    {
    }
}
