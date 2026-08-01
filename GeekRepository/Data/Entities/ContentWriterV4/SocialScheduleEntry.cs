namespace GeekRepository.Data.Entities.ContentWriterV4;

public class SocialScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid AssetId { get; set; }
    public Guid AssetVersionId { get; set; }
    public string Channel { get; set; } = "linkedin";
    public DateTime ScheduledAtUtc { get; set; }
    public string Status { get; set; } = "scheduled"; // scheduled, posted, cancelled
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
