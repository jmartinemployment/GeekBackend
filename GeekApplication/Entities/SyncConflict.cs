namespace GeekApplication.Entities;

public class SyncConflict
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string ResourceId { get; set; }
    public required string LocalVersion { get; set; } // JSON snapshot
    public required string RemoteVersion { get; set; } // JSON snapshot
    public DateTime LocalUpdatedAt { get; set; }
    public DateTime RemoteUpdatedAt { get; set; }
    public DateTime DetectedAt { get; set; }
    public string? ResolvedVersion { get; set; } // NULL if unresolved
    public DateTime? ResolvedAt { get; set; }

    // Navigation property
    public virtual User User { get; set; } = null!;
}
