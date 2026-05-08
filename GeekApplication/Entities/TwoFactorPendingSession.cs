namespace GeekApplication.Entities;

public class TwoFactorPendingSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string SessionCode { get; set; } // Temporary code linking browser to 2FA completion
    public required string DeviceFingerprint { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; } // 5-minute TTL
    public bool IsCompleted { get; set; }

    // Navigation property
    public virtual User User { get; set; } = null!;
}
