namespace GeekRepository.Data.Entities.GeekCrawler;

public class GeekCrawlerSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = string.Empty;
    public string CrawlType { get; set; } = string.Empty;
    public string SeedUrlsJson { get; set; } = "[]";
    public string? SeedKey { get; set; }
    public int IntervalHours { get; set; } = 168;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset NextRunUtc { get; set; }
    public DateTimeOffset? LastStartedUtc { get; set; }
    public Guid? LastRunId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
