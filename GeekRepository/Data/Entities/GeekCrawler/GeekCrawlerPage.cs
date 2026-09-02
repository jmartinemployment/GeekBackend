namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool RobotsAllowed { get; set; }
    public string? Html { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
