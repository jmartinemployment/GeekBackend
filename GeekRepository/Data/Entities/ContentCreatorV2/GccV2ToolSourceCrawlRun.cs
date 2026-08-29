namespace GeekRepository.Data.Entities.ContentCreatorV2;

public class GccV2ToolSourceCrawlRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreateId { get; set; }
    public string Status { get; set; } = "pending";
    public string SeedUrlsJson { get; set; } = "[]";
    public string? HostProgressJson { get; set; }
    public string? PartnerResearchJson { get; set; }
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
