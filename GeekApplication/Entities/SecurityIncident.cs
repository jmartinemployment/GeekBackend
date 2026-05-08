namespace GeekApplication.Entities;

public class SecurityIncident
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public required string IncidentType { get; set; } // 'brute_force', 'suspicious_location', 'token_theft_detected', 'device_revoked', 'jti_replay_detected'
    public required string Description { get; set; }
    public string? IpAddress { get; set; }
    public string? DeviceFingerprint { get; set; }
    public string? Evidence { get; set; } // JSON details for forensics
    public bool RequiresUserAction { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    // Indefinite retention per security requirements
    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual DeviceOauth? Device { get; set; }
}
