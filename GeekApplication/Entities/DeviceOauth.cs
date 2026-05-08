namespace GeekApplication.Entities;

public class DeviceOauth
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string DeviceType { get; set; } // 'desktop', 'mobile', 'kiosk'
    public string? DeviceName { get; set; }
    public required string BiosId { get; set; } // Machine ID or BIOS UUID
    public required string DeviceFingerprint { get; set; } // SHA-256(machineId + biosUuid + platform)
    public required string Platform { get; set; } // 'win32', 'darwin', 'linux'
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsTrusted { get; set; }
    public bool IsRevoked { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
}
