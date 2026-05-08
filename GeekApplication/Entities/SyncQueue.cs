namespace GeekApplication.Entities;

public class SyncQueue
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TargetDeviceId { get; set; }
    public required string Payload { get; set; } // JSON serialized sync event
    public required string Status { get; set; } // 'pending', 'acknowledged', 'failed'
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ExpiresAt { get; set; } // 24-hour TTL

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual DeviceOauth TargetDevice { get; set; } = null!;
}
