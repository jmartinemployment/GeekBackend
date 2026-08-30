namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string CrawlType { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string SeedUrlsJson { get; set; } = "[]";
    public string? HostProgressJson { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
