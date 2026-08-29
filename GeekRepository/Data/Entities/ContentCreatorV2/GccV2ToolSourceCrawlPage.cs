namespace GeekRepository.Data.Entities.ContentCreatorV2;

public class GccV2ToolSourceCrawlPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string FinalUrl { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool RobotsAllowed { get; set; } = true;
    public string? Html { get; set; }
    public DateTimeOffset CrawledAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
