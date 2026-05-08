namespace GeekApplication.Entities;

public class TwoFactorTrustedDevice
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string DeviceFingerprint { get; set; }
    public DateTime TrustedAt { get; set; }
    public DateTime? ExpiresAt { get; set; } // Optional: 30-day trust period
    public bool IsRevoked { get; set; }

    // Navigation property
    public virtual User User { get; set; } = null!;
}
