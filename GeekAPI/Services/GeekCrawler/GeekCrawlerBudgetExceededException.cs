namespace GeekAPI.Services.GeekCrawler;

/// <summary>Thrown when a crawl hits wall-clock or bytes-fetched budget.</summary>
public sealed class GeekCrawlerBudgetExceededException : Exception
{
    public GeekCrawlerBudgetExceededException(string message) : base(message)
    {
    }
}
